using Microsoft.EntityFrameworkCore;
using Parties.Application.Abstractions;
using Parties.Domain.Parties;

namespace Parties.Infrastructure.Persistence.Repositories;

internal sealed class PartyRepository(PartiesDbContext db) : IPartyRepository
{
    // Les comptes par agence (OwnsMany) sont toujours chargés avec le tiers.
    public Task<Party?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Parties.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Party>> ListAsync(PartyFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = db.Parties.AsNoTracking();

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

    public Task<int> CountAsync(bool includeArchived, CancellationToken cancellationToken) =>
        includeArchived
            ? db.Parties.CountAsync(cancellationToken)
            : db.Parties.CountAsync(p => !p.IsArchived, cancellationToken);

    public Task<bool> PhoneExistsAsync(PartyPhone phone, Guid? excludingPartyId, CancellationToken cancellationToken) =>
        db.Parties.AnyAsync(
            p => p.Phone == phone && (excludingPartyId == null || p.Id != excludingPartyId),
            cancellationToken);

    public Task<bool> NniExistsAsync(string nni, Guid? excludingPartyId, CancellationToken cancellationToken) =>
        db.Parties.AnyAsync(
            p => p.Nni == nni && (excludingPartyId == null || p.Id != excludingPartyId),
            cancellationToken);

    public void Add(Party party) => db.Parties.Add(party);

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
