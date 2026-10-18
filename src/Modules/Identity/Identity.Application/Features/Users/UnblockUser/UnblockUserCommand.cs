using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Users.UnblockUser;

/// <summary>Déblocage d'un compte par le Superviseur.</summary>
public sealed record UnblockUserCommand(Guid UserId) : ICommand<UnblockedUser>;

/// <summary>
/// Règle validée : au déblocage, l'ancien mot de passe n'est plus valable. Le nouveau mot de passe
/// temporaire n'est renvoyé qu'ici, une seule fois ; il n'est jamais stocké en clair ni journalisé.
/// </summary>
public sealed record UnblockedUser(string TemporaryPassword);
