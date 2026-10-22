using System.Data.Common;
using Audit.Application.Abstractions;
using Audit.Domain;
using Hawala.SharedInfrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Audit.Infrastructure.Persistence;

/// <summary>
/// Journal d'audit, dans le schéma PostgreSQL « audit ».
/// Travaille sur la connexion partagée de la requête et ne valide jamais de transaction : une ligne d'audit
/// est validée ou annulée avec la commande qui l'écrit (une commande = une transaction).
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options)
    : DbContext(options), IAuditStore, ISharedTransactionParticipant
{
    public const string Schema = "audit";

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    bool IAuditStore.IsInSharedTransaction => Database.CurrentTransaction is not null;

    void IAuditStore.Add(AuditEntry entry) => Entries.Add(entry);

    async Task IAuditStore.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    void ISharedTransactionParticipant.UseSharedTransaction(DbTransaction? transaction) =>
        Database.UseTransaction(transaction);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditDbContext).Assembly);
    }
}
