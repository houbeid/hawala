using Identity.Contracts;
using Identity.Domain.Accounts;

namespace Identity.Infrastructure.Sessions;

/// <summary>
/// Permissions d'un compte = modèle de son rôle + ajouts − retraits du compte (phase 6), calculées par <see cref="SessionValidator"/>.
/// Lit l'état mis en cache par <see cref="SessionValidator"/> : un changement de rôle ou un blocage
/// vide ce cache, donc l'effet est immédiat.
/// </summary>
internal sealed class PermissionChecker(SessionValidator sessions) : IPermissionChecker
{
    public async Task<bool> HasPermissionAsync(Guid accountId, string permission, CancellationToken cancellationToken)
    {
        var state = await sessions.GetStateAsync(accountId, cancellationToken);

        return state is { Status: AccountStatus.Active }
            && state.Permissions.Contains(permission);
    }
}