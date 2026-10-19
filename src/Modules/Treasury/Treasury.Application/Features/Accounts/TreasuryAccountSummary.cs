using Hawala.SharedKernel.Money;
using Treasury.Domain.Accounts;


namespace Treasury.Application.Features.Accounts;

/// <summary>Un compte de trésorerie tel que l'interface l'affiche (liste et fiche).</summary>
/// <param name="Type">Cash, Bank ou Wallet.</param>
/// <param name="Currency">MRO à Nouakchott (affichage toujours en MRO), XOF à Dakar.</param>
/// <param name="Balance">
/// Solde (débit − crédit) lu au grand livre. Null si le compte connecté n'a pas la permission cash.budget.read :
/// règle validée, un agent voit les comptes de son agence mais pas leurs soldes.
/// </param>
public sealed record TreasuryAccountSummary(
    Guid Id,
    string Name,
    string Type,
    Guid AgencyId,
    Currency Currency,
    bool IsActive,
    decimal? Balance);

internal static class TreasuryAccountSummaryMapper
{
    public static TreasuryAccountSummary ToSummary(TreasuryAccount account, VisibleBalances balances) =>
        new(
            account.Id,
            account.Name,
            account.Type.ToString(),
            account.AgencyId,
            account.Currency,
            account.IsActive,
            balances.For(account));
}
