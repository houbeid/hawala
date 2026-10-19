using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Treasury.Domain.Accounts;

namespace Treasury.Infrastructure.Persistence.Configurations;

internal sealed class TreasuryAccountConfiguration : IEntityTypeConfiguration<TreasuryAccount>
{
    public void Configure(EntityTypeBuilder<TreasuryAccount> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Name).HasMaxLength(TreasuryAccount.NameMaxLength).IsRequired();
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.AgencyId);
        builder.Property(a => a.Currency).HasConversion<string>().HasMaxLength(3);
        builder.Property(a => a.LedgerAccountId);
        builder.Property(a => a.IsActive);
        builder.Property(a => a.CreatedAt);

        // Règle validée : nom unique dans une agence. L'Application compare sans tenir compte de la casse ;
        // POINT OUVERT : cet index, lui, est sensible à la casse (« Bankily » / « bankily » passeraient
        // en cas de course). Le rendre insensible demandera un index sur lower("Name") en SQL dans la migration.
        builder.HasIndex(a => new { a.AgencyId, a.Name }).IsUnique();

        // Un compte de trésorerie = un compte au grand livre (pas de clé étrangère entre schémas).
        builder.HasIndex(a => a.LedgerAccountId).IsUnique();
    }
}
