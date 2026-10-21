using Parties.Application.Abstractions;
using Parties.Contracts;

namespace Parties.Application.Features.Parties;

/// <summary>Implémentation de la porte publique des noms de tiers.</summary>
public sealed class PartyDirectory(IPartyRepository parties) : IPartyDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var found = await parties.GetByIdsAsync(ids, cancellationToken);
        return found.ToDictionary(p => p.Id, p => p.Name);
    }

    public async Task<PartyAccountRef?> GetAgencyAccountAsync(Guid partyId, Guid agencyId, CancellationToken cancellationToken)
    {
        var party = (await parties.GetByIdsAsync([partyId], cancellationToken)).SingleOrDefault();
        var account = party?.AgencyAccounts.SingleOrDefault(a => a.AgencyId == agencyId);
        return party is null || account is null
            ? null
            : new PartyAccountRef(party.Id, party.Name, party.IsArchived, account.LedgerAccountId);
    }
}
