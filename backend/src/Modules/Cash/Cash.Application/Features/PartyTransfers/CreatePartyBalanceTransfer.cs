using Cash.Application.Abstractions;
using Cash.Domain;
using Cash.Domain.PartyTransfers;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Ledger.Contracts;
using Organization.Contracts;
using Parties.Contracts;

namespace Cash.Application.Features.PartyTransfers;

/// <summary>Transfert de solde entre deux tiers (permission parties.transfer_balance : Superviseur et Agent).</summary>
/// <param name="IdempotencyKey">En-tête Idempotency-Key, obligatoire.</param>
/// <param name="AgencyId">Agence : celle de l'agent (facultative pour lui) ; obligatoire pour le Superviseur.</param>
/// <param name="FromPartyId">Tiers débité : il nous doit davantage, ou on lui doit moins.</param>
/// <param name="ToPartyId">Tiers crédité : il nous doit moins, ou on lui doit davantage.</param>
/// <param name="Unit">Devise de l'agence (défaut) ou MRU (converti × 10, agence en ouguiya seulement).</param>
/// <param name="Note">Facultative (pas de motif obligatoire, décision du patron).</param>
/// <param name="OperationAt">Date et heure de l'opération ; maintenant par défaut.</param>
public sealed record CreatePartyBalanceTransferCommand(
    Guid ActorId,
    string? IdempotencyKey,
    Guid? AgencyId,
    Guid? FromPartyId,
    Guid? ToPartyId,
    decimal? Amount,
    string? Unit,
    string? Note,
    DateTimeOffset? OperationAt) : ICommand<CreatedPartyBalanceTransfer>;

/// <param name="Replayed">Vrai si la clé d'idempotence avait déjà servi : rien n'a été enregistré.</param>
public sealed record CreatedPartyBalanceTransfer(Guid Id, decimal Amount, Currency Currency, bool Replayed);

