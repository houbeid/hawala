using System.Data.Common;
using Hawala.SharedInfrastructure.Persistence;
using Balance.Application.Abstractions;
using Balance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Balance.Infrastructure.Persistence;

/// <summary>
/// Base du Bilan, dans le schéma PostgreSQL « balance ». Module sans écriture d'argent : il garde sa propre connexion.
/// Pas de filtre par agence : le bilan général est réservé au Superviseur.
/// </summary>
public sealed class BalanceDbContext(DbContextOptions<BalanceDbContext> options)
    : DbContext(options), IUnitOfWork, ISharedTransactionParticipant
{
    public const string Schema = "balance";

    public DbSet<CapitalChange> CapitalChanges => Set<CapitalChange>();

    public DbSet<BalanceSnapshot> Snapshots => Set<BalanceSnapshot>();

    public DbSet<GeneralCapitalChange> GeneralCapitalChanges => Set<GeneralCapitalChange>();

    public DbSet<GeneralBalanceSnapshot> GeneralSnapshots => Set<GeneralBalanceSnapshot>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    void ISharedTransactionParticipant.UseSharedTransaction(DbTransaction? transaction) =>
        Database.UseTransaction(transaction);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<CapitalChange>(change =>
        {
            change.ToTable("capital_changes", table =>
                table.HasCheckConstraint("ck_capital_changes_amount_positive", "\"Amount\" > 0"));
            change.HasKey(c => c.Id);
            change.Property(c => c.Id).ValueGeneratedNever();
            change.Property(c => c.AgencyId);
            change.Property(c => c.Amount).HasPrecision(18, 2);
            change.Property(c => c.Currency).HasConversion<string>().HasMaxLength(3);
            change.Property(c => c.Reason).HasMaxLength(CapitalChange.ReasonMaxLength);
            change.Property(c => c.SetBy);
            change.Property(c => c.SetAt);
            change.HasIndex(c => new { c.AgencyId, c.SetAt });
        });

        modelBuilder.Entity<BalanceSnapshot>(snapshot =>
        {
            snapshot.ToTable("balance_snapshots");
            snapshot.HasKey(s => s.Id);
            snapshot.Property(s => s.Id).ValueGeneratedNever();
            snapshot.Property(s => s.AgencyId);
            snapshot.Property(s => s.Date);
            snapshot.Property(s => s.Currency).HasConversion<string>().HasMaxLength(3);
            snapshot.Property(s => s.Budget).HasPrecision(18, 2);
            snapshot.Property(s => s.InitialCapital).HasPrecision(18, 2);
            snapshot.Property(s => s.OwnerContributions).HasPrecision(18, 2);
            snapshot.Property(s => s.CapitalUsed).HasPrecision(18, 2);
            snapshot.Property(s => s.NetResult).HasPrecision(18, 2);

            // Pleine précision (règle d'arrondi 4) : pourcentage et bénéfices cumulés.
            snapshot.Property(s => s.NetResultPercent).HasPrecision(28, 10);
            snapshot.Property(s => s.SavedProfitsMro).HasPrecision(28, 10);
            snapshot.Property(s => s.SavedBy);
            snapshot.Property(s => s.SavedAt);

            // Une photo par caisse et par jour.
            snapshot.HasIndex(s => new { s.AgencyId, s.Date }).IsUnique();
        });

        modelBuilder.Entity<GeneralCapitalChange>(change =>
        {
            change.ToTable("general_capital_changes", table =>
                table.HasCheckConstraint("ck_general_capital_changes_amount_positive", "\"Amount\" > 0"));
            change.HasKey(c => c.Id);
            change.Property(c => c.Id).ValueGeneratedNever();
            change.Property(c => c.Amount).HasPrecision(18, 2);
            change.Property(c => c.Reason).HasMaxLength(CapitalChange.ReasonMaxLength);
            change.Property(c => c.SetBy);
            change.Property(c => c.SetAt);
            change.HasIndex(c => c.SetAt);
        });

        modelBuilder.Entity<GeneralBalanceSnapshot>(snapshot =>
        {
            snapshot.ToTable("general_balance_snapshots");
            snapshot.HasKey(s => s.Id);
            snapshot.Property(s => s.Id).ValueGeneratedNever();
            snapshot.Property(s => s.Date);
            snapshot.Property(s => s.Rate).HasPrecision(18, 2);

            // Composants de chaque caisse dans sa devise : montants des comptes, deux décimales.
            snapshot.Property(s => s.NouakchottCash).HasPrecision(18, 2);
            snapshot.Property(s => s.NouakchottBanks).HasPrecision(18, 2);
            snapshot.Property(s => s.NouakchottDebtors).HasPrecision(18, 2);
            snapshot.Property(s => s.NouakchottCreditors).HasPrecision(18, 2);
            snapshot.Property(s => s.NouakchottUndelivered).HasPrecision(18, 2);
            snapshot.Property(s => s.NouakchottOwnerContributions).HasPrecision(18, 2);
            snapshot.Property(s => s.DakarCash).HasPrecision(18, 2);
            snapshot.Property(s => s.DakarBanks).HasPrecision(18, 2);
            snapshot.Property(s => s.DakarDebtors).HasPrecision(18, 2);
            snapshot.Property(s => s.DakarCreditors).HasPrecision(18, 2);
            snapshot.Property(s => s.DakarUndelivered).HasPrecision(18, 2);
            snapshot.Property(s => s.DakarOwnerContributions).HasPrecision(18, 2);

            // Pleine précision (règle d'arrondi 4) : tout ce qui vient d'une conversion au taux du bilan.
            snapshot.Property(s => s.GeneralBalance).HasPrecision(28, 10);
            snapshot.Property(s => s.Capital).HasPrecision(18, 2);
            snapshot.Property(s => s.CapitalUsed).HasPrecision(28, 10);
            snapshot.Property(s => s.NetResult).HasPrecision(28, 10);
            snapshot.Property(s => s.NetResultPercent).HasPrecision(28, 10);
            snapshot.Property(s => s.SavedProfitsMro).HasPrecision(28, 10);
            snapshot.Property(s => s.SavedBy);
            snapshot.Property(s => s.SavedAt);

            // Une photo par jour.
            snapshot.HasIndex(s => s.Date).IsUnique();
        });
    }
}

