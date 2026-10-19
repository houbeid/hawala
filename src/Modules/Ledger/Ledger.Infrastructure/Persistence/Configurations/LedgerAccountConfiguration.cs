using Ledger.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Persistence.Configurations;

internal sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.SystemRole).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.OwnerId);
        builder.Property(a => a.Name).HasMaxLength(LedgerAccount.NameMaxLength).IsRequired();
        builder.Property(a => a.Currency).HasConversion<string>().HasMaxLength(3);
        builder.Property(a => a.AgencyId);
        builder.Property(a => a.IsActive);
        builder.Property(a => a.CreatedAt);

        // Règle validée : un seul compte système de chaque rôle par agence.
        builder.HasIndex(a => new { a.SystemRole, a.AgencyId })
            .IsUnique()
            .HasFilter("\"SystemRole\" IS NOT NULL");

        // Une caisse a un compte ; un tiers a un compte par agence.
        builder.HasIndex(a => new { a.OwnerId, a.AgencyId })
            .IsUnique()
            .HasFilter("\"OwnerId\" IS NOT NULL");

        // Lecture des comptes d'une agence (budget de la caisse, périmètre).
        builder.HasIndex(a => a.AgencyId);
    }
}
