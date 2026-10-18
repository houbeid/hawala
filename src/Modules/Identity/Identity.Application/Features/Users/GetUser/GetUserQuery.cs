using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Users.GetUser;

/// <summary>Fiche d'un compte (cible de l'en-tête Location renvoyé à la création).</summary>
public sealed record GetUserQuery(Guid UserId) : IQuery<UserSummary>;
