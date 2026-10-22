using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Contracts;
using Ledger.Contracts;
using Parties.Application.Abstractions;
using Parties.Domain;

namespace Parties.Application.Features.Parties.GetParty;

/// <summary>Un tiers est commun aux deux agences : la fiche est visible de tous ; seul le solde dépend du périmètre.</summary>
public sealed class GetPartyQueryHandler(
    IPartyRepository parties,
    IPermissionChecker permissions,
    IAgencyScope scope,
    ILedger ledger)
    : IQueryHandler<GetPartyQuery, PartySummary>
{
    public async Task<Result<PartySummary>> Handle(GetPartyQuery query, CancellationToken cancellationToken)
    {
        var party = await parties.GetByIdAsync(query.PartyId, cancellationToken);
        if (party is null)
        {
            return PartiesErrors.Party.NotFound;
        }

        var balances = await PartyBalanceReader.ReadIfAllowedAsync(
            query.ActorId, query.AgencyId, [party], permissions, scope, ledger, cancellationToken);
        if (balances.IsFailure)
        {
            return balances.Error;
        }

        return PartySummaryMapper.ToSummary(party, balances.Value);
    }
}
