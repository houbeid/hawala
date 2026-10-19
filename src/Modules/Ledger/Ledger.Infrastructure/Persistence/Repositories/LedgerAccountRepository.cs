using Ledger.Application.Abstractions;
using Ledger.Contracts;
using Ledger.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Persistence.Repositories;

internal sealed class LedgerAccountRepository(LedgerDbContext db) : ILedgerAccountRepository
{
    public async Task<IReadOnlyList<LedgerAccount>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return ids.Count == 0
            ? []
            : await db.Accounts.Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);
    }

    public Task<LedgerAccount?> GetSystemAccountAsync(
        SystemAccountRole role, Guid agencyId, CancellationToken cancellationToken) =>
        db.Accounts.FirstOrDefaultAsync(a => a.SystemRole == role && a.AgencyId == agencyId, cancellationToken);

    public void Add(LedgerAccount account) => db.Accounts.Add(account);
}
