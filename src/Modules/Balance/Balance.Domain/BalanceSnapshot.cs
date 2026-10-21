using Hawala.SharedKernel.Ids;

namespace Balance.Domain;

/// <summary>
/// Photo datée du bilan général (« Enregistrer le bilan ») : une par jour, le jour courant seulement, remplacée si on la
/// réenregistre le même jour ; garde le taux, les deux budgets, le capital utilisé et le résultat net.
/// </summary>
public sealed class BalanceSnapshot
{
    // Constructeur réservé à EF Core.
    private BalanceSnapshot()
    {
    }

    private BalanceSnapshot(DateOnly date, BalanceFigures figures, decimal savedProfits, Guid savedBy, DateTimeOffset now)
    {
        Id = IdGenerator.NewId(now);
        Date = date;
        Apply(figures, savedProfits, savedBy, now);
    }

    public Guid Id { get; private set; }

    public DateOnly Date { get; private set; }

    public decimal Rate { get; private set; }

    public decimal MroBudget { get; private set; }

    public decimal XofBudget { get; private set; }

    public decimal GeneralBalance { get; private set; }

    public decimal? InitialCapital { get; private set; }

    public decimal OwnerContributionsMro { get; private set; }

    public decimal? CapitalUsed { get; private set; }

    public decimal? NetResult { get; private set; }

    public decimal? NetResultPercent { get; private set; }

    /// <summary>« الأرباح المحفوظة » : cumul des bénéfices du jour enregistrés, à titre d'information.</summary>
    public decimal SavedProfits { get; private set; }

    public Guid SavedBy { get; private set; }

    public DateTimeOffset SavedAt { get; private set; }

    public static BalanceSnapshot Create(DateOnly date, BalanceFigures figures, decimal savedProfits, Guid savedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(figures);
        return new BalanceSnapshot(date, figures, savedProfits, savedBy, now);
    }

    public void Replace(BalanceFigures figures, decimal savedProfits, Guid savedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(figures);
        Apply(figures, savedProfits, savedBy, now);
    }

    private void Apply(BalanceFigures figures, decimal savedProfits, Guid savedBy, DateTimeOffset now)
    {
        Rate = figures.Rate;
        MroBudget = figures.MroBudget;
        XofBudget = figures.XofBudget;
        GeneralBalance = figures.GeneralBalance;
        InitialCapital = figures.InitialCapital;
        OwnerContributionsMro = figures.OwnerContributionsMro;
        CapitalUsed = figures.CapitalUsed;
        NetResult = figures.NetResult;
        NetResultPercent = figures.NetResultPercent;
        SavedProfits = savedProfits;
        SavedBy = savedBy;
        SavedAt = now;
    }
}
