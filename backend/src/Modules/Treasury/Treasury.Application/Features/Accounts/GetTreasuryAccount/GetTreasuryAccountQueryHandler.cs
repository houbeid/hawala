using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Contracts;
using Ledger.Contracts;
using Treasury.Application.Abstractions;
using Treasury.Application.Features.Accounts.SetTreasuryOpeningBalance;
using Treasury.Domain;

namespace Treasury.Application.Features.Accounts.GetTreasuryAccount;

public sealed class GetTreasuryAccountQueryHandler(
    ITreasuryAccountRepository accounts,
    IPermissionChecker permissions,
    ILedger ledger)
    : IQueryHandler<GetTreasuryAccountQuery, TreasuryAccountSummary>
{
    public async Task<Result<TreasuryAccountSummary>> Handle(GetTreasuryAccountQuery query, CancellationToken cancellationToken)
    {
        // Un compte de l'autre agence est « introuvable » pour un agent : 404, sans révéler qu'il existe.
        var account = await accounts.GetByIdAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return TreasuryErrors.Account.NotFound;
        }

        var balances = await BalanceReader.ReadIfAllowedAsync(query.ActorId, [account], permissions, ledger, cancellationToken);
        if (balances.IsFailure)
        {
            return balances.Error;
        }

        var summary = TreasuryAccountSummaryMapper.ToSummary(account, balances.Value);
        if (!balances.Value.Visible)
        {
            return summary;
        }

        var opening = await ledger.SumEntriesAsync(account.LedgerAccountId, SetTreasuryOpeningBalanceCommandHandler.EntryType, cancellationToken);
        return summary with { OpeningBalance = opening };
    }
}
