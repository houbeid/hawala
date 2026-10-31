using Hawala.SharedKernel.Money;

namespace Cash.Contracts;

/// <summary>Porte publique de la Caisse pour le Bilan : le budget en direct d'une agence (même calcul que l'écran du budget).</summary>
public interface ICashBudgets
{
    /// <summary>Budget de l'agence dans sa devise ; null si l'agence est inconnue ou hors du périmètre.</summary>
    Task<CashBudgetTotal?> GetBudgetAsync(Guid agencyId, CancellationToken cancellationToken);
}

/// <summary>Budget d'une agence et ses composants, dans sa devise (totaux seulement).</summary>
/// <param name="Debtors">Tiers « dans le bilan » qui nous doivent, en positif.</param>
/// <param name="Creditors">Tiers « dans le bilan » à qui l'on doit, en positif.</param>
/// <param name="Undelivered">Transferts que l'agence doit encore payer.</param>
public sealed record CashBudgetTotal(
    Guid AgencyId,
    Currency Currency,
    decimal Budget,
    decimal Cash,
    decimal Banks,
    decimal Debtors,
    decimal Creditors,
    decimal Undelivered);
