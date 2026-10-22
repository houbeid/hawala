using Hawala.SharedInfrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Parties.Domain.Parties;

namespace Parties.Infrastructure.Persistence.Configurations;

/// <summary>
/// Phase 6 : téléphone et NNI chiffrés au repos (<see cref="FieldProtector"/>) ; leur unicité passe par une empreinte
/// (colonnes « PhoneIndex » et « NniIndex », remplies par <see cref="PartiesDbContext"/> à l'enregistrement).
/// Appliquée à la main par le DbContext (elle reçoit le chiffreur), pas par la recherche dans l'assembly.
/// </summary>
internal sealed class PartyConfiguration(FieldProtector protector) : IEntityTypeConfiguration<Party>
{
    // E.164 : « + » et 15 chiffres au plus.
    private const int PhoneMaxLength = 16;

    public const string PhoneIndex = "PhoneIndex";
    public const string NniIndex = "NniIndex";

    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("parties");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name).HasMaxLength(Party.NameMaxLength).IsRequired();
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(10);

        // Chiffré au repos ; relu par PartyPhone.Create : un numéro invalide en base fait échouer la lecture.
        builder.Property(p => p.Phone)
            .HasConversion(
                phone => protector.Protect(phone!.Value),
                value => PartyPhone.Create(protector.Unprotect(value)).Value)
            .HasMaxLength(FieldProtector.StoredLength(PhoneMaxLength));

        builder.Property(p => p.Nni)
            .HasConversion(nni => protector.Protect(nni!), value => protector.Unprotect(value))
            .HasMaxLength(FieldProtector.StoredLength(Party.NniMaxLength));

        builder.Property<string?>(PhoneIndex).HasMaxLength(FieldProtector.IndexLength);
        builder.Property<string?>(NniIndex).HasMaxLength(FieldProtector.IndexLength);
        builder.Property(p => p.Address).HasMaxLength(Party.AddressMaxLength);
        builder.Property(p => p.Note).HasMaxLength(Party.NoteMaxLength);
        builder.Property(p => p.InBalanceSheet);
        builder.Property(p => p.IsArchived);
        builder.Property(p => p.CreatedAt);

        // Déduit du type, jamais stocké.
        builder.Ignore(p => p.AccountCode);

        // Règles validées : téléphone et NNI uniques quand ils sont renseignés (PostgreSQL permet plusieurs NULL),
        // vérifiés sur l'empreinte (le texte chiffré change à chaque écriture).
        builder.HasIndex(PhoneIndex).IsUnique();
        builder.HasIndex(NniIndex).IsUnique();

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
