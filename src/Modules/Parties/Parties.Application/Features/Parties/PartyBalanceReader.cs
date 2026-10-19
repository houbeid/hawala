using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Identity.Contracts;
using Ledger.Contracts;
using Parties.Domain.Parties;

namespace Parties.Application.Features.Parties;

/// <summary>Soldes que le compte connecté a le droit de voir, par compte du grand livre.</summary>
/// <param name="Visible">Faux sans la permission parties.view_balance : soldes masqués (null dans la réponse).</param>
/// <param name="AgencyIds">Agences dont les soldes sont montrés (périmètre ∩ agence demandée).</param>
internal sealed record VisiblePartyBalances(
    bool Visible,
    IReadOnlySet<Guid> AgencyIds,
    IReadOnlyDictionary<Guid, Money> ByLedgerAccount)
{
    public static readonly VisiblePartyBalances Hidden = new(false, new HashSet<Guid>(), new Dictionary<Guid, Money>());

    public IReadOnlyList<PartyAgencyBalance>? For(Party party)
    {
        if (!Visible)
        {
            return null;
        }

        return
        [
            .. party.AgencyAccounts
                .Where(a => AgencyIds.Contains(a.AgencyId) && ByLedgerAccount.ContainsKey(a.LedgerAccountId))
                .Select(a =>
                {
                    var balance = ByLedgerAccount[a.LedgerAccountId];
                    return new PartyAgencyBalance(a.AgencyId, balance.Currency, balance.Amount);
                }),
        ];
    }
}

/// <summary>
/// Lit au grand livre, en une seule fois, les soldes des tiers dans les agences visibles, mais SEULEMENT si le
/// compte connecté a la permission parties.view_balance (règle validée). Partagé par la liste et la fiche.
/// Un agent ne voit que le solde de son agence ; le Superviseur les deux, ou celle qu'il demande.
/// </summary>
internal static class PartyBalanceReader
{
    public static async Task<Result<VisiblePartyBalances>> ReadIfAllowedAsync(
        Guid actorId,
        Guid? requestedAgencyId,
        IReadOnlyCollection<Party> parties,
        IPermissionChecker permissions,
        IAgencyScope scope,
        ILedger ledger,
        CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(actorId, Permissions.PartiesViewBalance, cancellationToken))
        {
            return VisiblePartyBalances.Hidden;
        }

        // Agences visibles : celles du périmètre, réduites à l'agence demandée s'il y en a une.
        // Une agence hors périmètre donne simplement aucun solde, sans rien révéler.
        var agencyIds = parties
            .SelectMany(p => p.AgencyAccounts)
            .Select(a => a.AgencyId)
            .Where(scope.CanSee)
            .Where(id => requestedAgencyId is null || id == requestedAgencyId)
            .ToHashSet();

        var ledgerAccountIds = parties
            .SelectMany(p => p.AgencyAccounts)
            .Where(a => agencyIds.Contains(a.AgencyId))
            .Select(a => a.LedgerAccountId)
            .ToList();

        if (ledgerAccountIds.Count == 0)
        {
            return new VisiblePartyBalances(true, agencyIds, new Dictionary<Guid, Money>());
        }

        var balances = await ledger.GetBalancesAsync(ledgerAccountIds, asOf: null, cancellationToken);
        if (balances.IsFailure)
        {
            return balances.Error;
        }

        return new VisiblePartyBalances(true, agencyIds, balances.Value.ToDictionary(b => b.AccountId, b => b.Balance));
    }
}

internal static class PartySummaryMapper
{
    public static PartySummary ToSummary(Party party, VisiblePartyBalances balances) =>
        new(
            party.Id,
            party.Name,
            party.Type.ToString(),
            party.AccountCode,
            party.Phone?.Value,
            party.Nni,
            party.Address,
            party.Note,
            party.InBalanceSheet,
            party.IsArchived,
            balances.For(party));
}
