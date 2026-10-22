using System.Data.Common;
using Hawala.SharedInfrastructure.Persistence;
using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Persistence;

/// <summary>
/// Base du grand livre, dans le schéma PostgreSQL « ledger ».
/// Travaille sur la connexion partagée de la requête (voir DependencyInjection) et ne valide jamais
/// de transaction : SaveChanges écrit dans la transaction ouverte par la commande appelante, s'il y en a une.
/// </summary>
public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options)
    : DbContext(options), IUnitOfWork, ISharedTransactionParticipant
{
    public const string Schema = "ledger";

    public DbSet<LedgerAccount> Accounts => Set<LedgerAccount>();

    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    void ISharedTransactionParticipant.UseSharedTransaction(DbTransaction? transaction) =>
        Database.UseTransaction(transaction);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LedgerDbContext).Assembly);
    }
}
