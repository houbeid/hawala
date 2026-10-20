using Hawala.SharedKernel.Money;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Transfers.Domain.Transfers;

namespace Transfers.Infrastructure.Persistence.Configurations;

internal sealed class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    private const int CurrencyMaxLength = 3;
    private const int CodeHashMaxLength = 200;
    private const int EnumMaxLength = 20;

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
        builder.Property(t => t.CreatedBy);
        builder.Property(t => t.CreatedAt);

        // Calculés depuis les montants enregistrés, jamais stockés.
        builder.Ignore(t => t.PaidAmount);
        builder.Ignore(t => t.AmountToDeliver);
        builder.Ignore(t => t.CreditMoney);

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
    }
}