/// <summary>
/// Décisions du patron (2 novembre 2026) : même agence seulement (deux tiers, une devise), Superviseur et Agent, sans
/// motif, plafond du tiers débité bloquant. Écriture « Cash.PartyTransfer » dans la devise de l'agence : débit du tiers
/// débité / crédit du tiers crédité ; les caisses ne bougent pas, le budget de la caisse non plus (sauf un tiers hors
/// bilan). Le compte du tiers débité est verrouillé (FOR UPDATE) avant de lire son solde, comme pour un crédit.
/// Un compte limité ne voit que ses tiers (filtre du module Tiers). Opération courante : pas de ligne d'audit.
/// POINT OUVERT : la date d'opération est lue en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// </summary>
public sealed class CreatePartyBalanceTransferCommandHandler(
    IPartyBalanceTransferRepository transfers,
    IAgencyDirectory agencies,
    IAgencyScope scope,
    IPartyDirectory parties,
    IPartyCreditAccounts partyCredit,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<CreatePartyBalanceTransferCommand, CreatedPartyBalanceTransfer>
{
    public const string EntryType = "Cash.PartyTransfer";

    private const int LabelMaxLength = 200;

    public async Task<Result<CreatedPartyBalanceTransfer>> Handle(
        CreatePartyBalanceTransferCommand command, CancellationToken cancellationToken)
    {
        var key = command.IdempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > PartyBalanceTransfer.IdempotencyKeyMaxLength)
        {
            return CashErrors.Movement.IdempotencyKeyRequired;
        }

        var existing = await transfers.FindByIdempotencyKeyAsync(command.ActorId, key, cancellationToken);
        if (existing is not null)
        {
            return ToResponse(existing, replayed: true);
        }

        var agency = await ResolveAgencyAsync(command.AgencyId, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var amount = ReadAmount(command.Amount, command.Unit, agency.Value.Currency);
        if (amount.IsFailure)
        {
            return amount.Error;
        }

        var now = clock.UtcNow;
        var created = PartyBalanceTransfer.Create(
            agency.Value.Id, command.FromPartyId, command.ToPartyId, amount.Value, command.Note,
            command.OperationAt ?? now, key, command.ActorId, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var transfer = created.Value;
        var from = await VisiblePartyAsync(transfer.FromPartyId, transfer.AgencyId, cancellationToken);
        if (from.IsFailure)
        {
            return from.Error;
        }

        var to = await VisiblePartyAsync(transfer.ToPartyId, transfer.AgencyId, cancellationToken);
        if (to.IsFailure)
        {
            return to.Error;
        }

        var limit = await CheckLimitAsync(transfer, cancellationToken);
        if (limit.IsFailure)
        {
            return limit.Error;
        }

        var entry = await ledger.PostAsync(
            new PostEntryRequest(
                DateOnly.FromDateTime(transfer.OperationAt.UtcDateTime),
                EntryType,
                "Cash",
                transfer.Id,
                command.ActorId,
                Label(from.Value.Name, to.Value.Name),
                [
                    new PostEntryLine(from.Value.LedgerAccountId, EntrySide.Debit, transfer.Money),
                    new PostEntryLine(to.Value.LedgerAccountId, EntrySide.Credit, transfer.Money),
                ]),
            cancellationToken);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        transfer.AttachEntry(entry.Value);
        transfers.Add(transfer);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(transfer, replayed: false);
    }

    private static CreatedPartyBalanceTransfer ToResponse(PartyBalanceTransfer transfer, bool replayed) =>
        new(transfer.Id, transfer.Amount, transfer.Currency, replayed);

    /// <summary>Libellé du relevé des deux tiers : « Transfert de solde : A → B », coupé à 200 caractères.</summary>
    private static string Label(string fromName, string toName)
    {
        var label = $"Transfert de solde : {fromName} → {toName}";
        return label.Length <= LabelMaxLength ? label : label[..LabelMaxLength];
    }

    /// <summary>Le tiers dans l'agence, visible du compte connecté et non archivé.</summary>
    private async Task<Result<PartyAccountRef>> VisiblePartyAsync(Guid partyId, Guid agencyId, CancellationToken cancellationToken)
    {
        var party = await parties.GetAgencyAccountAsync(partyId, agencyId, cancellationToken);
        if (party is null)
        {
            return CashErrors.PartyTransfer.PartyNotFound;
        }

        return party.IsArchived ? CashErrors.PartyTransfer.PartyArchived : party;
    }

    /// <summary>Plafond du tiers débité (bloquant) : solde actuel dans l'agence + montant ≤ plafond. Compte verrouillé d'abord.</summary>
    private async Task<Result> CheckLimitAsync(PartyBalanceTransfer transfer, CancellationToken cancellationToken)
    {
        var account = await partyCredit.LockForCreditAsync(transfer.FromPartyId, transfer.AgencyId, cancellationToken);
        if (account is null)
        {
            return CashErrors.PartyTransfer.PartyNotFound;
        }

        if (account.CreditLimit is not { } limit)
        {
            return Result.Success();
        }

        var balances = await ledger.GetBalancesAsync([account.LedgerAccountId], asOf: null, cancellationToken);
        if (balances.IsFailure)
        {
            return balances.Error;
        }

        var current = balances.Value.Single().Balance.Amount;
        return current + transfer.Amount > limit ? CashErrors.PartyTransfer.LimitExceeded : Result.Success();
    }

    /// <summary>Devise de l'agence ; MRU accepté pour une agence en ouguiya (× 10, règle validée).</summary>
    private static Result<Money> ReadAmount(decimal? amount, string? unit, Currency currency)
    {
        if (amount is not { } value || value <= 0m)
        {
            return CashErrors.Movement.AmountInvalid;
        }

        if (string.IsNullOrWhiteSpace(unit) || string.Equals(unit.Trim(), currency.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Money.Create(value, currency);
        }

        return currency == Currency.MRO && string.Equals(unit.Trim(), "MRU", StringComparison.OrdinalIgnoreCase)
            ? Money.FromMru(value)
            : CashErrors.Movement.UnitInvalid;
    }

    /// <summary>Un agent travaille sur son agence seulement ; le Superviseur la choisit.</summary>
    private async Task<Result<AgencySummary>> ResolveAgencyAsync(Guid? requested, CancellationToken cancellationToken)
    {
        Guid agencyId;
        if (scope.SeesAllAgencies)
        {
            if (requested is not { } chosen)
            {
                return CashErrors.Movement.AgencyRequired;
            }

            agencyId = chosen;
        }
        else
        {
            if (scope.AgencyId is not { } own || (requested is { } asked && asked != own))
            {
                return CashErrors.Movement.AgencyNotAllowed;
            }

            agencyId = own;
        }

        var agency = await agencies.GetByIdAsync(agencyId, cancellationToken);
        return agency is null ? CashErrors.Movement.AgencyRequired : agency;
    }
}
