using System.Text.Json;
using Hawala.SharedKernel.Money;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Transfers.Domain.Transfers;

namespace Transfers.Infrastructure.Persistence.Configurations;

internal sealed class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    private const int CurrencyMaxLength = 3;
    private const int CodeHashMaxLength = 200;
    private const int EnumMaxLength = 20;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Transfer> builder)
    {
        builder.ToTable("transfers", table =>
        {
            // Filets de sécurité en base, en plus des règles du Domain.
            table.HasCheckConstraint("ck_transfers_amounts_positive", "\"AmountXof\" > 0 AND \"AmountMro\" > 0");
            table.HasCheckConstraint(
                "ck_transfers_credit",
                "(\"CreditPartyId\" IS NULL AND \"CreditAmount\" = 0) OR (\"CreditPartyId\" IS NOT NULL AND \"CreditAmount\" > 0)");
            table.HasCheckConstraint(
                "ck_transfers_code_attempts",
                "\"FailedCodeAttempts\" BETWEEN 0 AND 5");
            table.HasCheckConstraint(
                "ck_transfers_delivery",
                "(\"Status\" = 'Delivered') = (\"DeliveredAt\" IS NOT NULL AND \"DeliveryEntryId\" IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_transfers_cancellation",
                "(\"Status\" = 'Cancelled') = (\"CancelledAt\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL AND \"CancellationEntryId\" IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_transfers_without_code",
                "NOT \"DeliveredWithoutCode\" OR (\"Status\" = 'Delivered' AND \"BeneficiaryIdDocument\" IS NOT NULL AND \"DeliveryWithoutCodeReason\" IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_transfers_corridor",
                "\"SourceAgencyId\" <> \"DestinationAgencyId\" AND \"SourceCurrency\" <> \"DestinationCurrency\"");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Reference).HasMaxLength(Transfer.ReferenceMaxLength).IsRequired();
        builder.Property(t => t.SourceAgencyId);
        builder.Property(t => t.SourceCurrency).HasConversion<string>().HasMaxLength(CurrencyMaxLength);
        builder.Property(t => t.DestinationAgencyId);
        builder.Property(t => t.DestinationCurrency).HasConversion<string>().HasMaxLength(CurrencyMaxLength);
        builder.Property(t => t.AmountXof).HasPrecision(18, 2);
        builder.Property(t => t.AmountMro).HasPrecision(18, 2);

        // Relus par ExchangeRate.Create : un taux invalide en base fait échouer la lecture.
        builder.Property(t => t.SaleRate)
            .HasConversion(r => r.MroPer5000Xof, v => ExchangeRate.Create(v).Value)
            .HasPrecision(18, 2);
        builder.Property(t => t.PurchaseRate)
            .HasConversion(r => r.MroPer5000Xof, v => ExchangeRate.Create(v).Value)
            .HasPrecision(18, 2);

        builder.Property(t => t.SenderInfo).HasMaxLength(Transfer.PartyInfoMaxLength).IsRequired();
        builder.Property(t => t.BeneficiaryInfo).HasMaxLength(Transfer.PartyInfoMaxLength).IsRequired();
        builder.Property(t => t.Note).HasMaxLength(Transfer.NoteMaxLength);
        builder.Property(t => t.OperationAt);
        builder.Property(t => t.CreditPartyId);
        builder.Property(t => t.CreditAmount).HasPrecision(18, 2);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(t => t.WithdrawalCodeHash).HasMaxLength(CodeHashMaxLength).IsRequired();
        builder.Property(t => t.FailedCodeAttempts);
        builder.Property(t => t.IdempotencyKey).HasMaxLength(Transfer.IdempotencyKeyMaxLength).IsRequired();
        builder.Property(t => t.CreationEntryId);
        builder.Property(t => t.DeliveryOperationAt);
        builder.Property(t => t.DeliveredAt);
        builder.Property(t => t.DeliveredBy);
        builder.Property(t => t.DeliveryNote).HasMaxLength(Transfer.NoteMaxLength);
        builder.Property(t => t.DeliveryEntryId);
        builder.Property(t => t.DeliveredWithoutCode);
        // BeneficiaryIdDocument : chiffré au repos (phase 6), configuré par TransfersDbContext qui détient le chiffreur.
        builder.Property(t => t.DeliveryWithoutCodeReason).HasMaxLength(Transfer.NoteMaxLength);
        builder.Property(t => t.CodeUnlockedAt);
        builder.Property(t => t.CodeUnlockedBy);
        builder.Property(t => t.CodeUnlockReason).HasMaxLength(Transfer.NoteMaxLength);
        builder.Property(t => t.CodeReplacedAt);
        builder.Property(t => t.CodeReplacedBy);
        builder.Property(t => t.CancellationOperationAt);
        builder.Property(t => t.CancelledAt);
        builder.Property(t => t.CancelledBy);
        builder.Property(t => t.CancellationReason).HasMaxLength(Transfer.NoteMaxLength);
        builder.Property(t => t.CancellationEntryId);
        builder.Property(t => t.CreatedBy);

        // Contrôle des modifications concurrentes, en plus du verrou de ligne.
        builder.Property(t => t.Version).IsConcurrencyToken();
        builder.Property(t => t.CreatedAt);

        // Calculés depuis les montants enregistrés, jamais stockés.
        builder.Ignore(t => t.PaidAmount);
        builder.Ignore(t => t.AmountToDeliver);
        builder.Ignore(t => t.CreditMoney);
        builder.Ignore(t => t.IsCodeLocked);
        builder.Ignore(t => t.RemainingCodeAttempts);

        builder.HasIndex(t => t.Reference).IsUnique();

        // Une clé d'idempotence appartient au compte qui l'envoie.
        builder.HasIndex(t => new { t.CreatedBy, t.IdempotencyKey }).IsUnique();

        // Listes par agence et par date (tour suivant).
        builder.HasIndex(t => new { t.SourceAgencyId, t.OperationAt });
        builder.HasIndex(t => new { t.DestinationAgencyId, t.OperationAt });

        // Transferts à crédit d'un tiers (fiche du tiers, tours suivants).
        builder.HasIndex(t => t.CreditPartyId);

        builder.OwnsMany(t => t.Payments, payment =>
        {
            payment.ToTable("transfer_payments", table =>
                table.HasCheckConstraint("ck_transfer_payments_amount_positive", "\"Amount\" > 0"));
            payment.WithOwner().HasForeignKey("TransferId");
            payment.HasKey(p => p.Id);
            payment.Property(p => p.Id).ValueGeneratedNever();
            payment.Property(p => p.Stage).HasConversion<string>().HasMaxLength(EnumMaxLength);
            payment.Property(p => p.TreasuryAccountId);
            payment.Property(p => p.Amount).HasPrecision(18, 2);
            payment.Property(p => p.Currency).HasConversion<string>().HasMaxLength(CurrencyMaxLength);

            payment.HasIndex(p => p.TreasuryAccountId);
        });

        builder.Navigation(t => t.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Historique : ajouté seulement. Avant / après en JSONB.
        // POINT OUVERT : l'historique est chargé avec chaque transfert (collection possédée) ; à séparer si la liste ralentit.
        builder.OwnsMany(t => t.Events, history =>
        {
            history.ToTable("transfer_events");
            history.WithOwner().HasForeignKey("TransferId");
            history.HasKey(e => e.Id);
            history.Property(e => e.Id).ValueGeneratedNever();
            history.Property(e => e.Type).HasConversion<string>().HasMaxLength(EnumMaxLength + 10);
            history.Property(e => e.AuthorId);
            history.Property(e => e.At);
            history.Property(e => e.Reason).HasMaxLength(TransferEvent.ReasonMaxLength);
            history.Ignore(e => e.Changes);
            history.Property<List<TransferFieldChange>>("_changes")
                .HasColumnName("Changes")
                .HasColumnType("jsonb")
                .HasConversion(
                    changes => JsonSerializer.Serialize(changes, JsonOptions),
                    json => JsonSerializer.Deserialize<List<TransferFieldChange>>(json, JsonOptions) ?? new List<TransferFieldChange>(),
                    new ValueComparer<List<TransferFieldChange>>(
                        (a, b) => (a ?? new List<TransferFieldChange>()).SequenceEqual(b ?? new List<TransferFieldChange>()),
                        list => list.Aggregate(0, (hash, change) => HashCode.Combine(hash, change.GetHashCode())),
                        list => list.ToList()));

            history.HasIndex(e => e.At);
        });

        builder.Navigation(t => t.Events).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
