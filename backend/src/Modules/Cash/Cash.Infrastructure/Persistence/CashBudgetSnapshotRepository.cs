using Cash.Application.Abstractions;
using Cash.Domain.Budgets;
using Microsoft.EntityFrameworkCore;

namespace Cash.Infrastructure.Persistence;

/// <summary>
/// POINT OUVERT : deux PREMIERS enregistrements simultanés du même jour se heurtent à l'index unique (500 au lieu de 409),
/// comme les autres index uniques du projet ; le verrou ne couvre que la photo déjà existante.
/// </summary>
internal sealed class CashBudgetSnapshotRepository(CashDbContext db) : ICashBudgetSnapshotRepository
{
    public void Add(CashBudgetSnapshot snapshot) => db.BudgetSnapshots.Add(snapshot);

    public Task<CashBudgetSnapshot?> GetLastBeforeAsync(Guid agencyId, DateOnly date, CancellationToken cancellationToken) =>
        db.BudgetSnapshots.AsNoTracking()
            .Where(s => s.AgencyId == agencyId && s.Date < date)
            .OrderByDescending(s => s.Date)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<CashBudgetSnapshot?> GetForUpdateAsync(Guid agencyId, DateOnly date, CancellationToken cancellationToken)
    {
        await db.Database
            .SqlQuery<Guid>($"""
                SELECT "Id" AS "Value"
                FROM cash.budget_snapshots
                WHERE "AgencyId" = {agencyId} AND "Date" = {date}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return await db.BudgetSnapshots.FirstOrDefaultAsync(s => s.AgencyId == agencyId && s.Date == date, cancellationToken);
    }

    public async Task<IReadOnlyList<CashBudgetSnapshot>> ListAsync(
        Guid agencyId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.BudgetSnapshots.AsNoTracking()
            .Where(s => s.AgencyId == agencyId && s.Date >= from && s.Date <= to)
            .OrderBy(s => s.Date)
            .ToListAsync(cancellationToken);
}
