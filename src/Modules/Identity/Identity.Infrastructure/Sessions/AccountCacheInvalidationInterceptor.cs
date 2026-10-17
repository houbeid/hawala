using Identity.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;

namespace Identity.Infrastructure.Sessions;

/// <summary>
/// Retire du cache l'état de sécurité de chaque compte modifié, juste avant l'enregistrement.
/// Automatique : aucun handler (blocage, rôle, mot de passe) n'a besoin d'y penser.
/// </summary>
internal sealed class AccountCacheInvalidationInterceptor(IMemoryCache cache) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Evict(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Evict(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Evict(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<UserAccount>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                cache.Remove(SessionValidator.CacheKey(entry.Entity.Id));
            }
        }
    }
}