using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Users.GetUser;

/// <summary>Fiche d'un compte (cible de l'en-tête Location renvoyé à la création).</summary>
/// <param name="ActorId">Compte connecté, lu par l'API dans le JWT : Superviseur, ou compte de la même agence avec users.manage.</param>
public sealed record GetUserQuery(Guid ActorId, Guid UserId) : IQuery<UserSummary>;
