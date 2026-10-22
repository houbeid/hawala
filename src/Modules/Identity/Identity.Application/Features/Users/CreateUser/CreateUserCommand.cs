using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Users.CreateUser;

/// <summary>Création d'un compte par le Superviseur (ou, dans son agence, par un compte qui a users.manage).</summary>
/// <param name="ActorId">Compte connecté, lu par l'API dans le JWT : Superviseur, ou compte de la même agence avec users.manage.</param>
/// <param name="Role">Supervisor, Agent, Cashier ou ReadOnly.</param>
/// <param name="AgencyId">Obligatoire pour un agent, absent pour un Superviseur.</param>
public sealed record CreateUserCommand(
    Guid ActorId,
    string? Email,
    string? Phone,
    string? FullName,
    string? Role,
    Guid? AgencyId) : ICommand<CreatedUser>;

/// <summary>
/// Le mot de passe temporaire n'est renvoyé qu'ici, une seule fois : il n'est jamais stocké en clair
/// et ne doit jamais apparaître dans les logs.
/// </summary>
public sealed record CreatedUser(Guid Id, string TemporaryPassword);
