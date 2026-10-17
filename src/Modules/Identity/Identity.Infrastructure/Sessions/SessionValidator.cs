using Identity.Domain.Accounts;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Identity.Infrastructure.Sessions;

/// <summary>
/// Option B : à chaque requête, vérifie que le JWT correspond toujours à l'état du compte.
/// Un compte bloqué, ou dont la SecurityVersion a changé (rôle, mot de passe…), est refusé tout de suite.
/// L'état est gardé 30 secondes en mémoire, et retiré du cache dès que le compte est modifié en base
/// (voir <see cref="AccountCacheInvalidationInterceptor"/>).
/// </summary>
public sealed class SessionValidator(IdentityModuleDbContext db, IMemoryCache cache)
{
    // Filet de sécurité : la suppression du cache à l'enregistrement rend l'effet immédiat dans le cas normal.
    // Seul un cas rare (une lecture concurrente pendant la transaction qui bloque le compte) peut garder
    // l'ancien état en cache, au plus pendant cette durée.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<bool> IsSessionCurrentAsync(Guid accountId, int securityVersion, CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(accountId, cancellationToken);

        return state is not null
            && state.Status == AccountStatus.Active
            && state.SecurityVersion == securityVersion;
    }

    internal static string CacheKey(Guid accountId) => $"identity:account-security:{accountId:N}";

    private async Task<AccountSecurityState?> GetStateAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var key = CacheKey(accountId);
        if (cache.TryGetValue(key, out AccountSecurityState? cached))
        {
            return cached;
        }

        var state = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new AccountSecurityState(a.SecurityVersion, a.Status))
            .FirstOrDefaultAsync(cancellationToken);

        // Un compte introuvable n'est pas mis en cache : il sera relu à la prochaine requête.
        if (state is not null)
        {
            cache.Set(key, state, CacheDuration);
        }

        return state;
    }

    private sealed record AccountSecurityState(int SecurityVersion, AccountStatus Status);
}