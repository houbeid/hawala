using Audit.Application.Features;
using Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Audit.Infrastructure.Persistence;

internal sealed class AuditReader(AuditDbContext db) : IAuditReader
{
    public async Task<(IReadOnlyList<AuditEntry> Items, int TotalCount)> ListAsync(
        AuditFilter filter, int skip, int take, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = db.Entries.AsNoTracking()
            .Where(e => e.OccurredAt >= filter.From && e.OccurredAt < filter.ToExclusive);

        if (filter.ActorId is { } actorId)
        {
            query = query.Where(e => e.ActorId == actorId);
        }

        if (filter.ActionPrefix is { } prefix)
        {
            query = query.Where(e => e.Action.StartsWith(prefix));
        }

        if (filter.EntityType is { } entityType)
        {
            query = query.Where(e => e.EntityType == entityType);
        }

        if (filter.EntityId is { } entityId)
        {
            query = query.Where(e => e.EntityId == entityId);
        }

        if (filter.AgencyId is { } agencyId)
        {
            query = query.Where(e => e.AgencyId == agencyId);
        }
        else if (filter.OnlyAgency)
        {
            // Lecteur limité sans agence : rien (même règle que le périmètre : un oubli ne montre jamais tout).
            query = query.Where(_ => false);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
