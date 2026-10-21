using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Ledger.Contracts;
using Parties.Contracts;
using Transfers.Domain;
using Transfers.Domain.Transfers;
using Treasury.Contracts;

namespace Transfers.Application.Features.Transfers.CreateTransfer;

/// <summary>Une ligne de paiement du client et le compte au grand livre de sa caisse.</summary>
public sealed record SourcePayment(PaymentLine Line, Guid LedgerAccountId);

/// <summary>
/// Étapes communes à la création et à la modification d'argent d'un transfert :
/// caisses de l'agence d'envoi, contrôle du crédit (tiers verrouillé, plafond), écriture via « Position de change ».
/// </summary>
public sealed class CreationPosting(ITreasuryDirectory treasury, IPartyCreditAccounts partyCredit, ILedger ledger)
{
    /// <summary>Caisses actives de l'agence d'envoi, dans sa devise ; montants refusés s'ils sont trop précis.</summary>
    public async Task<Result<IReadOnlyList<SourcePayment>>> ResolvePaymentsAsync(
        IReadOnlyList<CreateTransferPayment>? payments, Guid agencyId, Currency currency, CancellationToken cancellationToken)
    {
        // Sans paiement : le Domain dit si c'est permis (crédit total) ou non.
        if (payments is null || payments.Count == 0)
        {
            return Array.Empty<SourcePayment>();
        }

        var accounts = await treasury.GetByIdsAsync([.. payments.Select(p => p.TreasuryAccountId).Distinct()], cancellationToken);

        var resolved = new List<SourcePayment>(payments.Count);
        foreach (var payment in payments)
        {
            // FCFA entier, MRO à 2 décimales au plus : refusé, jamais arrondi.
            var amount = Money.Create(payment.Amount, currency);
            if (amount.IsFailure)
            {
                return amount.Error;
            }

            var account = accounts.FirstOrDefault(a => a.Id == payment.TreasuryAccountId);
            if (account is null || !account.IsActive || account.AgencyId != agencyId || account.Currency != currency)
            {
                return TransfersErrors.Payment.AccountNotAllowed;
            }

            resolved.Add(new SourcePayment(new PaymentLine(account.Id, amount.Value), account.LedgerAccountId));
        }

        return resolved;
    }

    /// <summary>
    /// Tiers du crédit : existe, pas archivé, plafond respecté (solde actuel dans l'agence + part à crédit ≤ plafond).
    /// Le compte du tiers est verrouillé d'abord : le solde lu tient compte d'un crédit simultané déjà validé.
    /// Renvoie son compte au grand livre dans l'agence.
    /// </summary>
    public async Task<Result<Guid>> CheckCreditAsync(
        Guid partyId, Guid agencyId, Money creditPart, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(creditPart);

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

    /// <summary>
    /// Écriture équilibrée par devise : devise d'envoi, débit des caisses (et du tiers pour la part à crédit) /
    /// crédit Position de change ; devise de destination, débit Position de change / crédit Transferts à livrer.
    /// </summary>
    public async Task<Result<Guid>> PostAsync(
        Transfer transfer,
        IReadOnlyList<SourcePayment> payments,
        Guid? creditLedgerAccount,
        DateOnly operationDate,
        string entryType,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(payments);

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
                entryType,
                "Transfers",
                transfer.Id,
                actorId,
                $"Transfert {transfer.Reference}",
                lines),
            cancellationToken);
    }
}
