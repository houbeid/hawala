using Microsoft.EntityFrameworkCore;
using Treasury.Application.Abstractions;
using Treasury.Domain.Accounts;

namespace Treasury.Infrastructure.Persistence.Repositories;

internal sealed class TreasuryAccountRepository(TreasuryDbContext db) : ITreasuryAccountRepository
{
    // Les deux lectures ci-dessous passent par le filtre global du périmètre (aucun IgnoreQueryFilters).
    public Task<TreasuryAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TreasuryAccount>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return ids.Count == 0
            ? []
            : await db.Accounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TreasuryAccount>> ListAsync(
        TreasuryAccountFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = db.Accounts.AsNoTracking();

        if (filter.AgencyId is { } agencyId)
        {
            query = query.Where(a => a.AgencyId == agencyId);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(a => a.Type == type);
        }

        if (!filter.IncludeInactive)
        {
            query = query.Where(a => a.IsActive);
        }

        return await query
            .OrderBy(a => a.AgencyId)
            .ThenBy(a => a.Type)
            .ThenBy(a => a.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> NameExistsAsync(
        Guid agencyId, string name, Guid? excludingAccountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        var lowered = name.ToLowerInvariant();

        // Hors périmètre volontairement : l'unicité du nom vaut pour l'agence entière, quel que soit qui demande.
        return db.Accounts
            .IgnoreQueryFilters()
            .AnyAsync(
                a => a.AgencyId == agencyId
                    && a.Name.ToLower() == lowered
                    && (excludingAccountId == null || a.Id != excludingAccountId),
                cancellationToken);
    }

    public void Add(TreasuryAccount account) => db.Accounts.Add(account);
}
