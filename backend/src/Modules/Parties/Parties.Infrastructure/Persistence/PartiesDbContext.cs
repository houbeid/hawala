using System.Data.Common;
using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedInfrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Parties.Application.Abstractions;
using Parties.Domain.Parties;
using Parties.Domain.Scopes;
using Parties.Infrastructure.Persistence.Configurations;

namespace Parties.Infrastructure.Persistence;

/// <summary>
/// Base des Tiers, dans le schéma PostgreSQL « parties », sur la connexion partagée de la requête.
/// Pas de filtre par agence sur les fiches : un tiers est commun aux deux agences (règle validée).
/// Phase 6 : téléphone et NNI chiffrés ; leurs empreintes (unicité) sont recalculées à chaque enregistrement.
/// </summary>
public sealed class PartiesDbContext(DbContextOptions<PartiesDbContext> options, FieldProtector protector)
    : DbContext(options), IUnitOfWork, ISharedTransactionParticipant
{
    public const string Schema = "parties";

    public DbSet<Party> Parties => Set<Party>();

    public DbSet<AccountPartyScope> AccountPartyScopes => Set<AccountPartyScope>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RefreshIndexes();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RefreshIndexes();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Empreinte du téléphone et du NNI de chaque tiers ajouté ou modifié (null s'ils sont absents).</summary>
    private void RefreshIndexes()
    {
        foreach (var entry in ChangeTracker.Entries<Party>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            entry.Property(PartyConfiguration.PhoneIndex).CurrentValue =
                entry.Entity.Phone is { } phone ? protector.Index(phone.Value) : null;
            entry.Property(PartyConfiguration.NniIndex).CurrentValue =
                entry.Entity.Nni is { } nni ? protector.Index(nni) : null;
        }
    }

    void ISharedTransactionParticipant.UseSharedTransaction(DbTransaction? transaction) =>
        Database.UseTransaction(transaction);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new PartyConfiguration(protector));
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PartiesDbContext).Assembly);
    }
}
