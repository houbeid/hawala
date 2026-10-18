using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Users.CreateUser;

/// <summary>Création d'un compte par le Superviseur.</summary>
/// <param name="Role">Supervisor, Agent, Cashier ou ReadOnly.</param>
/// <param name="AgencyId">Obligatoire pour un agent, absent pour un Superviseur.</param>
public sealed record CreateUserCommand(
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
