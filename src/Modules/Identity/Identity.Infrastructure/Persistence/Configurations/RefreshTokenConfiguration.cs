using Identity.Domain.Accounts;
using Identity.Domain.RefreshTokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    // SHA-256 en hexadécimal = 64 caractères.
    private const int HashLength = 64;

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);

        // Les propriétés sans setter ne sont pas mappées par convention : on les déclare toutes.
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.UserId);
        builder.Property(t => t.FamilyId);
        builder.Property(t => t.TokenHash).HasMaxLength(HashLength).IsFixedLength().IsRequired();
        builder.Property(t => t.CreatedAt);
        builder.Property(t => t.ExpiresAt);
        builder.Property(t => t.RevokedAt);
        builder.Property(t => t.RevocationReason).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.ReplacedByTokenId);

        builder.Ignore(t => t.IsRevoked);

        // Recherche à chaque rafraîchissement.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Révocation d'une session (réutilisation) ou de toutes les sessions d'un compte.
        builder.HasIndex(t => t.FamilyId);
        builder.HasIndex(t => t.UserId);

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}