using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Users.ChangeUserRole;

/// <summary>Changement du rôle d'un compte par le Superviseur, avec l'agence qui va avec.</summary>
/// <param name="ActorId">Superviseur connecté, lu par l'API dans le JWT : il ne peut pas changer son propre rôle.</param>
/// <param name="Role">Supervisor, Agent, Cashier ou ReadOnly.</param>
/// <param name="AgencyId">Obligatoire pour un agent, absent (null) pour un Superviseur.</param>
public sealed record ChangeUserRoleCommand(
    Guid ActorId,
    Guid UserId,
    string? Role,
    Guid? AgencyId) : ICommand;
