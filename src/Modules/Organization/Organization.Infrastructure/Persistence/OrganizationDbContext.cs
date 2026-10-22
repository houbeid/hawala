using System.Data.Common;
using Hawala.SharedInfrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Organization.Application.Abstractions;
using Organization.Domain.Agencies;

namespace Organization.Infrastructure.Persistence;

/// <summary>Base de données du module Organisation, dans le schéma PostgreSQL « organization ».</summary>
public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options)
    : DbContext(options), IUnitOfWork, ISharedTransactionParticipant
{
    public const string Schema = "organization";

    public DbSet<Agency> Agencies => Set<Agency>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    void ISharedTransactionParticipant.UseSharedTransaction(DbTransaction? transaction) =>
        Database.UseTransaction(transaction);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrganizationDbContext).Assembly);
    }
}