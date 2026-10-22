using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Transfers.Infrastructure.Persistence.Configurations;

internal sealed class TransferReferenceCounterConfiguration : IEntityTypeConfiguration<TransferReferenceCounter>
{
    public void Configure(EntityTypeBuilder<TransferReferenceCounter> builder)
    {
        builder.ToTable("reference_counters");
        builder.HasKey(c => new { c.AgencyId, c.Day });
        builder.Property(c => c.LastValue);
    }
}
