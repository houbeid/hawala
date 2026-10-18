using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Organization.Domain.Agencies;

namespace Organization.Infrastructure.Persistence.Configurations;

internal sealed class AgencyConfiguration : IEntityTypeConfiguration<Agency>
{
    public void Configure(EntityTypeBuilder<Agency> builder)
    {
        builder.ToTable("agencies");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Code).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.HasIndex(a => a.Code).IsUnique();

        builder.Property(a => a.Name).HasMaxLength(Agency.NameMaxLength).IsRequired();

        builder.Property(a => a.Country).HasConversion<string>().HasMaxLength(20);

        // Relue par AgencyColor.Create : une couleur invalide en base fait échouer la lecture.
        builder.Property(a => a.Color)
            .HasConversion(color => color.Value, value => AgencyColor.Create(value).Value)
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(a => a.TimeZoneId).HasMaxLength(64).IsRequired();

        // Déduite du pays, jamais stockée.
        builder.Ignore(a => a.Currency);
    }
}