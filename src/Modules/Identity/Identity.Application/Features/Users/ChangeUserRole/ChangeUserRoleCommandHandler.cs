using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;
using Organization.Contracts;

namespace Identity.Application.Features.Users.ChangeUserRole;

/// <summary>
/// Change le rôle (et l'agence) d'un compte. Les sessions ne sont pas révoquées : le jeton d'accès en cours
/// est refusé (SecurityVersion), et le prochain rafraîchissement donne un jeton avec le nouveau rôle.
/// Doit être exécuté dans une transaction : le verrou posé par le comptage des Superviseurs
/// protège la règle « le dernier Superviseur actif ne peut pas être rétrogradé ».
/// POINT OUVERT : l'audit « qui a changé ce rôle » arrivera avec le module Audit.
/// </summary>
public sealed class ChangeUserRoleCommandHandler(
    IUserAccountRepository accounts,
    IAgencyDirectory agencies,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ChangeUserRoleCommand>
{
    public async Task<Result> Handle(ChangeUserRoleCommand command, CancellationToken cancellationToken)
    {
        if (command.ActorId == command.UserId)
        {
            return IdentityErrors.Account.CannotChangeOwnRole;
        }

        if (!EnumNames.TryParse<RoleTemplate>(command.Role, out var role))
        {
            return IdentityErrors.Account.RoleInvalid;
        }

        if (command.AgencyId is { } agencyId && agencyId != Guid.Empty
            && await agencies.GetByIdAsync(agencyId, cancellationToken) is null)
        {
            return IdentityErrors.Account.AgencyNotFound;
        }

        // Toujours avant la lecture du compte (on ne sait pas encore s'il est Superviseur) :
        // même raisonnement que pour le blocage.
        var activeSupervisorCount = await accounts.CountActiveSupervisorsForUpdateAsync(cancellationToken);

        var account = await accounts.GetByIdAsync(command.UserId, cancellationToken);
        if (account is null)
        {
            return IdentityErrors.Account.NotFound;
        }

        var changed = role == account.Role
            ? ChangeAgencyOnly(account, role, command.AgencyId)
            : account.ChangeRole(role, command.AgencyId, activeSupervisorCount);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Même rôle : ChangeRole ne ferait rien et ignorerait l'agence sans le dire.
    /// On applique donc l'agence envoyée, avec les mêmes règles qu'à la création.
    /// </summary>
    private static Result ChangeAgencyOnly(UserAccount account, RoleTemplate role, Guid? agencyId)
    {
        if (role == RoleTemplate.Supervisor)
        {
            return agencyId is null
                ? Result.Success()
                : IdentityErrors.Account.AgencyNotAllowedForSupervisor;
        }

        return account.ChangeAgency(agencyId ?? Guid.Empty);
    }
}
