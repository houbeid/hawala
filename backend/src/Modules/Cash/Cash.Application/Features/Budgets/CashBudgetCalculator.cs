using Cash.Domain;
using Cash.Domain.Budgets;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Identity.Contracts;
using Ledger.Contracts;
using Organization.Contracts;
using Parties.Contracts;
using Treasury.Contracts;

namespace Cash.Application.Features.Budgets;

/// <summary>Un compte ou un tiers qui compose une ligne du budget, avec son solde.</summary>
public sealed record CashBudgetItem(Guid Id, string Name, decimal Balance);

/// <summary>Une ligne du budget et son détail (« chaque ligne s'ouvre sur son détail »).</summary>
public sealed record CashBudgetLine(decimal Amount, IReadOnlyList<CashBudgetItem> Items);

/// <summary>
/// Budget de la caisse d'une agence, calculé en direct dans sa devise.
/// </summary>
/// <param name="Debtors">Tiers « dans le bilan » en rouge (ils nous doivent).</param>
/// <param name="Creditors">Tiers « dans le bilan » en vert (on leur doit), montants en positif.</param>
/// <param name="Undelivered">Transferts que l'agence doit encore payer (solde de « Transferts à livrer »).</param>
public sealed record CashBudgetView(
    Guid AgencyId,
    Currency Currency,
    CashBudgetLine Cash,
    CashBudgetLine Banks,
    CashBudgetLine Debtors,
    CashBudgetLine Creditors,
    decimal Undelivered,
    decimal Budget)
{
    public CashBudgetFigures Figures => new(Cash.Amount, Banks.Amount, Debtors.Amount, Creditors.Amount, Undelivered);
}

/// <summary>
/// Calcule le budget d'une agence (règles validées) : tout est lu dans le grand livre, pour l'agence et dans sa devise.
/// Espèces = comptes « Cash » ; Banques = comptes « Bank » et « Wallet » ; soldes débiteurs / créditeurs = tiers marqués
/// « dans le bilan » ; transferts non livrés = solde créditeur de « Transferts à livrer » de l'agence.
/// Un agent ne calcule que son agence ; le Superviseur la choisit.
/// </summary>
public sealed class CashBudgetCalculator(
    IAgencyDirectory agencies,
    IAgencyScope scope,
    ITreasuryDirectory treasury,
    IPartyDirectory parties,
    ILedger ledger)
{
    /// <summary>Agence du budget : celle de l'agent (il ne peut pas en demander une autre), ou celle choisie par le Superviseur.</summary>
    public async Task<Result<AgencySummary>> ResolveAgencyAsync(Guid? requested, CancellationToken cancellationToken)
    {
        Guid agencyId;
        if (scope.SeesAllAgencies)
        {
            if (requested is not { } chosen)
            {
                return CashErrors.Movement.AgencyRequired;
            }

            agencyId = chosen;
        }
        else
        {
            if (scope.AgencyId is not { } own || (requested is { } asked && asked != own))
            {
                return CashErrors.Movement.AgencyNotAllowed;
            }

            agencyId = own;
        }

        var agency = await agencies.GetByIdAsync(agencyId, cancellationToken);
        return agency is null ? CashErrors.Movement.AgencyRequired : agency;
    }

    public async Task<Result<CashBudgetView>> ComputeAsync(AgencySummary agency, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agency);

        var accounts = await treasury.ListByAgencyAsync(agency.Id, cancellationToken);
        var partyAccounts = await parties.ListBalanceSheetAccountsAsync(agency.Id, cancellationToken);
        var toDeliver = await ledger.FindSystemAccountAsync(SystemAccountRole.TransfersToDeliver, agency.Id, cancellationToken);

        List<Guid> ledgerIds =
        [
            .. accounts.Select(a => a.LedgerAccountId),
            .. partyAccounts.Select(p => p.LedgerAccountId),
            .. toDeliver is { } id ? [id] : Array.Empty<Guid>(),
        ];

        var balances = await ledger.GetBalancesAsync(ledgerIds, asOf: null, cancellationToken);
        if (balances.IsFailure)
        {
            return balances.Error;
        }

        var byAccount = balances.Value.ToDictionary(b => b.AccountId, b => b.Balance.Amount);
        decimal BalanceOf(Guid ledgerAccountId) => byAccount.GetValueOrDefault(ledgerAccountId);

        var cash = Line(accounts.Where(a => a.Kind == TreasuryAccountKind.Cash)
            .Select(a => new CashBudgetItem(a.Id, a.Name, BalanceOf(a.LedgerAccountId))));
        var banks = Line(accounts.Where(a => a.Kind != TreasuryAccountKind.Cash)
            .Select(a => new CashBudgetItem(a.Id, a.Name, BalanceOf(a.LedgerAccountId))));

        var partyBalances = partyAccounts
            .Select(p => new CashBudgetItem(p.PartyId, p.Name, BalanceOf(p.LedgerAccountId)))
            .ToList();
        var debtors = Line(partyBalances.Where(p => p.Balance > 0m));
        var creditors = Line(partyBalances.Where(p => p.Balance < 0m).Select(p => p with { Balance = -p.Balance }));

        // « Transferts à livrer » est un compte de passif : son solde (débit − crédit) est négatif quand l'agence doit payer.
        var undelivered = toDeliver is { } deliverAccount ? -BalanceOf(deliverAccount) : 0m;

        var figures = new CashBudgetFigures(cash.Amount, banks.Amount, debtors.Amount, creditors.Amount, undelivered);
        return new CashBudgetView(agency.Id, agency.Currency, cash, banks, debtors, creditors, undelivered, figures.Budget);
    }

    private static CashBudgetLine Line(IEnumerable<CashBudgetItem> items)
    {
        var list = items.OrderBy(i => i.Name, StringComparer.CurrentCulture).ToList();
        return new CashBudgetLine(list.Sum(i => i.Balance), list);
    }
}
