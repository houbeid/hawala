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

    public async Task<IReadOnlyList<PartyAccountRef>> ListBalanceSheetAccountsAsync(Guid agencyId, CancellationToken cancellationToken)
    {
        var found = await parties.ListAsync(new PartyFilter(InBalanceSheet: true, IncludeArchived: true), cancellationToken);
        return [.. found
            .Select(p => (Party: p, Account: p.AgencyAccounts.SingleOrDefault(a => a.AgencyId == agencyId)))
            .Where(x => x.Account is not null)
            .Select(x => new PartyAccountRef(x.Party.Id, x.Party.Name, x.Party.IsArchived, x.Account!.LedgerAccountId))];
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
