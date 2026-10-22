using System.Data.Common;
using Hawala.SharedInfrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Treasury.Application.Abstractions;
using Treasury.Domain.Accounts;

namespace Treasury.Infrastructure.Persistence;

/// <summary>
/// Base de la Trésorerie, dans le schéma PostgreSQL « treasury », sur la connexion partagée de la requête.
/// Périmètre par agence (règle validée) : un filtre global ne laisse voir que les comptes de l'agence du compte
/// connecté, ou de toutes pour un Superviseur. Sans session validée, aucun compte n'est visible.
/// </summary>
public sealed class TreasuryDbContext(DbContextOptions<TreasuryDbContext> options, IAgencyScope agencyScope)
    : DbContext(options), IUnitOfWork, ISharedTransactionParticipant
{
    public const string Schema = "treasury";

    public DbSet<TreasuryAccount> Accounts => Set<TreasuryAccount>();

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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TreasuryDbContext).Assembly);

        modelBuilder.Entity<TreasuryAccount>()
            .HasQueryFilter(a => SeesAllAgencies || a.AgencyId == ScopeAgencyId);
    }
}
