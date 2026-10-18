using Identity.Contracts;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;

namespace Identity.Infrastructure.Sessions;

/// <summary>
/// Permissions d'un compte = celles de son modèle de rôle (ajouts / retraits par compte : phase 6).
/// Lit l'état mis en cache par <see cref="SessionValidator"/> : un changement de rôle ou un blocage
/// vide ce cache, donc l'effet est immédiat.
/// </summary>
internal sealed class PermissionChecker(SessionValidator sessions) : IPermissionChecker
{
    public async Task<bool> HasPermissionAsync(Guid accountId, string permission, CancellationToken cancellationToken)
    {
        var state = await sessions.GetStateAsync(accountId, cancellationToken);

        return state is { Status: AccountStatus.Active }
            && RolePermissions.For(state.Role).Contains(permission);
    }
}