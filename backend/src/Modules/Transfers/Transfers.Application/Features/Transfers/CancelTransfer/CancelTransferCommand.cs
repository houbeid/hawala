using Hawala.SharedKernel.Messaging;

namespace Transfers.Application.Features.Transfers.CancelTransfer;

/// <summary>Annulation d'un transfert non livré par le Superviseur (permission transfers.cancel).</summary>
/// <param name="ActorId">Superviseur connecté, lu dans le JWT.</param>
/// <param name="Reason">Motif obligatoire (500 caractères au plus).</param>
/// <param name="OperationAt">Date et heure de l'annulation ; maintenant par défaut.</param>
public sealed record CancelTransferCommand(
    Guid ActorId,
    Guid TransferId,
    string? Reason,
    DateTimeOffset? OperationAt) : ICommand;
