using Microsoft.EntityFrameworkCore;
using Profit.Application.Abstractions;
using Profit.Domain.Snapshots;

namespace Profit.Infrastructure.Persistence;

/// <summary>
/// Base du Bénéfice, dans le schéma PostgreSQL « profit ». Module sans écriture d'argent : il garde sa propre connexion.
/// Pas de filtre par agence : tout le module est réservé au Superviseur (profit.view).
/// </summary>
public sealed class ProfitDbContext(DbContextOptions<ProfitDbContext> options) : DbContext(options), IUnitOfWork
{
    public const string Schema = "profit";

    public DbSet<ProfitSnapshot> Snapshots => Set<ProfitSnapshot>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ProfitSnapshot>(snapshot =>
        {
            snapshot.ToTable("profit_snapshots");
            snapshot.HasKey(s => s.Id);
            snapshot.Property(s => s.Id).ValueGeneratedNever();
            snapshot.Property(s => s.AgencyId);
            snapshot.Property(s => s.Date);
            snapshot.Property(s => s.TransferCount);
            snapshot.Property(s => s.AmountXof).HasPrecision(18, 2);
            snapshot.Property(s => s.PurchaseRate).HasPrecision(18, 2);

            // Pleine précision (règle d'arrondi 4) : le bénéfice peut avoir plus de 2 décimales.
            snapshot.Property(s => s.ProfitMro).HasPrecision(28, 10);
            snapshot.Property(s => s.SavedBy);
            snapshot.Property(s => s.SavedAt);

            // Une photo par jour et par agence ; l'agence vide (les deux) compte comme une valeur.
            snapshot.HasIndex(s => new { s.AgencyId, s.Date }).IsUnique().AreNullsDistinct(false);
        });
    }
}

internal sealed class ProfitSnapshotRepository(ProfitDbContext db) : IProfitSnapshotRepository
{
    public void Add(ProfitSnapshot snapshot) => db.Snapshots.Add(snapshot);

    public Task<ProfitSnapshot?> GetAsync(Guid? agencyId, DateOnly date, CancellationToken cancellationToken) =>
        db.Snapshots.FirstOrDefaultAsync(s => s.AgencyId == agencyId && s.Date == date, cancellationToken);

    public async Task<decimal> SumProfitsAsync(Guid agencyId, CancellationToken cancellationToken) =>
        await db.Snapshots.Where(s => s.AgencyId == agencyId).SumAsync(s => s.ProfitMro, cancellationToken);

    public async Task<IReadOnlyList<ProfitSnapshot>> ListAsync(
        Guid? agencyId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Snapshots.AsNoTracking()
            .Where(s => s.AgencyId == agencyId && s.Date >= from && s.Date <= to)
            .OrderByDescending(s => s.Date)
            .ToListAsync(cancellationToken);
}
