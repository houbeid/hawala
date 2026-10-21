using Cash.Application.Abstractions;
using Cash.Domain.Movements;
using Microsoft.EntityFrameworkCore;

namespace Cash.Infrastructure.Persistence;

internal sealed class CashMovementRepository(CashDbContext db) : ICashMovementRepository
{
    private const string LikeEscape = "\\";

    public void Add(CashMovement movement) => db.Movements.Add(movement);

    // Sans le filtre de périmètre : la clé appartient au compte, même s'il a changé d'agence depuis.
    public Task<CashMovement?> FindByIdempotencyKeyAsync(Guid createdBy, string idempotencyKey, CancellationToken cancellationToken) =>
        db.Movements.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.CreatedBy == createdBy && m.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<CashMovement?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Movements.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<CashMovement?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        // Verrou d'abord (sans filtre), puis lecture suivie AVEC le filtre de périmètre.
        await db.Database
            .SqlQuery<Guid>($"""
                SELECT "Id" AS "Value"
                FROM cash.cash_movements
                WHERE "Id" = {id}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return await db.Movements.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<(IReadOnlyList<CashMovement> Items, int TotalCount)> ListAsync(
        CashMovementFilter filter, int skip, int take, CancellationToken cancellationToken)
    {
        var query = Filtered(filter);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.OperationAt)
            .ThenByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<CashMovementTypeTotal>> TotalsAsync(CashMovementFilter filter, CancellationToken cancellationToken) =>
        await Filtered(filter)
            .Where(m => m.Status == CashMovementStatus.Active)
            .GroupBy(m => m.Type)
            .Select(g => new CashMovementTypeTotal(g.Key, g.Sum(m => m.Amount), g.Count()))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CashMovement>> ListCancelledSinceAsync(
        Guid agencyId, DateTimeOffset since, CancellationToken cancellationToken) =>
        await db.Movements.AsNoTracking()
            .Where(m => m.AgencyId == agencyId && m.Status == CashMovementStatus.Cancelled && m.CancelledAt >= since)
            .OrderBy(m => m.CancelledAt)
            .ToListAsync(cancellationToken);

    public Task<int> CountVisibleAsync(CancellationToken cancellationToken) => db.Movements.CountAsync(cancellationToken);

    private IQueryable<CashMovement> Filtered(CashMovementFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = db.Movements.AsNoTracking()
            .Where(m => m.OperationAt >= filter.From && m.OperationAt < filter.ToExclusive);

        if (filter.AgencyId is { } agencyId)
        {
            query = query.Where(m => m.AgencyId == agencyId);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(m => m.Type == type);
        }

        if (filter.TreasuryAccountId is { } accountId)
        {
            query = query.Where(m => m.TreasuryAccountId == accountId || m.DestinationTreasuryAccountId == accountId);
        }

        if (filter.Search is { } search)
        {
            // « % » et « _ » saisis sont cherchés tels quels, pas comme jokers.
            var pattern = "%" + search.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
                .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
                .Replace("_", LikeEscape + "_", StringComparison.Ordinal) + "%";
            query = query.Where(m => m.Note != null && EF.Functions.ILike(m.Note, pattern, LikeEscape));
        }

        return query;
    }
}
