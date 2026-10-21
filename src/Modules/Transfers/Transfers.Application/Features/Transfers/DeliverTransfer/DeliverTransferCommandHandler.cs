using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Ledger.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Application.Features.Transfers.CreateTransfer;
using Transfers.Domain;
using Transfers.Domain.Transfers;
using Treasury.Contracts;

namespace Transfers.Application.Features.Transfers.DeliverTransfer;

/// <summary>
/// Livre un transfert. Règles validées le 20 octobre 2026 :
/// - seule l'agence de destination livre ; le Superviseur peut livrer tout transfert ;
/// - un transfert à crédit non remboursé se livre quand même (la dette reste sur le tiers) ;
/// - le code de retrait est saisi par tous, Superviseur compris (livrer sans code : action à part) ;
/// - code faux compté par transfert et enregistré même si la livraison est refusée ; blocage au 5e ;
///   les erreurs de saisie (montants, caisses, date) sont vérifiées AVANT le code, pour ne pas consommer d'essai ;
/// - une seule livraison, répartie sur des caisses actives de l'agence de destination, total = montant à livrer ;
/// - date : maintenant par défaut, passée permise, jamais future ni avant le transfert ;
/// - caisse en négatif : livraison acceptée, avertissement dans la réponse ;
/// - écriture : débit « Transferts à livrer » / crédit des caisses, dans la devise de destination ;
/// - le transfert est verrouillé (FOR UPDATE) : deux livraisons simultanées sont impossibles.
/// POINT OUVERT : livrer en créditant un tiers (bénéficiaire qui laisse l'argent en dépôt) : pas encore.
/// POINT OUVERT : la date d'opération est lue en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// POINT OUVERT : l'audit (qui a livré) arrivera avec le module Audit ; le transfert garde déjà l'auteur.
/// </summary>
public sealed class DeliverTransferCommandHandler(
    ITransferRepository transfers,
    IWithdrawalCodeHasher codeHasher,
    IAgencyScope scope,
    ITreasuryDirectory treasury,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<DeliverTransferCommand, DeliveryOutcome>
{
    public const string EntryType = "Transfers.Delivered";

    public async Task<Result<DeliveryOutcome>> Handle(DeliverTransferCommand command, CancellationToken cancellationToken)
    {
        var code = command.Code?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            return TransfersErrors.Delivery.CodeRequired;
        }

        var transfer = await transfers.GetForUpdateAsync(command.TransferId, cancellationToken);
        if (transfer is null)
        {
            return TransfersErrors.Transfer.NotFound;
        }

        if (!scope.SeesAllAgencies && scope.AgencyId != transfer.DestinationAgencyId)
        {
            return TransfersErrors.Delivery.NotDestinationAgency;
        }

        var deliverable = transfer.EnsureDeliverable();
        if (deliverable.IsFailure)
        {
            return deliverable.Error;
        }

        var lines = await ResolveLinesAsync(command.Payments, transfer, cancellationToken);
        if (lines.IsFailure)
        {
            return lines.Error;
        }

        var now = clock.UtcNow;
        var operationAt = command.OperationAt ?? now;
        var paymentLines = lines.Value.Select(l => l.Line).ToList();
        var valid = transfer.ValidateDelivery(paymentLines, operationAt, command.Note, now);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        if (!codeHasher.Verify(code, transfer.WithdrawalCodeHash))
        {
            // Enregistré malgré le refus : le décorateur valide la transaction, car le résultat est un succès technique.
            var refusal = transfer.RegisterWrongCode();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return DeliveryOutcome.Refused(refusal, transfer.RemainingCodeAttempts);
        }

        transfer.Deliver(paymentLines, operationAt, command.Note, command.ActorId, now);

        var entry = await PostDeliveryEntryAsync(transfer, lines.Value, operationAt, command.ActorId, cancellationToken);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        transfer.AttachDeliveryEntry(entry.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var warnings = await NegativeBalancesAsync(lines.Value, cancellationToken);
        return DeliveryOutcome.Success(new DeliveredTransfer(
            transfer.Id,
            transfer.Reference,
            transfer.AmountToDeliver.Amount,
            transfer.DestinationCurrency,
            operationAt,
            warnings));
    }

    private sealed record ResolvedLine(PaymentLine Line, TreasuryAccountRef Account);

    private async Task<Result<IReadOnlyList<ResolvedLine>>> ResolveLinesAsync(
        IReadOnlyList<CreateTransferPayment>? payments, Transfer transfer, CancellationToken cancellationToken)
    {
        if (payments is null || payments.Count == 0)
        {
            return TransfersErrors.Payment.Required;
        }

        var accounts = await treasury.GetByIdsAsync([.. payments.Select(p => p.TreasuryAccountId).Distinct()], cancellationToken);

        var resolved = new List<ResolvedLine>(payments.Count);
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

            resolved.Add(new ResolvedLine(new PaymentLine(account.Id, amount.Value), account));
        }

        return resolved;
    }

    private async Task<Result<Guid>> PostDeliveryEntryAsync(
        Transfer transfer,
        IReadOnlyList<ResolvedLine> lines,
        DateTimeOffset operationAt,
        Guid actorId,
        CancellationToken cancellationToken)
    {
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

    /// <summary>Soldes des caisses de la livraison après l'écriture (lus dans la même transaction).</summary>
    private async Task<IReadOnlyList<NegativeBalanceWarning>> NegativeBalancesAsync(
        IReadOnlyList<ResolvedLine> lines, CancellationToken cancellationToken)
    {
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
