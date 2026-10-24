using Ledger.Application.Abstractions;
using Ledger.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Persistence.Repositories;

internal sealed class LedgerSettingsRepository(LedgerDbContext db) : ILedgerSettingsRepository
{
    public Task<LedgerSettings?> GetAsync(CancellationToken cancellationToken) =>
        db.Settings.FirstOrDefaultAsync(s => s.Id == LedgerSettings.SingletonId, cancellationToken);

    public void Add(LedgerSettings settings) => db.Settings.Add(settings);
}
