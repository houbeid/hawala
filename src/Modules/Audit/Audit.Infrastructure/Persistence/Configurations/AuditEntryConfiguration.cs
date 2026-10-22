using System.Text.Json;
using Audit.Contracts;
using Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audit.Infrastructure.Persistence.Configurations;

/// <summary>
/// Journal d'audit. Inséré seulement : la migration ajoute un déclencheur qui refuse UPDATE, DELETE et TRUNCATE
/// (SQLSTATE 23001), comme pour le grand livre. Les changements (avant / après) sont gardés en JSONB.
/// </summary>
internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.OccurredAt);
        builder.Property(e => e.Action).HasMaxLength(AuditEntry.ActionMaxLength).IsRequired();
        builder.Property(e => e.EntityType).HasMaxLength(AuditEntry.EntityTypeMaxLength).IsRequired();
        builder.Property(e => e.EntityId);
        builder.Property(e => e.ActorId);
        builder.Property(e => e.AgencyId);
        builder.Property(e => e.Reason).HasMaxLength(AuditEntry.ReasonMaxLength);
        builder.Property(e => e.IpAddress).HasMaxLength(AuditEntry.IpAddressMaxLength);
        builder.Property(e => e.UserAgent).HasMaxLength(AuditEntry.UserAgentMaxLength);

        builder.Ignore(e => e.Changes);
        builder.Property<List<AuditChange>>("_changes")
            .HasColumnName("Changes")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                changes => JsonSerializer.Serialize(changes, JsonOptions),
                json => JsonSerializer.Deserialize<List<AuditChange>>(json, JsonOptions) ?? new List<AuditChange>(),
                new ValueComparer<List<AuditChange>>(
                    (a, b) => (a ?? new List<AuditChange>()).SequenceEqual(b ?? new List<AuditChange>()),
                    list => list.Aggregate(0, (hash, change) => HashCode.Combine(hash, change.GetHashCode())),
                    list => list.ToList()));

        // Lectures prévues (tour « audit lisible ») : par période, par entité, par auteur.
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => e.ActorId);
    }
}
