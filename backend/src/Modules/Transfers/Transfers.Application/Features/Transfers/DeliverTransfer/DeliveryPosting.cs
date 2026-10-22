using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Ledger.Contracts;
using Transfers.Application.Features.Transfers.CreateTransfer;
using Transfers.Domain;
using Transfers.Domain.Transfers;
using Treasury.Contracts;

namespace Transfers.Application.Features.Transfers.DeliverTransfer;

/// <summary>Une ligne de livraison et la caisse qui la porte.</summary>
public sealed record DeliveryLine(PaymentLine Line, TreasuryAccountRef Account);

/// <summary>
/// Étapes communes à toute livraison (avec ou sans code) : caisses de l'agence de destination,
/// écriture « Transferts à livrer » / caisses, avertissement des caisses passées en négatif.
/// </summary>
public sealed class DeliveryPosting(ITreasuryDirectory treasury, ILedger ledger)
{
    public const string EntryType = "Transfers.Delivered";

    /// <summary>Caisses actives de l'agence de destination, dans sa devise ; montants refusés s'ils sont trop précis.</summary>
    public async Task<Result<IReadOnlyList<DeliveryLine>>> ResolveLinesAsync(
        IReadOnlyList<CreateTransferPayment>? payments, Transfer transfer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        if (payments is null || payments.Count == 0)
        {
            return TransfersErrors.Payment.Required;
        }

        var accounts = await treasury.GetByIdsAsync([.. payments.Select(p => p.TreasuryAccountId).Distinct()], cancellationToken);

        var resolved = new List<DeliveryLine>(payments.Count);
        foreach (var payment in payments)
        {
            // FCFA entier, MRO à 2 décimales au plus : refusé, jamais arrondi.
            var amount = Money.Create(payment.Amount, transfer.DestinationCurrency);
            if (amount.IsFailure)
            {
                return amount.Error;
            }

            var account = accounts.FirstOrDefault(a => a.Id == payment.TreasuryAccountId);
            if (account is null
                || !account.IsActive
                || account.AgencyId != transfer.DestinationAgencyId
                || account.Currency != transfer.DestinationCurrency)
            {
                return TransfersErrors.Payment.AccountNotAllowed;
            }

            resolved.Add(new DeliveryLine(new PaymentLine(account.Id, amount.Value), account));
        }

        return resolved;
    }

    /// <summary>Débit « Transferts à livrer » / crédit des caisses, dans la devise de destination, à la date de la livraison.</summary>
    public async Task<Result<Guid>> PostAsync(
        Transfer transfer,
        IReadOnlyList<DeliveryLine> lines,
        DateTimeOffset operationAt,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(lines);

        var toDeliver = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.TransfersToDeliver, transfer.DestinationAgencyId, transfer.DestinationCurrency, cancellationToken);
        if (toDeliver.IsFailure)
        {
            return toDeliver.Error;
        }

        List<PostEntryLine> entryLines =
        [
            new PostEntryLine(toDeliver.Value, EntrySide.Debit, transfer.AmountToDeliver),
            .. lines.Select(l => new PostEntryLine(l.Account.LedgerAccountId, EntrySide.Credit, l.Line.Amount)),
        ];

        return await ledger.PostAsync(
            new PostEntryRequest(
                DateOnly.FromDateTime(operationAt.UtcDateTime),
                EntryType,
                "Transfers",
                transfer.Id,
                actorId,
                $"Livraison {transfer.Reference}",
                entryLines),
            cancellationToken);
    }

    /// <summary>Soldes des caisses de la livraison après l'écriture (lus dans la même transaction) : celles en négatif.</summary>
    public async Task<IReadOnlyList<NegativeBalanceWarning>> NegativeBalancesAsync(
        IReadOnlyList<DeliveryLine> lines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var balances = await ledger.GetBalancesAsync([.. lines.Select(l => l.Account.LedgerAccountId)], asOf: null, cancellationToken);
        if (balances.IsFailure)
        {
            return [];
        }

        return [.. lines
            .Select(l => (l.Account, Balance: balances.Value.Single(b => b.AccountId == l.Account.LedgerAccountId).Balance))
            .Where(x => x.Balance.Amount < 0m)
            .Select(x => new NegativeBalanceWarning(x.Account.Id, x.Account.Name, x.Balance.Amount, x.Balance.Currency))];
    }
}
