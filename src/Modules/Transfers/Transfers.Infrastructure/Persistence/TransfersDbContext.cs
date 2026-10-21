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
/// <remarks>
/// Temps réel : à chaque enregistrement, les transferts qui ont une nouvelle ligne d'historique sont gardés dans le
/// <see cref="TransferChangeBuffer"/> ; ils ne seront notifiés qu'après la validation de la transaction.
/// </remarks>
public sealed class TransfersDbContext(
    DbContextOptions<TransfersDbContext> options, IAgencyScope agencyScope, TransferChangeBuffer changes)
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

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Lu AVANT l'enregistrement : ensuite, les lignes ajoutées ne sont plus « Added ».
        ChangeTracker.DetectChanges();
        var changed = ChangeTracker.Entries<Transfer>()
            .Select(entry => (Transfer: entry.Entity, Last: entry.Entity.Events.LastOrDefault(e => Entry(e).State == EntityState.Added)))
            .Where(x => x.Last is not null)
            .Select(x => new TransferChanged(
                x.Transfer.Id, x.Transfer.Reference, x.Last!.Type, x.Transfer.Version, x.Transfer.Status,
                x.Transfer.SourceAgencyId, x.Transfer.DestinationAgencyId))
            .ToList();

        var saved = await base.SaveChangesAsync(cancellationToken);
        changed.ForEach(changes.Add);
        return saved;
    }

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
