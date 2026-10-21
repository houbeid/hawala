using Cash.Application.Abstractions;
using Cash.Domain;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Ledger.Contracts;

namespace Cash.Application.Features.Movements.CancelCashMovement;

/// <summary>Annulation d'un mouvement de caisse par le Superviseur (permission cash.manage), motif obligatoire.</summary>
public sealed record CancelCashMovementCommand(Guid ActorId, Guid MovementId, string? Reason) : ICommand;

/// <summary>
/// Règle validée (21 octobre 2026) : contre-passation de l'écriture du mouvement, datée du jour de l'annulation
/// (les budgets déjà enregistrés ne changent pas). Le mouvement est verrouillé pendant la commande.
/// POINT OUVERT : les jours sont lus en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// </summary>
public sealed class CancelCashMovementCommandHandler(
    ICashMovementRepository movements,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<CancelCashMovementCommand>
{
    public async Task<Result> Handle(CancelCashMovementCommand command, CancellationToken cancellationToken)
    {
        var movement = await movements.GetForUpdateAsync(command.MovementId, cancellationToken);
        if (movement is null)
        {
            return CashErrors.Movement.NotFound;
        }

        var now = clock.UtcNow;
        var cancelled = movement.Cancel(command.Reason, command.ActorId, now);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        var reversal = await ledger.ReverseAsync(
            movement.EntryId, DateOnly.FromDateTime(now.UtcDateTime), command.ActorId, movement.CancellationReason, cancellationToken);
        if (reversal.IsFailure)
        {
            return reversal.Error;
        }

        movement.AttachCancellationEntry(reversal.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
