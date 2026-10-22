using Microsoft.EntityFrameworkCore;
using Parties.Application.Abstractions;
using Parties.Domain.Scopes;

namespace Parties.Infrastructure.Persistence.Repositories;

internal sealed class PartyScopeRepository(PartiesDbContext db) : IPartyScopeRepository
{
    public Task<AccountPartyScope?> GetAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.AccountPartyScopes.FirstOrDefaultAsync(s => s.AccountId == accountId, cancellationToken);

    public async Task<VisibleParties> GetVisibleAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var scope = await db.AccountPartyScopes
            .AsNoTracking()
            .Where(s => s.AccountId == accountId && s.IsLimited)
            .Select(s => new { Ids = s.AllowedParties.Select(p => p.PartyId).ToList() })
            .FirstOrDefaultAsync(cancellationToken);

        return scope is null ? VisibleParties.All : new VisibleParties(true, scope.Ids);
    }

    public void Add(AccountPartyScope scope) => db.AccountPartyScopes.Add(scope);
}
