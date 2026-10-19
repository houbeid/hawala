using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Persistence.Configurations;

/// <summary>Lignes d'écriture : montant strictement positif (contrôle en base), sens en texte.</summary>
internal sealed class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> builder)
    {
        builder.ToTable("journal_lines", table =>
        {
            table.HasCheckConstraint("ck_journal_lines_amount_positive", "\"Amount\" > 0");

            // Le contrôle d'équilibre compte en débit tout ce qui vaut 'Debit' : aucune autre valeur n'est admise.
            table.HasCheckConstraint("ck_journal_lines_side", "\"Side\" IN ('Debit', 'Credit')");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.AccountId);
        builder.Property(l => l.Side).HasConversion<string>().HasMaxLength(6);
        builder.Property(l => l.Amount).HasPrecision(18, 2);
        builder.Property(l => l.Currency).HasConversion<string>().HasMaxLength(3);

        builder.Ignore(l => l.SignedAmount);

        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(l => l.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Calcul des soldes : toutes les lignes d'un compte.
        builder.HasIndex(l => l.AccountId);
    }
}
