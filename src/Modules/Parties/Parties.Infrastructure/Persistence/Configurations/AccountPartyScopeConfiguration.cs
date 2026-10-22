using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Parties.Domain.Scopes;

namespace Parties.Infrastructure.Persistence.Configurations;

/// <summary>
/// « Limiter les tiers » : une ligne par compte qui a une règle, et ses tiers autorisés.
/// AccountId vient du module Identité et PartyId de ce module : pas de clé étrangère vers identity (modules séparés).
/// </summary>
internal sealed class AccountPartyScopeConfiguration : IEntityTypeConfiguration<AccountPartyScope>
{
    public void Configure(EntityTypeBuilder<AccountPartyScope> builder)
    {
        builder.ToTable("account_party_scopes");

        builder.HasKey(s => s.AccountId);
        builder.Property(s => s.AccountId).ValueGeneratedNever();
        builder.Property(s => s.IsLimited);
        builder.Property(s => s.UpdatedAt);

        builder.OwnsMany(s => s.AllowedParties, allowed =>
        {
            allowed.ToTable("account_allowed_parties");
            allowed.WithOwner().HasForeignKey("AccountId");
            allowed.Property<Guid>("AccountId");
            // Valeur donnée par le code, jamais par la base : sans cela, EF prend un tiers ajouté à une règle
            // existante pour une ligne déjà enregistrée et ne l'insère pas.
            allowed.Property(a => a.PartyId).ValueGeneratedNever();
            allowed.HasKey("AccountId", nameof(AllowedParty.PartyId));
            allowed.HasIndex(a => a.PartyId);
        });

        builder.Navigation(s => s.AllowedParties).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
