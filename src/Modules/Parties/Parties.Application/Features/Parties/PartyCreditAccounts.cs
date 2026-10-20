using Parties.Application.Abstractions;
using Parties.Contracts;

namespace Parties.Application.Features.Parties;

/// <summary>Implémentation de la porte publique du crédit : verrou du compte de l'agence, puis lecture de la fiche.</summary>
public sealed class PartyCreditAccounts(IPartyRepository parties) : IPartyCreditAccounts
{
    public async Task<PartyCreditAccount?> LockForCreditAsync(Guid partyId, Guid agencyId, CancellationToken cancellationToken)
    {
        if (!await parties.LockAgencyAccountAsync(partyId, agencyId, cancellationToken))
        {
            return null;
        }

        var party = await parties.GetByIdAsync(partyId, cancellationToken);
        var account = party?.AgencyAccounts.SingleOrDefault(a => a.AgencyId == agencyId);
        return party is null || account is null
            ? null
            : new PartyCreditAccount(party.Id, party.Name, party.IsArchived, account.LedgerAccountId, account.CreditLimit);
    }
}
