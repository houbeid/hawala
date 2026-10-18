using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Users.ResetUserPassword;

/// <summary>Réinitialisation du mot de passe d'un compte par le Superviseur (ex. mot de passe oublié).</summary>
/// <param name="ActorId">Superviseur connecté, lu par l'API dans le JWT : il ne peut pas réinitialiser son propre mot de passe.</param>
/// <param name="UserId">Compte à réinitialiser.</param>
public sealed record ResetUserPasswordCommand(Guid ActorId, Guid UserId) : ICommand<ResetPassword>;

/// <summary>
/// Le nouveau mot de passe temporaire n'est renvoyé qu'ici, une seule fois : il n'est jamais stocké en clair
/// et ne doit jamais apparaître dans les logs.
/// </summary>
public sealed record ResetPassword(string TemporaryPassword);
