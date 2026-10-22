using Identity.Domain.Accounts;
using Identity.Domain.Roles;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Identity.Infrastructure.Sessions;

/// <summary>
/// Option B : à chaque requête, vérifie que le JWT correspond toujours à l'état du compte.
/// Un compte bloqué, ou dont la SecurityVersion a changé (rôle, agence, mot de passe…), est refusé tout de suite.
/// L'état (version, statut, rôle, permissions effectives) est gardé 30 secondes en mémoire, et retiré du cache dès que le compte
/// est modifié en base (voir <see cref="AccountCacheInvalidationInterceptor"/>).
/// Le même état sert à la vérification des permissions et au périmètre par agence : aucune requête en base en plus.
/// </summary>
public sealed class SessionValidator(IdentityModuleDbContext db, IMemoryCache cache, AgencyScope agencyScope)
{
    // Filet de sécurité : la suppression du cache à l'enregistrement rend l'effet immédiat dans le cas normal.
    // Seul un cas rare (une lecture concurrente pendant la transaction qui bloque le compte) peut garder
    // l'ancien état en cache, au plus pendant cette durée.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>Jeton sans « sid » (émis avant la phase 6) : seuls le compte et la SecurityVersion sont vérifiés.</summary>
    public Task<bool> IsSessionCurrentAsync(Guid accountId, int securityVersion, CancellationToken cancellationToken) =>
        IsSessionCurrentAsync(accountId, securityVersion, sessionId: null, cancellationToken);

    /// <param name="sessionId">« sid » du jeton (phase 6) : une session fermée est refusée ; null pour un jeton plus ancien.</param>
    public async Task<bool> IsSessionCurrentAsync(
        Guid accountId, int securityVersion, Guid? sessionId, CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(accountId, cancellationToken);

        var isCurrent = state is not null
            && state.Status == AccountStatus.Active
            && state.SecurityVersion == securityVersion
            && (sessionId is not { } sid || await IsSessionOpenAsync(sid, cancellationToken));

        // Le périmètre n'est rempli que pour une session valide : sinon il reste vide (aucune agence visible).
        if (isCurrent)
        {
            agencyScope.Set(accountId, state!.Role, state.AgencyId);
        }

        return isCurrent;
    }

    internal static string CacheKey(Guid accountId) => $"identity:account-security:{accountId:N}";

    internal static string SessionCacheKey(Guid sessionId) => $"identity:session-open:{sessionId:N}";

    /// <summary>Session encore ouverte ? Gardé 30 s, retiré du cache dès qu'un de ses jetons change (intercepteur EF).</summary>
    private async Task<bool> IsSessionOpenAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var key = SessionCacheKey(sessionId);
        if (cache.TryGetValue(key, out bool open))
        {
            return open;
        }

        open = await db.RefreshTokens.AnyAsync(t => t.FamilyId == sessionId && t.RevokedAt == null, cancellationToken);
        cache.Set(key, open, CacheDuration);
        return open;
    }

    internal async Task<AccountSecurityState?> GetStateAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var key = CacheKey(accountId);
        if (cache.TryGetValue(key, out AccountSecurityState? cached))
        {
            return cached;
        }

        var account = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new { a.SecurityVersion, a.Status, a.Role, a.AgencyId, a.PermissionOverrides })
            .FirstOrDefaultAsync(cancellationToken);

        // Permissions calculées une fois ici (rôle + ajouts − retraits), puis lues dans le cache à chaque contrôle.
        var state = account is null
            ? null
            : new AccountSecurityState(
                account.SecurityVersion,
                account.Status,
                account.Role,
                account.AgencyId,
                AccountPermissions.Effective(account.Role, account.PermissionOverrides));

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
/// <param name="Permissions">Permissions effectives ; un changement augmente aussi la SecurityVersion et vide le cache.</param>
internal sealed record AccountSecurityState(
    int SecurityVersion,
    AccountStatus Status,
    RoleTemplate Role,
    Guid? AgencyId,
    IReadOnlySet<string> Permissions);