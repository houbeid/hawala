using System.Data.Common;
using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedKernel.Money;
using Microsoft.EntityFrameworkCore;
using Rates.Application.Abstractions;
using Rates.Domain.DailyRates;

namespace Rates.Infrastructure.Persistence;

/// <summary>
/// Base des taux, dans le schéma PostgreSQL « rates ». Module sans écriture d'argent : il garde sa propre connexion
/// (les Transferts ne feront que lire le taux du jour).
/// </summary>
public sealed class RatesDbContext(DbContextOptions<RatesDbContext> options)
    : DbContext(options), IUnitOfWork, ISharedTransactionParticipant
{
    public const string Schema = "rates";

    public DbSet<DailyRate> DailyRates => Set<DailyRate>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    void ISharedTransactionParticipant.UseSharedTransaction(DbTransaction? transaction) =>
        Database.UseTransaction(transaction);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<DailyRate>(rate =>
        {
            rate.ToTable("daily_rates");

            // Un seul taux par journée (règle validée : un seul taux pour les deux agences).
            rate.HasKey(r => r.Date);
            rate.Property(r => r.Date).ValueGeneratedNever();

            // Relus par ExchangeRate.Create : un taux invalide en base fait échouer la lecture.
            rate.Property(r => r.PurchaseRate)
                .HasConversion(r => r.MroPer5000Xof, v => ExchangeRate.Create(v).Value)
                .HasPrecision(18, 2);
            rate.Property(r => r.SaleRate)
                .HasConversion(r => r.MroPer5000Xof, v => ExchangeRate.Create(v).Value)
                .HasPrecision(18, 2);

            rate.Property(r => r.UpdatedBy);
            rate.Property(r => r.UpdatedAt);
        });
    }
}
