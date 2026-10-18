using Microsoft.EntityFrameworkCore;
using Organization.Application.Abstractions;
using Organization.Domain.Agencies;

namespace Organization.Infrastructure.Persistence;

/// <summary>Base de données du module Organisation, dans le schéma PostgreSQL « organization ».</summary>
public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public const string Schema = "organization";

    public DbSet<Agency> Agencies => Set<Agency>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrganizationDbContext).Assembly);
    }
}