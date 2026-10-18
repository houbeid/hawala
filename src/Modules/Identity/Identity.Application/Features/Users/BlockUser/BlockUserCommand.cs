using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Users.BlockUser;

/// <summary>Blocage d'un compte par le Superviseur. Aucun motif n'est demandé (règle validée).</summary>
/// <param name="ActorId">Superviseur connecté, lu par l'API dans le JWT : il ne peut pas se bloquer lui-même.</param>
/// <param name="UserId">Compte à bloquer.</param>
public sealed record BlockUserCommand(Guid ActorId, Guid UserId) : ICommand;
