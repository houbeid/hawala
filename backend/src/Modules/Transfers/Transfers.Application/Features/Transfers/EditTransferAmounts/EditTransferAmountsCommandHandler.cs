using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Ledger.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Application.Features.Transfers.CreateTransfer;
using Transfers.Domain;

namespace Transfers.Application.Features.Transfers.EditTransferAmounts;

/// <summary>
/// Règles validées le 21 octobre 2026 : le Superviseur modifie montant, prix de vente, répartition, crédit et date
/// d'un transfert non livré ; motif obligatoire ; l'écriture en vigueur est contre-passée et une nouvelle est passée,
/// toutes deux au jour de la modification ; plafond de crédit appliqué à la nouvelle part, sans l'ancienne
/// (la contre-passation a lieu AVANT le contrôle, dans la même transaction). Rien de changé : rien n'est écrit.
/// POINT OUVERT : les jours sont lus en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// </summary>
public sealed class EditTransferAmountsCommandHandler(
    ITransferRepository transfers,
    CreationPosting posting,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditLog audit)
    : ICommandHandler<EditTransferAmountsCommand>
{
    public const string EntryType = "Transfers.Edited";

    public async Task<Result> Handle(EditTransferAmountsCommand command, CancellationToken cancellationToken)
    {
        if (command.Version is not { } version)
        {
            return TransfersErrors.Transfer.VersionRequired;
        }

        var transfer = await transfers.GetForUpdateAsync(command.TransferId, cancellationToken);
        if (transfer is null)
        {
            return TransfersErrors.Transfer.NotFound;
        }

        if (Money.Create(command.AmountXof ?? transfer.AmountXof, Currency.XOF) is not { IsSuccess: true } xof)
        {
            return TransfersErrors.Transfer.AmountInvalid;
        }

        var saleRate = command.SaleRate is { } rate ? ExchangeRate.Create(rate) : transfer.SaleRate;
        if (saleRate.IsFailure)
        {
            return saleRate.Error;
        }

        var payments = await posting.ResolvePaymentsAsync(
            command.Payments, transfer.SourceAgencyId, transfer.SourceCurrency, cancellationToken);
        if (payments.IsFailure)
        {
            return payments.Error;
        }

        var now = clock.UtcNow;
        var entryInForce = transfer.CreationEntryId;
        var edited = transfer.EditAmounts(
            version,
            xof.Value,
            saleRate.Value,
            command.OperationAt ?? transfer.OperationAt,
            [.. payments.Value.Select(p => p.Line)],
            command.CreditPartyId,
            command.Reason,
            command.ActorId,
            now);
        if (edited.IsFailure)
        {
            return edited.Error;
        }

        if (!edited.Value)
        {
            return Result.Success();
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var reversal = await ledger.ReverseAsync(entryInForce, today, command.ActorId, command.Reason, cancellationToken);
        if (reversal.IsFailure)
        {
            return reversal.Error;
        }

        Guid? creditLedgerAccount = null;
        if (transfer.CreditMoney is { } creditPart)
        {
            var credit = await posting.CheckCreditAsync(transfer.CreditPartyId!.Value, transfer.SourceAgencyId, creditPart, cancellationToken);
            if (credit.IsFailure)
            {
                return credit.Error;
            }

            creditLedgerAccount = credit.Value;
        }

        var entry = await posting.PostAsync(
            transfer, payments.Value, creditLedgerAccount, today, EntryType, command.ActorId, cancellationToken);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        transfer.ReplaceCreationEntry(entry.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var audited = await TransferAudit.RecordAsync(
            audit, TransferAudit.AmountsEdited, transfer, command.ActorId, transfer.SourceAgencyId, cancellationToken);
        if (audited.IsFailure)
        {
            return audited.Error;
        }

        return Result.Success();
    }
}
