using System.Data.Common;
using Cash.Application.Abstractions;
using Cash.Domain.Movements;
using Hawala.SharedInfrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Cash.Infrastructure.Persistence;

/// <summary>
/// Base de la Caisse, dans le schéma PostgreSQL « cash », sur la connexion partagée de la requête.
/// Périmètre par agence (règle validée) : un agent ne voit que les mouvements de son agence ; le Superviseur voit les deux.
/// Sans session validée, aucun mouvement n'est visible.
/// </summary>
public sealed class CashDbContext(DbContextOptions<CashDbContext> options, IAgencyScope agencyScope)
    : DbContext(options), IUnitOfWork, ISharedTransactionParticipant
{
    public const string Schema = "cash";

    public DbSet<CashMovement> Movements => Set<CashMovement>();

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

        modelBuilder.Entity<CashMovement>(movement =>
        {
            movement.ToTable("cash_movements", table =>
            {
                table.HasCheckConstraint("ck_cash_movements_amount_positive", "\"Amount\" > 0");
                // Un tiers pour un encaissement ou un décaissement, aucun pour l'approvisionnement et le retrait du patron.
                table.HasCheckConstraint(
                    "ck_cash_movements_party",
                    "(\"Type\" IN ('Receipt', 'Payment')) = (\"PartyId\" IS NOT NULL)");
            });

            movement.HasKey(m => m.Id);
            movement.Property(m => m.Id).ValueGeneratedNever();
            movement.Property(m => m.AgencyId);
            movement.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
            movement.Property(m => m.TreasuryAccountId);
            movement.Property(m => m.PartyId);
            movement.Property(m => m.Amount).HasPrecision(18, 2);
            movement.Property(m => m.Currency).HasConversion<string>().HasMaxLength(3);
            movement.Property(m => m.Note).HasMaxLength(CashMovement.NoteMaxLength);
            movement.Property(m => m.OperationAt);
            movement.Property(m => m.IdempotencyKey).HasMaxLength(CashMovement.IdempotencyKeyMaxLength).IsRequired();
            movement.Property(m => m.EntryId);
            movement.Property(m => m.CreatedBy);
            movement.Property(m => m.CreatedAt);
            movement.Ignore(m => m.IsIncoming);
            movement.Ignore(m => m.Money);

            // Une clé d'idempotence appartient au compte qui l'envoie.
            movement.HasIndex(m => new { m.CreatedBy, m.IdempotencyKey }).IsUnique();
            movement.HasIndex(m => new { m.AgencyId, m.OperationAt });
            movement.HasIndex(m => m.TreasuryAccountId);
            movement.HasIndex(m => m.PartyId);

            movement.HasQueryFilter(m => SeesAllAgencies || m.AgencyId == ScopeAgencyId);
        });
    }
}
