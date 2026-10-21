using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Transfers.Application.Abstractions;
using Transfers.Application.Features.Transfers.DeliverTransfer;
using Transfers.Domain;

namespace Transfers.Application.Features.Transfers.DeliverWithoutCode;

/// <summary>
/// Livraison sans code par le Superviseur (règles validées le 21 octobre 2026) : pièce d'identité du bénéficiaire et motif
/// obligatoires, possible même si le transfert est bloqué ; mêmes règles de livraison que d'habitude
/// (caisses de l'agence de destination, total = montant, date ni future ni avant le transfert).
/// POINT OUVERT : la pièce d'identité est stockée en clair jusqu'à la phase 6 (chiffrement, comme le NNI des tiers).
/// POINT OUVERT : l'audit (qui, motif, pièce) arrivera avec le module Audit ; le transfert garde déjà tout cela.
/// </summary>
public sealed class DeliverWithoutCodeCommandHandler(
    ITransferRepository transfers,
    DeliveryPosting posting,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<DeliverWithoutCodeCommand, DeliveredTransfer>
{
    public async Task<Result<DeliveredTransfer>> Handle(DeliverWithoutCodeCommand command, CancellationToken cancellationToken)
    {
        var texts = Domain.Transfers.Transfer.ValidateWithoutCodeTexts(command.IdDocument, command.Reason);
        if (texts.IsFailure)
        {
            return texts.Error;
        }

        var transfer = await transfers.GetForUpdateAsync(command.TransferId, cancellationToken);
        if (transfer is null)
        {
            return TransfersErrors.Transfer.NotFound;
        }

        var lines = await posting.ResolveLinesAsync(command.Payments, transfer, cancellationToken);
        if (lines.IsFailure)
        {
            return lines.Error;
        }

        var now = clock.UtcNow;
        var operationAt = command.OperationAt ?? now;
        var delivered = transfer.DeliverWithoutCode(
            [.. lines.Value.Select(l => l.Line)], operationAt, command.Note, command.IdDocument, command.Reason, command.ActorId, now);
        if (delivered.IsFailure)
        {
            return delivered.Error;
        }

        var entry = await posting.PostAsync(transfer, lines.Value, operationAt, command.ActorId, cancellationToken);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        transfer.AttachDeliveryEntry(entry.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var warnings = await posting.NegativeBalancesAsync(lines.Value, cancellationToken);
        return new DeliveredTransfer(
            transfer.Id,
            transfer.Reference,
            transfer.AmountToDeliver.Amount,
            transfer.DestinationCurrency,
            operationAt,
            warnings);
    }
}
