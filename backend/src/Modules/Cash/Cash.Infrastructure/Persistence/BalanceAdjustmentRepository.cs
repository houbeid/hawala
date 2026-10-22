using Cash.Application.Abstractions;
using Cash.Domain.Adjustments;
using Microsoft.EntityFrameworkCore;

namespace Cash.Infrastructure.Persistence;

internal sealed class BalanceAdjustmentRepository(CashDbContext db) : IBalanceAdjustmentRepository
{
    public void Add(BalanceAdjustment adjustment) => db.Adjustments.Add(adjustment);

    public async Task<IReadOnlyList<BalanceAdjustment>> ListRecordedSinceAsync(
        Guid agencyId, DateTimeOffset since, CancellationToken cancellationToken) =>
        await db.Adjustments.AsNoTracking()
            .Where(a => a.AgencyId == agencyId && a.CreatedAt >= since)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BalanceAdjustment>> ListAsync(
        Guid? agencyId, DateTimeOffset from, DateTimeOffset toExclusive, CancellationToken cancellationToken)
    {
        var query = db.Adjustments.AsNoTracking().Where(a => a.OperationAt >= from && a.OperationAt < toExclusive);
        if (agencyId is { } id)
        {
            query = query.Where(a => a.AgencyId == id);
        }

        return await query.OrderByDescending(a => a.OperationAt).ThenByDescending(a => a.Id).ToListAsync(cancellationToken);
    }
}
