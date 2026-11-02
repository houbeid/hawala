using Hawala.SharedKernel.Messaging;

namespace Transfers.Application.Features.Transfers.CancelOwnTransfer;

/// <summary>
/// Annulation d'un transfert par son auteur, depuis l'écran du code juste après la création (décision du patron,
/// 2 novembre 2026 ; permission transfers.create). Voir <c>Transfer.CancelByCreator</c>.
/// </summary>
/// <param name="ActorId">Compte connecté, lu dans le JWT : doit être l'auteur du transfert.</param>
/// <param name="Reason">Motif facultatif (500 caractères au plus) ; un motif par défaut est enregistré sinon.</param>
public sealed record CancelOwnTransferCommand(Guid ActorId, Guid TransferId, string? Reason) : ICommand;
