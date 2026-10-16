using Identity.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
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

        // Enums en texte : la base reste lisible, et ajouter une valeur ne décale rien.
        builder.Property(a => a.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

        // Concurrence optimiste : deux Superviseurs qui modifient le même compte en même temps,
        // le second reçoit un conflit au lieu d'écraser le premier.
        builder.Property(a => a.SecurityVersion).IsConcurrencyToken();

        builder.Property(a => a.MustChangePassword);
        builder.Property(a => a.CreatedAt);

        // Un compte = une ligne d'identifiants ASP.NET Identity, même Id.
        builder.HasOne<AppUser>()
            .WithOne()
            .HasForeignKey<UserAccount>(a => a.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}