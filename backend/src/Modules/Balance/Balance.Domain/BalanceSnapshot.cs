using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;

namespace Balance.Domain;

/// <summary>
/// Photo datée du bilan d'une caisse (« Enregistrer le bilan ») : une par caisse et par jour, le jour courant seulement,
/// remplacée si on la réenregistre le même jour ; tout dans la devise de la caisse.
/// </summary>
public sealed class BalanceSnapshot
{
    // Constructeur réservé à EF Core.
    private BalanceSnapshot()
    {
    }

    private BalanceSnapshot(Guid agencyId, DateOnly date, BalanceFigures figures, decimal savedProfitsMro, Guid savedBy, DateTimeOffset now)
    {
        Id = IdGenerator.NewId(now);
        AgencyId = agencyId;
        Date = date;
        Apply(figures, savedProfitsMro, savedBy, now);
    }

    public Guid Id { get; private set; }

    public Guid AgencyId { get; private set; }

    public DateOnly Date { get; private set; }

    public Currency Currency { get; private set; }

    public decimal Budget { get; private set; }

    public decimal? InitialCapital { get; private set; }

    public decimal OwnerContributions { get; private set; }

    public decimal? CapitalUsed { get; private set; }

    public decimal? NetResult { get; private set; }

    public decimal? NetResultPercent { get; private set; }

    /// <summary>« الأرباح المحفوظة » de la caisse : cumul des bénéfices du jour enregistrés (toujours en MRO), à titre d'information.</summary>
    public decimal SavedProfitsMro { get; private set; }

    public Guid SavedBy { get; private set; }

    public DateTimeOffset SavedAt { get; private set; }

    public static BalanceSnapshot Create(
        Guid agencyId, DateOnly date, BalanceFigures figures, decimal savedProfitsMro, Guid savedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(figures);
        return new BalanceSnapshot(agencyId, date, figures, savedProfitsMro, savedBy, now);
    }

    public void Replace(BalanceFigures figures, decimal savedProfitsMro, Guid savedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(figures);
        Apply(figures, savedProfitsMro, savedBy, now);
    }

    private void Apply(BalanceFigures figures, decimal savedProfitsMro, Guid savedBy, DateTimeOffset now)
    {
        Currency = figures.Currency;
        Budget = figures.Budget;
        InitialCapital = figures.InitialCapital;
        OwnerContributions = figures.OwnerContributions;
        CapitalUsed = figures.CapitalUsed;
        NetResult = figures.NetResult;
        NetResultPercent = figures.NetResultPercent;
        SavedProfitsMro = savedProfitsMro;
        SavedBy = savedBy;
        SavedAt = now;
    }
}
