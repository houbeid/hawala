using Hawala.SharedInfrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Parties.Application.Abstractions;
using Parties.Application.Features.Scopes;
using Parties.Domain.Parties;
using Parties.Infrastructure.Persistence.Configurations;

namespace Parties.Infrastructure.Persistence.Repositories;

/// <summary>
/// Les lectures passent par <see cref="VisibleAsync"/> : un compte limité ne voit que ses tiers autorisés
/// (phase 6). Téléphone et NNI : vérifiés sur tous les tiers (l'unicité vaut pour tous).
/// </summary>
internal sealed class PartyRepository(PartiesDbContext db, PartyVisibility visibility, FieldProtector protector) : IPartyRepository
{
    // Les comptes par agence (OwnsMany) sont toujours chargés avec le tiers.
    public async Task<Party?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await (await VisibleAsync(db.Parties, cancellationToken)).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Party>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return ids.Count == 0
            ? []
            : await (await VisibleAsync(db.Parties.AsNoTracking(), cancellationToken))
                .Where(p => ids.Contains(p.Id))
                .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Party>> GetByIdsIgnoringScopeAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return ids.Count == 0
            ? []
            : await db.Parties.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Party>> ListAsync(PartyFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = filter.IgnoreAccountScope
            ? db.Parties.AsNoTracking()
            : await VisibleAsync(db.Parties.AsNoTracking(), cancellationToken);

        if (!filter.IncludeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(p => p.Type == type);
        }

        if (filter.InBalanceSheet is { } inBalanceSheet)
        {
            query = query.Where(p => p.InBalanceSheet == inBalanceSheet);
        }

        var parties = await query.OrderBy(p => p.Name).ThenBy(p => p.Id).ToListAsync(cancellationToken);

        // POINT OUVERT : la recherche texte est faite en mémoire, car le téléphone est un value object converti
        // par EF (un LIKE ne se traduit pas proprement). Suffisant pour quelques centaines de tiers ;
        // à passer en SQL avec la pagination si la liste grossit.
        return string.IsNullOrWhiteSpace(filter.Search)
            ? parties
            : [.. parties.Where(p => Matches(p, filter.Search))];
    }

    public async Task<int> CountAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var visible = await VisibleAsync(db.Parties, cancellationToken);
        return includeArchived
            ? await visible.CountAsync(cancellationToken)
            : await visible.CountAsync(p => !p.IsArchived, cancellationToken);
    }

    // Comparé sur l'empreinte : le texte chiffré change à chaque écriture.
    public Task<bool> PhoneExistsAsync(PartyPhone phone, Guid? excludingPartyId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);
        var index = protector.Index(phone.Value);
        return db.Parties.AnyAsync(
            p => EF.Property<string?>(p, PartyConfiguration.PhoneIndex) == index && (excludingPartyId == null || p.Id != excludingPartyId),
            cancellationToken);
    }

    public Task<bool> NniExistsAsync(string nni, Guid? excludingPartyId, CancellationToken cancellationToken)
    {
        var index = protector.Index(nni);
        return db.Parties.AnyAsync(
            p => EF.Property<string?>(p, PartyConfiguration.NniIndex) == index && (excludingPartyId == null || p.Id != excludingPartyId),
            cancellationToken);
    }

    public async Task<bool> LockAgencyAccountAsync(Guid partyId, Guid agencyId, CancellationToken cancellationToken)
    {
        var locked = await db.Database
            .SqlQuery<Guid>($"""
                SELECT "PartyId" AS "Value"
                FROM parties.party_agency_accounts
                WHERE "PartyId" = {partyId} AND "AgencyId" = {agencyId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return locked.Count == 1;
    }

    public void Add(Party party) => db.Parties.Add(party);

    private async Task<IQueryable<Party>> VisibleAsync(IQueryable<Party> query, CancellationToken cancellationToken)
    {
        var visible = await visibility.GetAsync(cancellationToken);
        if (!visible.IsLimited)
        {
            return query;
        }

        var allowed = visible.AllowedPartyIds.ToArray();
        return query.Where(p => allowed.Contains(p.Id));
    }

    /// <summary>
    /// Nom : contient le texte, sans tenir compte de la casse (arabe compris).
    /// Téléphone : comparé sur les chiffres seuls, pour que « 22 12 34 » trouve « +22222123456 ».
    /// </summary>
    private static bool Matches(Party party, string search)
    {
        var text = search.Trim();
        if (party.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var digits = new string([.. text.Where(char.IsAsciiDigit)]);
        return digits.Length > 0
            && party.Phone is not null
            && party.Phone.Value.Contains(digits, StringComparison.Ordinal);
    }
}
