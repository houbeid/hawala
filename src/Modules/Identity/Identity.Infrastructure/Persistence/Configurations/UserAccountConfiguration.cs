using Identity.Domain.Accounts;
using Identity.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    private const int PermissionMaxLength = 50;

    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        // Les value objects sont stockés sous leur forme normalisée. À la lecture, on repasse par
        // Create : une valeur invalide en base fait échouer la lecture au lieu de circuler silencieusement.
        builder.Property(a => a.Email)
            .HasConversion(email => email.Value, value => Email.Create(value).Value)
            .HasMaxLength(Email.MaxLength)
            .IsRequired();
        builder.HasIndex(a => a.Email).IsUnique();

        builder.Property(a => a.Phone)
            .HasConversion(phone => phone!.Value, value => PhoneNumber.Create(value).Value)
            .HasMaxLength(16);
        // Unique seulement entre numéros renseignés : PostgreSQL accepte plusieurs NULL.
        builder.HasIndex(a => a.Phone).IsUnique();

        builder.Property(a => a.FullName)
            .HasMaxLength(UserAccount.FullNameMaxLength)
            .IsRequired();

        // Agence du module Organisation. Volontairement sans clé étrangère vers organization.agencies :
        // les modules restent séparés ; l'existence de l'agence est vérifiée via IAgencyDirectory.
        builder.Property(a => a.AgencyId);
        builder.HasIndex(a => a.AgencyId);

        // Enums en texte : la base reste lisible, et ajouter une valeur ne décale rien.
        builder.Property(a => a.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

        // Concurrence optimiste : deux Superviseurs qui modifient le même compte en même temps,
        // le second reçoit un conflit au lieu d'écraser le premier.
        builder.Property(a => a.SecurityVersion).IsConcurrencyToken();

        builder.Property(a => a.MustChangePassword);
        builder.Property(a => a.CreatedAt);

        // Plafonds (phase 6), dans la devise de l'agence ; null = pas de limite.
        builder.Property(a => a.LimitPerTransfer).HasPrecision(18, 2);
        builder.Property(a => a.LimitPerDay).HasPrecision(18, 2);
        builder.Property(a => a.CreditLimitPerDay).HasPrecision(18, 2);
        builder.Ignore(a => a.Limits);

        // Ajouts / retraits par rapport au modèle du rôle (phase 6) : une ligne par permission ajustée.
        // Chargés avec le compte (collection possédée) : quelques lignes au plus par compte.
        builder.OwnsMany(a => a.PermissionOverrides, overrides =>
        {
            overrides.ToTable("account_permission_overrides");
            overrides.WithOwner().HasForeignKey("AccountId");
            overrides.Property<Guid>("AccountId");
            overrides.Property(o => o.Permission).HasMaxLength(PermissionMaxLength).IsRequired();
            overrides.Property(o => o.Kind).HasConversion<string>().HasMaxLength(20);

            // Une permission n'est ajustée qu'une fois par compte (ajoutée OU retirée).
            overrides.HasKey("AccountId", nameof(PermissionOverride.Permission));
        });

        builder.Navigation(a => a.PermissionOverrides).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Un compte = une ligne d'identifiants ASP.NET Identity, même Id.
        builder.HasOne<AppUser>()
            .WithOne()
            .HasForeignKey<UserAccount>(a => a.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}