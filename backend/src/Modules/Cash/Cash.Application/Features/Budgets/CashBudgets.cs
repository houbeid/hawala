using Cash.Contracts;

namespace Cash.Application.Features.Budgets;

/// <summary>Implémentation de la porte publique du budget : le calcul de l'écran, sans les détails.</summary>
public sealed class CashBudgets(CashBudgetCalculator calculator) : ICashBudgets
{
    public async Task<CashBudgetTotal?> GetBudgetAsync(Guid agencyId, CancellationToken cancellationToken)
    {
        var agency = await calculator.ResolveAgencyAsync(agencyId, cancellationToken);
        if (agency.IsFailure)
        {
            return null;
        }

        var budget = await calculator.ComputeAsync(agency.Value, cancellationToken);
        if (budget.IsFailure)
        {
            return null;
        }

        var view = budget.Value;
        return new CashBudgetTotal(
            agency.Value.Id, agency.Value.Currency, view.Budget,
            view.Cash.Amount, view.Banks.Amount, view.Debtors.Amount, view.Creditors.Amount, view.Undelivered);
    }
}
