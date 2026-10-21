using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;

namespace Cash.Domain.Budgets;

/// <summary>
/// Les cinq chiffres du budget d'une agence, dans sa devise, tous lus dans le grand livre (rien n'est saisi à la main).
/// Formule validée (soustraction des non livrés tranchée le 21 octobre 2026) :
/// budget = espèces + banques + soldes débiteurs − soldes créditeurs − transferts non livrés.
/// </summary>
/// <param name="Cash">Solde des caisses en espèces de l'agence.</param>
/// <param name="Banks">Solde de ses comptes banque et wallets.</param>
/// <param name="Debtors">Tiers « dans le bilan » en rouge (ils nous doivent), en positif.</param>
/// <param name="Creditors">Tiers « dans le bilan » en vert (on leur doit), en positif.</param>
/// <param name="Undelivered">Transferts que l'agence doit encore payer.</param>
public sealed record CashBudgetFigures(decimal Cash, decimal Banks, decimal Debtors, decimal Creditors, decimal Undelivered)
{
    public decimal Budget => Cash + Banks + Debtors - Creditors - Undelivered;
}

/// <summary>
/// Photo datée du budget d'une agence (règles validées) : une par agence et par jour ; la réenregistrer le même jour remplace
/// la précédente (auteur et heure gardés) ; les jours passés ne changent jamais.
/// </summary>
public sealed class CashBudgetSnapshot
{
    private List<CoherenceAlert> _alerts = [];

    // Constructeur réservé à EF Core.
    private CashBudgetSnapshot()
    {
    }

    private CashBudgetSnapshot(
        Guid agencyId, DateOnly date, Currency currency, CashBudgetFigures figures,
        IReadOnlyList<CoherenceAlert> alerts, DateTimeOffset? checkedSince, Guid savedBy, DateTimeOffset now)
    {
        Id = IdGenerator.NewId(now);
        AgencyId = agencyId;
        Date = date;
        Currency = currency;
        Apply(figures, alerts, checkedSince, savedBy, now);
    }

    public Guid Id { get; private set; }

    public Guid AgencyId { get; private set; }

    /// <summary>Jour du budget (calendrier de l'agence).</summary>
    public DateOnly Date { get; private set; }

    public Currency Currency { get; private set; }

    public decimal Cash { get; private set; }

    public decimal Banks { get; private set; }

    public decimal Debtors { get; private set; }

    public decimal Creditors { get; private set; }

    public decimal Undelivered { get; private set; }

    public decimal Budget { get; private set; }

    public Guid SavedBy { get; private set; }

    public DateTimeOffset SavedAt { get; private set; }

    /// <summary>Début de la période analysée par le contrôle de cohérence au moment de l'enregistrement.</summary>
    public DateTimeOffset? CheckedSince { get; private set; }

    /// <summary>Alertes du contrôle de cohérence, gardées avec la photo (règle validée).</summary>
    public IReadOnlyList<CoherenceAlert> Alerts => _alerts;

    public CashBudgetFigures Figures => new(Cash, Banks, Debtors, Creditors, Undelivered);

    public static CashBudgetSnapshot Create(
        Guid agencyId,
        DateOnly date,
        Currency currency,
        CashBudgetFigures figures,
        Guid savedBy,
        DateTimeOffset now,
        IReadOnlyList<CoherenceAlert>? alerts = null,
        DateTimeOffset? checkedSince = null)
    {
        ArgumentNullException.ThrowIfNull(figures);
        return new CashBudgetSnapshot(agencyId, date, currency, figures, alerts ?? [], checkedSince, savedBy, now);
    }

    /// <summary>Réenregistrement le même jour : les chiffres, les alertes, l'auteur et l'heure sont remplacés.</summary>
    public void Replace(
        CashBudgetFigures figures,
        Guid savedBy,
        DateTimeOffset now,
        IReadOnlyList<CoherenceAlert>? alerts = null,
        DateTimeOffset? checkedSince = null)
    {
        ArgumentNullException.ThrowIfNull(figures);
        Apply(figures, alerts ?? [], checkedSince, savedBy, now);
    }

    private void Apply(
        CashBudgetFigures figures, IReadOnlyList<CoherenceAlert> alerts, DateTimeOffset? checkedSince, Guid savedBy, DateTimeOffset now)
    {
        _alerts = [.. alerts];
        CheckedSince = checkedSince;
        Cash = figures.Cash;
        Banks = figures.Banks;
        Debtors = figures.Debtors;
        Creditors = figures.Creditors;
        Undelivered = figures.Undelivered;
        Budget = figures.Budget;
        SavedBy = savedBy;
        SavedAt = now;
    }
}
