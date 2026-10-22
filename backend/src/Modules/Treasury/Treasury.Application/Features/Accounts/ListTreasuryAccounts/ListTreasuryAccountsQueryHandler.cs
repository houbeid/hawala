using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;
using Identity.Contracts;
using Ledger.Contracts;
using Treasury.Application.Abstractions;
using Treasury.Domain;
using Treasury.Domain.Accounts;

namespace Treasury.Application.Features.Accounts.ListTreasuryAccounts;

/// <summary>Deux lectures au plus, quelle que soit la taille de la liste : les comptes, puis leurs soldes.</summary>
public sealed class ListTreasuryAccountsQueryHandler(
    ITreasuryAccountRepository accounts,
    IPermissionChecker permissions,
    ILedger ledger)
    : IQueryHandler<ListTreasuryAccountsQuery, IReadOnlyList<TreasuryAccountSummary>>
{
    public async Task<Result<IReadOnlyList<TreasuryAccountSummary>>> Handle(
        ListTreasuryAccountsQuery query, CancellationToken cancellationToken)
    {
        TreasuryAccountType? type = null;
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            if (!EnumNames.TryParse<TreasuryAccountType>(query.Type, out var parsedType))
            {
                return TreasuryErrors.Account.TypeInvalid;
            }

            type = parsedType;
        }

        // Une agence hors périmètre donne une liste vide (filtre global), jamais une erreur qui révélerait quelque chose.
        var found = await accounts.ListAsync(
            new TreasuryAccountFilter(query.AgencyId, type, query.IncludeInactive), cancellationToken);

        var balances = await BalanceReader.ReadIfAllowedAsync(query.ActorId, found, permissions, ledger, cancellationToken);
        if (balances.IsFailure)
        {
            return balances.Error;
        }

        return Result.Success<IReadOnlyList<TreasuryAccountSummary>>(
            [.. found.Select(a => TreasuryAccountSummaryMapper.ToSummary(a, balances.Value))]);
    }
}
