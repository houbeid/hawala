using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;
using Identity.Contracts;
using Ledger.Contracts;
using Parties.Application.Abstractions;
using Parties.Domain;
using Parties.Domain.Parties;

namespace Parties.Application.Features.Parties.ListParties;

/// <summary>
/// Trois lectures au plus, quelle que soit la taille de la liste : les tiers, leur nombre total, leurs soldes.
/// Le filtre « solde » et les totaux se calculent ensuite en mémoire, sur les soldes visibles.
/// </summary>
public sealed class ListPartiesQueryHandler(
    IPartyRepository parties,
    IPermissionChecker permissions,
    IAgencyScope scope,
    ILedger ledger)
    : IQueryHandler<ListPartiesQuery, PartyList>
{
    public async Task<Result<PartyList>> Handle(ListPartiesQuery query, CancellationToken cancellationToken)
    {
        var filters = await ParseFiltersAsync(query, cancellationToken);
        if (filters.IsFailure)
        {
            return filters.Error;
        }

        var (type, inBalanceSheet, balanceFilter, balanceAgencyId) = filters.Value;

        var found = await parties.ListAsync(
            new PartyFilter(query.Search, type, inBalanceSheet, query.IncludeArchived), cancellationToken);
        var totalCount = await parties.CountAsync(query.IncludeArchived, cancellationToken);

        var balances = await PartyBalanceReader.ReadIfAllowedAsync(
            query.ActorId, query.AgencyId, found, permissions, scope, ledger, cancellationToken);
        if (balances.IsFailure)
        {
            return balances.Error;
        }

        var items = found
            .Select(p => PartySummaryMapper.ToSummary(p, balances.Value))
            .Where(p => Matches(p, balanceFilter, balanceAgencyId))
            .ToList();

        return new PartyList(items, items.Count, totalCount, balances.Value.Visible ? Totals(items) : null);
    }

    private async Task<Result<(PartyType? Type, bool? InBalanceSheet, BalanceFilter Balance, Guid? BalanceAgencyId)>> ParseFiltersAsync(
        ListPartiesQuery query, CancellationToken cancellationToken)
    {
        PartyType? type = null;
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            if (!EnumNames.TryParse<PartyType>(query.Type, out var parsedType))
            {
                return PartiesErrors.Party.TypeInvalid;
            }

            type = parsedType;
        }

        var balanceSheet = BalanceSheetFilter.All;
        if (!string.IsNullOrWhiteSpace(query.BalanceSheet) && !EnumNames.TryParse(query.BalanceSheet, out balanceSheet))
        {
            return PartiesErrors.List.BalanceSheetFilterInvalid;
        }

        var balance = BalanceFilter.All;
        if (!string.IsNullOrWhiteSpace(query.Balance) && !EnumNames.TryParse(query.Balance, out balance))
        {
            return PartiesErrors.List.BalanceFilterInvalid;
        }

        Guid? balanceAgencyId = null;
        if (balance != BalanceFilter.All)
        {
            // Filtrer par solde révélerait qui est rouge ou vert : seulement pour qui voit les soldes.
            if (!await permissions.HasPermissionAsync(query.ActorId, Permissions.PartiesViewBalance, cancellationToken))
            {
                return PartiesErrors.List.BalanceNotVisible;
            }

            // Un solde = une agence : un agent filtre sur la sienne, le Superviseur doit en choisir une.
            balanceAgencyId = scope.SeesAllAgencies ? query.AgencyId : scope.AgencyId;
            if (balanceAgencyId is null)
            {
                return PartiesErrors.List.AgencyRequiredForBalanceFilter;
            }
        }

        bool? inBalanceSheet = balanceSheet switch
        {
            BalanceSheetFilter.In => true,
            BalanceSheetFilter.Out => false,
            _ => null,
        };

        return (type, inBalanceSheet, balance, balanceAgencyId);
    }

    private static bool Matches(PartySummary party, BalanceFilter filter, Guid? agencyId)
    {
        if (filter == BalanceFilter.All)
        {
            return true;
        }

        var amount = party.Balances?.FirstOrDefault(b => b.AgencyId == agencyId)?.Balance ?? 0m;
        return filter switch
        {
            BalanceFilter.Red => amount > 0m,
            BalanceFilter.Green => amount < 0m,
            _ => true,
        };
    }

    /// <summary>Par devise : jamais d'addition MRO + FCFA.</summary>
    private static List<PartyTotals> Totals(IEnumerable<PartySummary> items) =>
    [
        .. items
            .SelectMany(p => p.Balances ?? [])
            .GroupBy(b => b.Currency)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var red = g.Where(b => b.Balance > 0m).Sum(b => b.Balance);
                var green = g.Where(b => b.Balance < 0m).Sum(b => b.Balance);
                return new PartyTotals(g.Key, red, green, red + green);
            }),
    ];
}
