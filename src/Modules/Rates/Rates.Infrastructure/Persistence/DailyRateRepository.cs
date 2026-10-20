using Microsoft.EntityFrameworkCore;
using Rates.Application.Abstractions;
using Rates.Domain.DailyRates;

namespace Rates.Infrastructure.Persistence;

internal sealed class DailyRateRepository(RatesDbContext db) : IDailyRateRepository
{
    public Task<DailyRate?> GetAsync(DateOnly date, CancellationToken cancellationToken) =>
        db.DailyRates.FirstOrDefaultAsync(r => r.Date == date, cancellationToken);

    public async Task<IReadOnlyList<DailyRate>> ListAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.DailyRates
            .AsNoTracking()
            .Where(r => r.Date >= from && r.Date <= to)
            .OrderByDescending(r => r.Date)
            .ToListAsync(cancellationToken);

    public void Add(DailyRate rate) => db.DailyRates.Add(rate);
}
