using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Ledger.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Domain;

namespace Transfers.Application.Features.Transfers.CancelTransfer;

/// <summary>
/// Annule un transfert non livré (règles validées) : Superviseur seulement, motif obligatoire,
/// contre-passation de l'écriture de création à la date de l'annulation (les budgets déjà enregistrés ne changent pas).
/// La contre-passation remet tout à zéro : l'argent encaissé sort des caisses de l'agence d'envoi (remboursement),
/// la dette d'un crédit disparaît du tiers, et « Transferts à livrer » est soldé. Annulé = verrouillé.
/// Le transfert est verrouillé (FOR UPDATE) : pas de course avec une livraison.
/// POINT OUVERT : la date d'opération est lue en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// POINT OUVERT : l'audit (qui a annulé, motif) arrivera avec le module Audit ; le transfert garde déjà l'auteur et le motif.
/// </summary>
public sealed class CancelTransferCommandHandler(
    ITransferRepository transfers,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<CancelTransferCommand>
{
    public async Task<Result> Handle(CancelTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await transfers.GetForUpdateAsync(command.TransferId, cancellationToken);
        if (transfer is null)
        {
            return TransfersErrors.Transfer.NotFound;
        }

        var now = clock.UtcNow;
        var operationAt = command.OperationAt ?? now;
        var cancelled = transfer.Cancel(command.Reason, operationAt, command.ActorId, now);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        var reversal = await ledger.ReverseAsync(
            transfer.CreationEntryId,
            DateOnly.FromDateTime(operationAt.UtcDateTime),
            command.ActorId,
            transfer.CancellationReason,
            cancellationToken);
        if (reversal.IsFailure)
        {
            return reversal.Error;
        }

        transfer.AttachCancellationEntry(reversal.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
