using System.Data.Common;
using Hawala.SharedInfrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Transfers.Application.Abstractions;
using Transfers.Domain.Transfers;

namespace Transfers.Infrastructure.Persistence;

/// <summary>
/// Base des Transferts, dans le schéma PostgreSQL « transfers », sur la connexion partagée de la requête.
/// Périmètre par agence (règle validée) : un agent voit les transferts envoyés ET reçus par son agence ;
/// le Superviseur voit tout. Sans session validée, aucun transfert n'est visible.
/// </summary>
public sealed class TransfersDbContext(DbContextOptions<TransfersDbContext> options, IAgencyScope agencyScope)
    : DbContext(options), IUnitOfWork, ISharedTransactionParticipant
{
    public const string Schema = "transfers";

    public DbSet<Transfer> Transfers => Set<Transfer>();

    internal DbSet<TransferReferenceCounter> ReferenceCounters => Set<TransferReferenceCounter>();

    // Lus à chaque requête SQL (et non à la création du contexte) : EF les traite comme des paramètres.
    private bool SeesAllAgencies => agencyScope.SeesAllAgencies;

    private Guid? ScopeAgencyId => agencyScope.AgencyId;

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    void ISharedTransactionParticipant.UseSharedTransaction(DbTransaction? transaction) =>
        Database.UseTransaction(transaction);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransfersDbContext).Assembly);

        modelBuilder.Entity<Transfer>()
            .HasQueryFilter(t => SeesAllAgencies
                || t.SourceAgencyId == ScopeAgencyId
                || t.DestinationAgencyId == ScopeAgencyId);
    }
}
