using Hawala.SharedKernel.Money;

namespace Cash.Contracts;

/// <summary>Porte publique de la Caisse pour le Bilan : le budget en direct d'une agence (même calcul que l'écran du budget).</summary>
public interface ICashBudgets
{
    /// <summary>Budget de l'agence dans sa devise ; null si l'agence est inconnue ou hors du périmètre.</summary>
    Task<CashBudgetTotal?> GetBudgetAsync(Guid agencyId, CancellationToken cancellationToken);
}

public sealed record CashBudgetTotal(Guid AgencyId, Currency Currency, decimal Budget);
