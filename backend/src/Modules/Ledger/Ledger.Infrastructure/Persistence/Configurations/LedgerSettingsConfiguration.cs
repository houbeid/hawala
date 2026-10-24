using Ledger.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Persistence.Configurations;

/// <summary>Table « ledger.settings » : une seule ligne (Id = 1), créée quand la date de démarrage est fixée.</summary>
internal sealed class LedgerSettingsConfiguration : IEntityTypeConfiguration<LedgerSettings>
{
    public void Configure(EntityTypeBuilder<LedgerSettings> builder)
    {
        builder.ToTable("settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.StartDate).IsRequired();
        builder.Property(s => s.StartDateSetAt).IsRequired();
        builder.Property(s => s.StartDateSetBy).IsRequired();
    }
}
