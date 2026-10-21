using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cash.Application.Abstractions;
using Cash.Domain.Adjustments;
using Cash.Domain.Budgets;
using Cash.Domain.Movements;
using Hawala.SharedInfrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public DbSet<CashMovement> Movements => Set<CashMovement>();

    public DbSet<BalanceAdjustment> Adjustments => Set<BalanceAdjustment>();

    public DbSet<CashBudgetSnapshot> BudgetSnapshots => Set<CashBudgetSnapshot>();

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
                // Un compte de destination pour un virement interne seulement, différent du compte d'origine.
                table.HasCheckConstraint(
                    "ck_cash_movements_destination",
                    "(\"Type\" = 'InternalTransfer') = (\"DestinationTreasuryAccountId\" IS NOT NULL) AND (\"DestinationTreasuryAccountId\" IS NULL OR \"DestinationTreasuryAccountId\" <> \"TreasuryAccountId\")");
                // Annulé = motif, instant et contre-passation enregistrés.
                table.HasCheckConstraint(
                    "ck_cash_movements_cancellation",
                    "(\"Status\" = 'Cancelled') = (\"CancelledAt\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL AND \"CancellationEntryId\" IS NOT NULL)");
            });

            movement.HasKey(m => m.Id);
            // Valeur par défaut en base : les mouvements enregistrés avant l'annulation deviennent « Active ».
            movement.Property(m => m.Status).HasConversion<string>().HasMaxLength(20).HasDefaultValueSql("'Active'");
            movement.Property(m => m.CancellationReason).HasMaxLength(CashMovement.NoteMaxLength);
            movement.Property(m => m.CancelledAt);
            movement.Property(m => m.CancelledBy);
            movement.Property(m => m.CancellationEntryId);
            movement.Property(m => m.Id).ValueGeneratedNever();
            movement.Property(m => m.AgencyId);
            movement.Property(m => m.Type).HasConversion<string>().HasMaxLength(30);
            movement.Property(m => m.TreasuryAccountId);
            movement.Property(m => m.PartyId);
            movement.Property(m => m.DestinationTreasuryAccountId);
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
            movement.Ignore(m => m.IsInternal);

            // Une clé d'idempotence appartient au compte qui l'envoie.
            movement.HasIndex(m => new { m.CreatedBy, m.IdempotencyKey }).IsUnique();
            movement.HasIndex(m => new { m.AgencyId, m.OperationAt });
            movement.HasIndex(m => m.TreasuryAccountId);
            movement.HasIndex(m => m.DestinationTreasuryAccountId);
            movement.HasIndex(m => m.PartyId);

            movement.HasQueryFilter(m => SeesAllAgencies || m.AgencyId == ScopeAgencyId);
        });

        modelBuilder.Entity<BalanceAdjustment>(adjustment =>
        {
            adjustment.ToTable("balance_adjustments", table =>
                table.HasCheckConstraint("ck_balance_adjustments_amount_not_zero", "\"Amount\" <> 0"));

            adjustment.HasKey(a => a.Id);
            adjustment.Property(a => a.Id).ValueGeneratedNever();
            adjustment.Property(a => a.AgencyId);
            adjustment.Property(a => a.Target).HasConversion<string>().HasMaxLength(20);
            adjustment.Property(a => a.TargetId);
            adjustment.Property(a => a.Amount).HasPrecision(18, 2);
            adjustment.Property(a => a.Currency).HasConversion<string>().HasMaxLength(3);
            adjustment.Property(a => a.Reason).HasMaxLength(BalanceAdjustment.ReasonMaxLength).IsRequired();
            adjustment.Property(a => a.OperationAt);
            adjustment.Property(a => a.EntryId);
            adjustment.Property(a => a.CreatedBy);
            adjustment.Property(a => a.CreatedAt);
            adjustment.Ignore(a => a.AbsoluteMoney);

            adjustment.HasIndex(a => new { a.AgencyId, a.OperationAt });
            adjustment.HasIndex(a => a.TargetId);

            adjustment.HasQueryFilter(a => SeesAllAgencies || a.AgencyId == ScopeAgencyId);
        });

        modelBuilder.Entity<CashBudgetSnapshot>(snapshot =>
        {
            snapshot.ToTable("budget_snapshots");
            snapshot.HasKey(s => s.Id);
            snapshot.Property(s => s.Id).ValueGeneratedNever();
            snapshot.Property(s => s.AgencyId);
            snapshot.Property(s => s.Date);
            snapshot.Property(s => s.Currency).HasConversion<string>().HasMaxLength(3);
            snapshot.Property(s => s.Cash).HasPrecision(18, 2);
            snapshot.Property(s => s.Banks).HasPrecision(18, 2);
            snapshot.Property(s => s.Debtors).HasPrecision(18, 2);
            snapshot.Property(s => s.Creditors).HasPrecision(18, 2);
            snapshot.Property(s => s.Undelivered).HasPrecision(18, 2);
            snapshot.Property(s => s.Budget).HasPrecision(18, 2);
            snapshot.Property(s => s.SavedBy);
            snapshot.Property(s => s.SavedAt);
            snapshot.Ignore(s => s.Figures);
            snapshot.Property(s => s.CheckedSince);
            snapshot.Ignore(s => s.Alerts);
            snapshot.Property<List<CoherenceAlert>>("_alerts")
                .HasColumnName("Alerts")
                .HasColumnType("jsonb")
                .HasConversion(
                    alerts => JsonSerializer.Serialize(alerts, JsonOptions),
                    json => JsonSerializer.Deserialize<List<CoherenceAlert>>(json, JsonOptions) ?? new List<CoherenceAlert>(),
                    new ValueComparer<List<CoherenceAlert>>(
                        (a, b) => (a ?? new List<CoherenceAlert>()).SequenceEqual(b ?? new List<CoherenceAlert>()),
                        list => list.Aggregate(0, (hash, alert) => HashCode.Combine(hash, alert.GetHashCode())),
                        list => list.ToList()));

            // Règle validée : une photo par agence et par jour.
            snapshot.HasIndex(s => new { s.AgencyId, s.Date }).IsUnique();

            snapshot.HasQueryFilter(s => SeesAllAgencies || s.AgencyId == ScopeAgencyId);
        });
    }
}
