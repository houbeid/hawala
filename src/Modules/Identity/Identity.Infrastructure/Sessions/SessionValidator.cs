using Identity.Domain.Accounts;
using Identity.Domain.Roles;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Identity.Infrastructure.Sessions;

/// <summary>
/// Option B : à chaque requête, vérifie que le JWT correspond toujours à l'état du compte.
/// Un compte bloqué, ou dont la SecurityVersion a changé (rôle, agence, mot de passe…), est refusé tout de suite.
/// L'état (version, statut, rôle) est gardé 30 secondes en mémoire, et retiré du cache dès que le compte
/// est modifié en base (voir <see cref="AccountCacheInvalidationInterceptor"/>).
/// Le même état sert à la vérification des permissions et au périmètre par agence : aucune requête en base en plus.
/// </summary>
public sealed class SessionValidator(IdentityModuleDbContext db, IMemoryCache cache, AgencyScope agencyScope)
{
    // Filet de sécurité : la suppression du cache à l'enregistrement rend l'effet immédiat dans le cas normal.
    // Seul un cas rare (une lecture concurrente pendant la transaction qui bloque le compte) peut garder
    // l'ancien état en cache, au plus pendant cette durée.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<bool> IsSessionCurrentAsync(Guid accountId, int securityVersion, CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(accountId, cancellationToken);

        var isCurrent = state is not null
            && state.Status == AccountStatus.Active
            && state.SecurityVersion == securityVersion;

        // Le périmètre n'est rempli que pour une session valide : sinon il reste vide (aucune agence visible).
        if (isCurrent)
        {
            agencyScope.Set(state!.Role, state.AgencyId);
        }

        return isCurrent;
    }

    internal static string CacheKey(Guid accountId) => $"identity:account-security:{accountId:N}";

    internal async Task<AccountSecurityState?> GetStateAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var key = CacheKey(accountId);
        if (cache.TryGetValue(key, out AccountSecurityState? cached))
        {
            return cached;
        }

        var state = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new AccountSecurityState(a.SecurityVersion, a.Status, a.Role, a.AgencyId))
            .FirstOrDefaultAsync(cancellationToken);

        // Un compte introuvable n'est pas mis en cache : il sera relu à la prochaine requête.
        if (state is not null)
        {
            cache.Set(key, state, CacheDuration);
        }

        return state;
    }
}

/// <param name="AgencyId">
/// Agence du compte, pour le périmètre. Toujours à jour dans le cache : changer d'agence ou de rôle
/// augmente la SecurityVersion et vide le cache (intercepteur EF).
/// </param>
internal sealed record AccountSecurityState(int SecurityVersion, AccountStatus Status, RoleTemplate Role, Guid? AgencyId);