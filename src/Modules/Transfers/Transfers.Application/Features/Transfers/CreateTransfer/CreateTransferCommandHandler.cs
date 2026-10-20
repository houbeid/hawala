using System.Globalization;
using System.Security.Cryptography;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Ledger.Contracts;
using Organization.Contracts;
using Parties.Contracts;
using Rates.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Domain;
using Transfers.Domain.Transfers;
using Treasury.Contracts;

namespace Transfers.Application.Features.Transfers.CreateTransfer;

/// <summary>
/// Crée un transfert payé comptant. Règles validées :
/// - l'agence d'envoi crée le transfert : un agent ne peut créer que depuis la sienne ;
/// - prix de vente et prix d'achat = taux du jour de l'opération, fixés par le Superviseur (refus s'il manque) ;
/// - méthodes de paiement : comptes de trésorerie actifs de l'agence d'envoi, dans sa devise ;
/// - écriture au grand livre, équilibrée par devise, via le compte « Position de change » :
///   devise d'envoi : débit des caisses / crédit Position de change ;
///   devise de destination : débit Position de change / crédit Transferts à livrer ;
/// - crédit activé (permission transfers.credit) : la part non payée est débitée du tiers choisi, dans l'agence d'envoi
///   (il passe en rouge) ; tiers archivé refusé ; plafond bloquant pour tous, Superviseur compris :
///   solde du tiers dans l'agence + part à crédit ≤ plafond (0 = rien au-delà de ce qu'on lui doit, vide = pas de limite) ;
///   le compte du tiers est verrouillé pendant la transaction, pour que deux crédits simultanés ne dépassent pas le plafond ;
/// - une seule transaction commune (compteur de référence + transfert + écriture) ;
/// - même clé d'idempotence pour le même compte : le transfert déjà créé est renvoyé, sans son code.
/// POINT OUVERT : la date de l'opération est lue en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// POINT OUVERT : l'audit (qui a créé) arrivera avec le module Audit ; le transfert garde déjà l'auteur.
/// </summary>
public sealed class CreateTransferCommandHandler(
    ITransferRepository transfers,
    ITransferReferenceGenerator references,
    IWithdrawalCodeHasher codeHasher,
    IAgencyDirectory agencies,
    IAgencyScope scope,
    IPermissionChecker permissions,
    IPartyCreditAccounts partyCredit,
    IDailyRates dailyRates,
    ITreasuryDirectory treasury,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<CreateTransferCommand, CreatedTransfer>
{
    public const string EntryType = "Transfers.Created";
    public const string WithdrawalCodeLength = "D6";

    public async Task<Result<CreatedTransfer>> Handle(CreateTransferCommand command, CancellationToken cancellationToken)
    {
        var key = command.IdempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > Transfer.IdempotencyKeyMaxLength)
        {
            return TransfersErrors.Transfer.IdempotencyKeyRequired;
        }

        var existing = await transfers.FindByIdempotencyKeyAsync(command.ActorId, key, cancellationToken);
        if (existing is not null)
        {
            return ToResponse(existing, withdrawalCode: null, replayed: true);
        }

        if (command.CreditPartyId is not null
            && !await permissions.HasPermissionAsync(command.ActorId, Permissions.TransfersCredit, cancellationToken))
        {
            return TransfersErrors.Credit.NotAllowed;
        }

        var corridor = await ResolveCorridorAsync(command, cancellationToken);
        if (corridor.IsFailure)
        {
            return corridor.Error;
        }

        var (source, destination) = corridor.Value;

        if (command.AmountXof is not { } amountXof || Money.Create(amountXof, Currency.XOF) is not { IsSuccess: true } xof)
        {
            return TransfersErrors.Transfer.AmountInvalid;
        }

        var now = clock.UtcNow;
        var operationAt = command.OperationAt ?? now;
        if (operationAt > now)
        {
            return TransfersErrors.Transfer.FutureDate;
        }

        var operationDate = DateOnly.FromDateTime(operationAt.UtcDateTime);
        var rate = await dailyRates.GetAsync(operationDate, cancellationToken);
        if (rate is null)
        {
            return TransfersErrors.Transfer.RateNotSet;
        }

        var payments = await ResolvePaymentsAsync(command.Payments, source, cancellationToken);
        if (payments.IsFailure)
        {
            return payments.Error;
        }

        var request = new NewTransfer(
            source.Id, source.Currency, destination.Id, destination.Currency,
            xof.Value, rate.SaleRate, rate.PurchaseRate,
            command.SenderInfo, command.BeneficiaryInfo, command.Note,
            operationAt, [.. payments.Value.Select(p => p.Line)], command.CreditPartyId);

        // Validation complète AVANT de consommer un numéro de référence (le compteur est transactionnel de toute façon).
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString(WithdrawalCodeLength, CultureInfo.InvariantCulture);
        var draft = Transfer.Create(request, "-", "-", key, command.ActorId, now);
        if (draft.IsFailure)
        {
            return draft.Error;
        }

        Guid? creditLedgerAccount = null;
        if (draft.Value.CreditMoney is { } creditPart)
        {
            var credit = await CheckCreditAsync(command.CreditPartyId!.Value, source.Id, creditPart, cancellationToken);
            if (credit.IsFailure)
            {
                return credit.Error;
            }

            creditLedgerAccount = credit.Value;
        }

        var reference = await references.NextAsync(
            source.Id, source.Code, DateOnly.FromDateTime(now.UtcDateTime), cancellationToken);
        var transfer = Transfer.Create(request, reference, codeHasher.Hash(code), key, command.ActorId, now).Value;

        var entry = await PostCreationEntryAsync(
            transfer, payments.Value, creditLedgerAccount, operationDate, command.ActorId, cancellationToken);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        transfer.AttachCreationEntry(entry.Value);
        transfers.Add(transfer);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(transfer, code, replayed: false);
    }

    private static CreatedTransfer ToResponse(Transfer transfer, string? withdrawalCode, bool replayed) =>
        new(transfer.Id, transfer.Reference, withdrawalCode, transfer.AmountXof, transfer.AmountMro,
            transfer.SaleRate.MroPer5000Xof, transfer.CreditAmount, replayed);

    private async Task<Result<(AgencySummary Source, AgencySummary Destination)>> ResolveCorridorAsync(
        CreateTransferCommand command, CancellationToken cancellationToken)
    {
        Guid sourceId;
        if (scope.SeesAllAgencies)
        {
            // Le Superviseur choisit l'agence d'envoi.
            if (command.SourceAgencyId is not { } chosen)
            {
                return TransfersErrors.Transfer.SourceRequired;
            }

            sourceId = chosen;
        }
        else
        {
            // Un agent crée depuis son agence, et seulement depuis elle.
            if (scope.AgencyId is not { } own
                || (command.SourceAgencyId is { } asked && asked != own))
            {
                return TransfersErrors.Transfer.SourceNotAllowed;
            }

            sourceId = own;
        }

        var source = await agencies.GetByIdAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return TransfersErrors.Transfer.SourceRequired;
        }

        if (command.DestinationAgencyId is not { } destinationId
            || await agencies.GetByIdAsync(destinationId, cancellationToken) is not { } destination)
        {
            return TransfersErrors.Transfer.DestinationNotFound;
        }

        return (source, destination);
    }

    /// <summary>Tiers du crédit : existe, pas archivé, plafond respecté. Renvoie son compte au grand livre dans l'agence.</summary>
    private async Task<Result<Guid>> CheckCreditAsync(
        Guid partyId, Guid agencyId, Money creditPart, CancellationToken cancellationToken)
    {
        // Verrou d'abord : le solde lu ensuite tient compte d'un crédit simultané déjà validé.
        var account = await partyCredit.LockForCreditAsync(partyId, agencyId, cancellationToken);
        if (account is null)
        {
            return TransfersErrors.Credit.PartyNotFound;
        }

        if (account.IsArchived)
        {
            return TransfersErrors.Credit.PartyArchived;
        }

        if (account.CreditLimit is { } limit)
        {
            var balances = await ledger.GetBalancesAsync([account.LedgerAccountId], asOf: null, cancellationToken);
            if (balances.IsFailure)
            {
                return balances.Error;
            }

            var current = balances.Value.Single().Balance.Amount;
            if (current + creditPart.Amount > limit)
            {
                return TransfersErrors.Credit.LimitExceeded;
            }
        }

        return account.LedgerAccountId;
    }

    private sealed record ResolvedPayment(PaymentLine Line, Guid LedgerAccountId);

    private async Task<Result<IReadOnlyList<ResolvedPayment>>> ResolvePaymentsAsync(
        IReadOnlyList<CreateTransferPayment>? payments, AgencySummary source, CancellationToken cancellationToken)
    {
        // Sans paiement : le Domain dit si c'est permis (crédit total) ou non.
        if (payments is null || payments.Count == 0)
        {
            return Array.Empty<ResolvedPayment>();
        }

        var accounts = await treasury.GetByIdsAsync([.. payments.Select(p => p.TreasuryAccountId).Distinct()], cancellationToken);

        var resolved = new List<ResolvedPayment>(payments.Count);
        foreach (var payment in payments)
        {
            // FCFA entier, MRO à 2 décimales au plus : refusé, jamais arrondi.
            var amount = Money.Create(payment.Amount, source.Currency);
            if (amount.IsFailure)
            {
                return amount.Error;
            }

            var account = accounts.FirstOrDefault(a => a.Id == payment.TreasuryAccountId);
            if (account is null || !account.IsActive || account.AgencyId != source.Id || account.Currency != source.Currency)
            {
                return TransfersErrors.Payment.AccountNotAllowed;
            }

            resolved.Add(new ResolvedPayment(new PaymentLine(account.Id, amount.Value), account.LedgerAccountId));
        }

        return resolved;
    }

    private async Task<Result<Guid>> PostCreationEntryAsync(
        Transfer transfer,
        IReadOnlyList<ResolvedPayment> payments,
        Guid? creditLedgerAccount,
        DateOnly operationDate,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var sourcePosition = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.ExchangePosition, transfer.SourceAgencyId, transfer.SourceCurrency, cancellationToken);
        if (sourcePosition.IsFailure)
        {
            return sourcePosition.Error;
        }

        var destinationPosition = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.ExchangePosition, transfer.DestinationAgencyId, transfer.DestinationCurrency, cancellationToken);
        if (destinationPosition.IsFailure)
        {
            return destinationPosition.Error;
        }

        var toDeliver = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.TransfersToDeliver, transfer.DestinationAgencyId, transfer.DestinationCurrency, cancellationToken);
        if (toDeliver.IsFailure)
        {
            return toDeliver.Error;
        }

        List<PostEntryLine> lines =
        [
            .. payments.Select(p => new PostEntryLine(p.LedgerAccountId, EntrySide.Debit, p.Line.Amount)),
        ];

        // Part à crédit : le tiers nous doit (rouge dans l'agence d'envoi).
        if (creditLedgerAccount is { } creditAccount && transfer.CreditMoney is { } creditPart)
        {
            lines.Add(new PostEntryLine(creditAccount, EntrySide.Debit, creditPart));
        }

        lines.AddRange(
        [
            new PostEntryLine(sourcePosition.Value, EntrySide.Credit, transfer.PaidAmount),
            new PostEntryLine(destinationPosition.Value, EntrySide.Debit, transfer.AmountToDeliver),
            new PostEntryLine(toDeliver.Value, EntrySide.Credit, transfer.AmountToDeliver),
        ]);

        return await ledger.PostAsync(
            new PostEntryRequest(
                operationDate,
                EntryType,
                "Transfers",
                transfer.Id,
                actorId,
                $"Transfert {transfer.Reference}",
                lines),
            cancellationToken);
    }
}
