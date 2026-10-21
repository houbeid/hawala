using Cash.Application.Abstractions;
using Cash.Domain;
using Cash.Domain.Movements;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Ledger.Contracts;
using Organization.Contracts;
using Parties.Contracts;
using Treasury.Contracts;

namespace Cash.Application.Features.Movements.CreateCashMovement;

/// <summary>
/// Enregistre un mouvement manuel de caisse (règles validées le 21 octobre 2026). Écriture au grand livre, dans la devise
/// de l'agence, dans la même transaction :
/// - encaissement : débit du compte de trésorerie / crédit du tiers (il nous doit moins, ou on lui doit plus) ;
/// - décaissement : débit du tiers / crédit du compte de trésorerie ;
/// - approvisionnement de caisse : débit du compte de trésorerie / crédit « Apports du patron » ;
/// - retrait du patron : débit « Apports du patron » / crédit du compte de trésorerie ;
/// - virement interne : débit du compte de destination / crédit du compte d'origine (même agence, même devise).
/// POINT OUVERT : le virement interne utilise la permission cash.create (choix à confirmer avec Hamade).
/// Un agent travaille sur son agence seulement ; le Superviseur choisit l'agence. Saisie en MRU convertie (× 10).
/// Même clé d'idempotence pour le même compte : le mouvement déjà enregistré est renvoyé.
/// POINT OUVERT : plafond de crédit d'un tiers sur un décaissement (il passe plus en rouge) : non appliqué pour l'instant.
/// POINT OUVERT : la date d'opération est lue en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// POINT OUVERT : l'audit arrivera avec le module Audit ; le mouvement garde déjà l'auteur.
/// </summary>
public sealed class CreateCashMovementCommandHandler(
    ICashMovementRepository movements,
    IAgencyDirectory agencies,
    IAgencyScope scope,
    ITreasuryDirectory treasury,
    IPartyDirectory parties,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<CreateCashMovementCommand, CreatedCashMovement>
{
    public async Task<Result<CreatedCashMovement>> Handle(CreateCashMovementCommand command, CancellationToken cancellationToken)
    {
        var key = command.IdempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > CashMovement.IdempotencyKeyMaxLength)
        {
            return CashErrors.Movement.IdempotencyKeyRequired;
        }

        var existing = await movements.FindByIdempotencyKeyAsync(command.ActorId, key, cancellationToken);
        if (existing is not null)
        {
            return ToResponse(existing, replayed: true);
        }

        if (!EnumNames.TryParse<CashMovementType>(command.Type, out var type))
        {
            return CashErrors.Movement.TypeInvalid;
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

        var requested = new[] { command.TreasuryAccountId, command.DestinationTreasuryAccountId }
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var accounts = requested.Count == 0 ? [] : await treasury.GetByIdsAsync(requested, cancellationToken);

        bool Usable(TreasuryAccountRef? a) =>
            a is not null && a.IsActive && a.AgencyId == agency.Value.Id && a.Currency == agency.Value.Currency;

        var account = accounts.FirstOrDefault(a => a.Id == command.TreasuryAccountId);
        if (!Usable(account))
        {
            return CashErrors.Movement.AccountNotAllowed;
        }

        // Même compte en destination : refusé plus loin par le Domain (SameAccount).
        TreasuryAccountRef? destination = null;
        if (command.DestinationTreasuryAccountId is { } destinationId && destinationId != account!.Id)
        {
            destination = accounts.FirstOrDefault(a => a.Id == destinationId);
            if (!Usable(destination))
            {
                return CashErrors.Movement.AccountNotAllowed;
            }
        }

        var now = clock.UtcNow;
        var created = CashMovement.Create(
            new NewCashMovement(
                agency.Value.Id, type, account!.Id, command.PartyId, amount.Value, command.Note, command.OperationAt ?? now,
                command.DestinationTreasuryAccountId),
            key,
            command.ActorId,
            now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var counterpart = created.Value.IsInternal
            ? destination!.LedgerAccountId
            : await ResolveCounterpartAsync(created.Value, cancellationToken);
        if (counterpart.IsFailure)
        {
            return counterpart.Error;
        }

        var movement = created.Value;
        // Virement interne : l'argent entre dans le compte de destination (la contrepartie) et sort du compte d'origine.
        var incoming = movement.IsIncoming;
        var entry = await ledger.PostAsync(
            new PostEntryRequest(
                DateOnly.FromDateTime(movement.OperationAt.UtcDateTime),
                $"Cash.{movement.Type}",
                "Cash",
                movement.Id,
                command.ActorId,
                Label(movement.Type),
                [
                    new PostEntryLine(account.LedgerAccountId, incoming ? EntrySide.Debit : EntrySide.Credit, movement.Money),
                    new PostEntryLine(counterpart.Value, incoming ? EntrySide.Credit : EntrySide.Debit, movement.Money),
                ]),
            cancellationToken);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        movement.AttachEntry(entry.Value);
        movements.Add(movement);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(movement, replayed: false);
    }

    private static CreatedCashMovement ToResponse(CashMovement movement, bool replayed) =>
        new(movement.Id, movement.Type.ToString(), movement.Amount, movement.Currency, replayed);

    private static string Label(CashMovementType type) => type switch
    {
        CashMovementType.Receipt => "Encaissement",
        CashMovementType.Payment => "Décaissement",
        CashMovementType.OwnerContribution => "Approvisionnement de caisse",
        CashMovementType.InternalTransfer => "Virement interne",
        _ => "Retrait du patron",
    };

    /// <summary>MRO ou FCFA selon l'agence ; MRU accepté pour une agence en ouguiya (× 10, règle validée).</summary>
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

    /// <summary>Compte de contrepartie : le tiers dans l'agence, ou « Apports du patron ».</summary>
    private async Task<Result<Guid>> ResolveCounterpartAsync(CashMovement movement, CancellationToken cancellationToken)
    {
        if (movement.PartyId is not { } partyId)
        {
            return await ledger.GetOrCreateSystemAccountAsync(
                SystemAccountRole.OwnerContributions, movement.AgencyId, movement.Currency, cancellationToken);
        }

        var party = await parties.GetAgencyAccountAsync(partyId, movement.AgencyId, cancellationToken);
        if (party is null)
        {
            return CashErrors.Movement.PartyNotFound;
        }

        return party.IsArchived ? CashErrors.Movement.PartyArchived : party.LedgerAccountId;
    }
}
