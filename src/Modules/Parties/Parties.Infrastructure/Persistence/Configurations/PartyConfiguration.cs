using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Parties.Domain.Parties;

namespace Parties.Infrastructure.Persistence.Configurations;

internal sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    // E.164 : « + » et 15 chiffres au plus.
    private const int PhoneMaxLength = 16;

    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("parties");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name).HasMaxLength(Party.NameMaxLength).IsRequired();
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(10);

        // Relu par PartyPhone.Create : un numéro invalide en base fait échouer la lecture.
        builder.Property(p => p.Phone)
            .HasConversion(phone => phone!.Value, value => PartyPhone.Create(value).Value)
            .HasMaxLength(PhoneMaxLength);

        builder.Property(p => p.Nni).HasMaxLength(Party.NniMaxLength);
        builder.Property(p => p.Address).HasMaxLength(Party.AddressMaxLength);
        builder.Property(p => p.Note).HasMaxLength(Party.NoteMaxLength);
        builder.Property(p => p.InBalanceSheet);
        builder.Property(p => p.IsArchived);
        builder.Property(p => p.CreatedAt);

        // Déduit du type, jamais stocké.
        builder.Ignore(p => p.AccountCode);

        // Règles validées : téléphone et NNI uniques quand ils sont renseignés (PostgreSQL permet plusieurs NULL).
        builder.HasIndex(p => p.Phone).IsUnique();
        builder.HasIndex(p => p.Nni).IsUnique();

        // Recherche par nom dans la liste.
        builder.HasIndex(p => p.Name);

        // Un compte au grand livre par agence (pas de clé étrangère vers le schéma « ledger »).
        builder.OwnsMany(p => p.AgencyAccounts, account =>
        {
            account.ToTable("party_agency_accounts");
            account.WithOwner().HasForeignKey("PartyId");
            account.HasKey("PartyId", nameof(PartyAgencyAccount.AgencyId));

            account.Property(a => a.AgencyId);
            account.Property(a => a.LedgerAccountId);
            account.Property(a => a.OpeningBalance).HasPrecision(18, 2);
            account.Property(a => a.CreditLimit).HasPrecision(18, 2);

            account.HasIndex(a => a.LedgerAccountId).IsUnique();
        });

        builder.Navigation(p => p.AgencyAccounts).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
