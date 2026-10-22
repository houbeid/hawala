using Ledger.Domain.Entries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Persistence.Configurations;

/// <summary>
/// Écritures et lignes. Immuables : la migration ajoute un déclencheur qui refuse tout UPDATE / DELETE,
/// et un contrôle d'équilibre par devise au COMMIT (une écriture déséquilibrée est refusée par la base).
/// </summary>
internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("journal_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.OperationDate);
        builder.Property(e => e.RecordedAt);
        builder.Property(e => e.EntryType).HasMaxLength(JournalEntry.TypeMaxLength).IsRequired();
        builder.Property(e => e.SourceModule).HasMaxLength(JournalEntry.TypeMaxLength).IsRequired();
        builder.Property(e => e.SourceId);
        builder.Property(e => e.AuthorId);
        builder.Property(e => e.Label).HasMaxLength(JournalEntry.StoredLabelMaxLength).IsRequired();
        builder.Property(e => e.Reason).HasMaxLength(JournalEntry.ReasonMaxLength);
        builder.Property(e => e.ReversesEntryId);

        builder.Ignore(e => e.IsReversal);

        // Règle validée : une écriture n'est contre-passée qu'une fois. Vrai garde-fou si deux
        // contre-passations arrivent en même temps (plusieurs NULL restent permis par PostgreSQL).
        builder.HasIndex(e => e.ReversesEntryId).IsUnique();

        builder.HasOne<JournalEntry>()
            .WithMany()
            .HasForeignKey(e => e.ReversesEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Retrouver les écritures d'un transfert, d'un mouvement de caisse…
        builder.HasIndex(e => new { e.SourceModule, e.SourceId });

        // Clé obligatoire : une ligne sans écriture échapperait au contrôle d'équilibre.
        builder.HasMany(e => e.Lines)
            .WithOne()
            .HasForeignKey("EntryId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(e => e.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
