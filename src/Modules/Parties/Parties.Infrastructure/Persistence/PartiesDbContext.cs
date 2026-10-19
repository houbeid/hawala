using System.Data.Common;
using Hawala.SharedInfrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Parties.Application.Abstractions;
using Parties.Domain.Parties;

namespace Parties.Infrastructure.Persistence;

/// <summary>
/// Base des Tiers, dans le schéma PostgreSQL « parties », sur la connexion partagée de la requête.
/// Pas de filtre par agence sur les fiches : un tiers est commun aux deux agences (règle validée).
/// </summary>
public sealed class PartiesDbContext(DbContextOptions<PartiesDbContext> options)
    : DbContext(options), IUnitOfWork, ISharedTransactionParticipant
{
    public const string Schema = "parties";

    public DbSet<Party> Parties => Set<Party>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    void ISharedTransactionParticipant.UseSharedTransaction(DbTransaction? transaction) =>
        Database.UseTransaction(transaction);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PartiesDbContext).Assembly);
    }
}
