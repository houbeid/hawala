using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Transfers.Application.Abstractions;
using Transfers.Domain;

namespace Transfers.Application.Features.Transfers.UnlockCode;

/// <summary>Le compteur d'essais repart à zéro (règle validée le 21 octobre 2026) ; le transfert reste verrouillé pendant la commande.</summary>
public sealed class UnlockCodeCommandHandler(ITransferRepository transfers, IUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<UnlockCodeCommand>
{
    public async Task<Result> Handle(UnlockCodeCommand command, CancellationToken cancellationToken)
    {
        var transfer = await transfers.GetForUpdateAsync(command.TransferId, cancellationToken);
        if (transfer is null)
        {
            return TransfersErrors.Transfer.NotFound;
        }

        var unlocked = transfer.UnlockCode(command.Reason, command.ActorId, clock.UtcNow);
        if (unlocked.IsFailure)
        {
            return unlocked;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