internal sealed class GeneralCapitalRepository(BalanceDbContext db) : IGeneralCapitalRepository
{
    public void Add(GeneralCapitalChange change) => db.GeneralCapitalChanges.Add(change);

    public async Task<IReadOnlyList<GeneralCapitalChange>> ListAsync(CancellationToken cancellationToken) =>
        await db.GeneralCapitalChanges.AsNoTracking()
            .OrderByDescending(c => c.SetAt).ThenByDescending(c => c.Id)
            .ToListAsync(cancellationToken);
}

internal sealed class GeneralBalanceSnapshotRepository(BalanceDbContext db) : IGeneralBalanceSnapshotRepository
{
    public void Add(GeneralBalanceSnapshot snapshot) => db.GeneralSnapshots.Add(snapshot);

    public Task<GeneralBalanceSnapshot?> GetAsync(DateOnly date, CancellationToken cancellationToken) =>
        db.GeneralSnapshots.FirstOrDefaultAsync(s => s.Date == date, cancellationToken);

    public async Task<IReadOnlyList<GeneralBalanceSnapshot>> ListAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.GeneralSnapshots.AsNoTracking()
            .Where(s => s.Date >= from && s.Date <= to)
            .OrderBy(s => s.Date)
            .ToListAsync(cancellationToken);
}

internal sealed class CapitalRepository(BalanceDbContext db) : ICapitalRepository
{
    public void Add(CapitalChange change) => db.CapitalChanges.Add(change);

    public Task<CapitalChange?> GetCurrentAsync(Guid agencyId, CancellationToken cancellationToken) =>
        db.CapitalChanges.AsNoTracking()
            .Where(c => c.AgencyId == agencyId)
            .OrderByDescending(c => c.SetAt).ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CapitalChange>> ListAsync(CancellationToken cancellationToken) =>
        await db.CapitalChanges.AsNoTracking().OrderByDescending(c => c.SetAt).ThenByDescending(c => c.Id).ToListAsync(cancellationToken);
}

internal sealed class BalanceSnapshotRepository(BalanceDbContext db) : IBalanceSnapshotRepository
{
    public void Add(BalanceSnapshot snapshot) => db.Snapshots.Add(snapshot);

    public Task<BalanceSnapshot?> GetAsync(Guid agencyId, DateOnly date, CancellationToken cancellationToken) =>
        db.Snapshots.FirstOrDefaultAsync(s => s.AgencyId == agencyId && s.Date == date, cancellationToken);

    public async Task<IReadOnlyList<BalanceSnapshot>> ListAsync(
        Guid? agencyId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Snapshots.AsNoTracking()
            .Where(s => (agencyId == null || s.AgencyId == agencyId) && s.Date >= from && s.Date <= to)
            .OrderBy(s => s.Date).ThenBy(s => s.AgencyId)
            .ToListAsync(cancellationToken);
}
