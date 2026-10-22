using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Identity.Contracts;
using Ledger.Contracts;
using Treasury.Domain.Accounts;

namespace Treasury.Application.Features.Accounts;

/// <summary>Soldes que le compte connecté a le droit de voir, par compte du grand livre.</summary>
/// <param name="Visible">Faux sans la permission cash.budget.read : les soldes sont alors masqués (null dans la réponse).</param>
internal sealed record VisibleBalances(bool Visible, IReadOnlyDictionary<Guid, Money> ByLedgerAccount)
{
    public static readonly VisibleBalances Hidden = new(false, new Dictionary<Guid, Money>());

    public decimal? For(TreasuryAccount account) =>
        Visible && ByLedgerAccount.TryGetValue(account.LedgerAccountId, out var balance) ? balance.Amount : null;
}

/// <summary>
/// Soldes des comptes de trésorerie, lus au grand livre en une seule fois, mais SEULEMENT si le compte connecté
/// a la permission cash.budget.read (règle validée). Partagé par la liste et la fiche.
/// </summary>
internal static class BalanceReader
{
    public static async Task<Result<VisibleBalances>> ReadIfAllowedAsync(
        Guid actorId,
        IReadOnlyCollection<TreasuryAccount> accounts,
        IPermissionChecker permissions,
        ILedger ledger,
        CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(actorId, Permissions.CashBudgetRead, cancellationToken))
        {
            return VisibleBalances.Hidden;
        }

        if (accounts.Count == 0)
        {
            return new VisibleBalances(true, new Dictionary<Guid, Money>());
        }

        var balances = await ledger.GetBalancesAsync(
            [.. accounts.Select(a => a.LedgerAccountId)], asOf: null, cancellationToken);
        if (balances.IsFailure)
        {
            return balances.Error;
        }

        return new VisibleBalances(true, balances.Value.ToDictionary(b => b.AccountId, b => b.Balance));
    }
}
