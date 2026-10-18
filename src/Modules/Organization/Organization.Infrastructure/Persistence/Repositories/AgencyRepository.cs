using Microsoft.EntityFrameworkCore;
using Organization.Application.Abstractions;
using Organization.Domain.Agencies;

namespace Organization.Infrastructure.Persistence.Repositories;

internal sealed class AgencyRepository(OrganizationDbContext db) : IAgencyRepository
{
    public async Task<IReadOnlyList<Agency>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.Agencies.OrderBy(a => a.Code).ToListAsync(cancellationToken);

    public Task<Agency?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Agencies.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
}