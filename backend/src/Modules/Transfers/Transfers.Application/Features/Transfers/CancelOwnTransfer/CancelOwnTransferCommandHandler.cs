using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Ledger.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Domain;

namespace Transfers.Application.Features.Transfers.CancelOwnTransfer;

/// <summary>
/// Annule un transfert que son auteur vient de créer : mêmes effets que l'annulation du Superviseur (contre-passation
/// de l'écriture de création, datée de maintenant ; remboursement en caisse, dette du crédit effacée ; audit), mais
/// seulement pour un transfert intact et récent (règles dans <c>Transfer.CancelByCreator</c>). Transfert verrouillé.
/// </summary>
public sealed class CancelOwnTransferCommandHandler(
    ITransferRepository transfers,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditLog audit)
    : ICommandHandler<CancelOwnTransferCommand>
{
    public async Task<Result> Handle(CancelOwnTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await transfers.GetForUpdateAsync(command.TransferId, cancellationToken);
        if (transfer is null)
        {
            return TransfersErrors.Transfer.NotFound;
        }

        var now = clock.UtcNow;
        var cancelled = transfer.CancelByCreator(command.Reason, command.ActorId, now);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        var reversal = await ledger.ReverseAsync(
            transfer.CreationEntryId,
            DateOnly.FromDateTime(now.UtcDateTime),
            command.ActorId,
            transfer.CancellationReason,
            cancellationToken);
        if (reversal.IsFailure)
        {
            return reversal.Error;
        }

        transfer.AttachCancellationEntry(reversal.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var audited = await TransferAudit.RecordAsync(
            audit, TransferAudit.Cancelled, transfer, command.ActorId, transfer.SourceAgencyId, cancellationToken);
        return audited.IsFailure ? audited.Error : Result.Success();
    }
}
