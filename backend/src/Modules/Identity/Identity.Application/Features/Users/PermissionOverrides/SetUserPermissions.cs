using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Roles;

namespace Identity.Application.Features.Users.PermissionOverrides;

/// <summary>Le Superviseur fixe la liste complète des permissions d'un compte (cases cochées).</summary>
/// <param name="ActorId">Compte connecté, lu par l'API dans le JWT : doit être un Superviseur.</param>
/// <param name="Permissions">Toutes les permissions voulues ; vide = aucune ; null = refusé.</param>
public sealed record SetUserPermissionsCommand(
    Guid ActorId,
    Guid UserId,
    IReadOnlyList<string?>? Permissions) : ICommand<UserPermissions>;

/// <summary>
/// Règles validées le 21 octobre 2026 : un non-Superviseur peut recevoir n'importe quelle permission ;
/// seul un compte de RÔLE Superviseur la donne ou la retire (la permission users.manage ne suffit pas) ;
/// un Superviseur garde toujours tout. Effet immédiat (SecurityVersion). Tracé dans l'audit, même transaction.
/// </summary>
public sealed class SetUserPermissionsCommandHandler(
    IUserAccountRepository accounts,
    IUnitOfWork unitOfWork,
    IAuditLog audit)
    : ICommandHandler<SetUserPermissionsCommand, UserPermissions>
{
    public const string AuditAction = "Identity.Account.PermissionsChanged";

    public async Task<Result<UserPermissions>> Handle(SetUserPermissionsCommand command, CancellationToken cancellationToken)
    {
        var actor = await accounts.GetByIdAsync(command.ActorId, cancellationToken);
        if (actor is null || actor.Role != RoleTemplate.Supervisor)
        {
            return IdentityErrors.Permissions.SupervisorOnly;
        }

        if (command.Permissions is null)
        {
            return IdentityErrors.Permissions.Required;
        }

        var account = await accounts.GetByIdAsync(command.UserId, cancellationToken);
        if (account is null)
        {
            return IdentityErrors.Account.NotFound;
        }

        var before = account.EffectivePermissions;
        var changed = account.SetPermissions(command.Permissions);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        if (!changed.Value)
        {
            return UserPermissionsMapper.ToView(account);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Une ligne par permission qui a changé : « accordée » avant / après, lisible plus tard (« X a retiré … à Y »).
        var after = account.EffectivePermissions;
        var changes = Identity.Contracts.Permissions.All
            .Where(p => before.Contains(p) != after.Contains(p))
            .Select(p => new AuditChange(p, Granted(before, p), Granted(after, p)))
            .ToList();

        var recorded = await audit.RecordAsync(
            new AuditRecord(AuditAction, "UserAccount", account.Id, command.ActorId, account.AgencyId, Changes: changes),
            cancellationToken);
        if (recorded.IsFailure)
        {
            return recorded.Error;
        }

        return UserPermissionsMapper.ToView(account);
    }

    private static string Granted(IReadOnlySet<string> permissions, string permission) =>
        permissions.Contains(permission) ? "granted" : "not_granted";
}
