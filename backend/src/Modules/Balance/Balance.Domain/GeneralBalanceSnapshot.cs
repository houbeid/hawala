using Hawala.SharedKernel.Ids;

namespace Balance.Domain;

/// <summary>
/// Photo datée du bilan général (« Sauvegarder le bilan ») : une par jour, le jour courant seulement, remplacée si on la
/// réenregistre le même jour. Elle garde le taux du bilan et les composants de chaque caisse dans sa devise (totaux seulement,
/// jamais le détail par tiers), ce qui permet de comparer deux photos poste par poste (« Expliquer la variation »).
/// </summary>
public sealed class GeneralBalanceSnapshot
{
    // Constructeur réservé à EF Core.
    private GeneralBalanceSnapshot()
    {
    }

    private GeneralBalanceSnapshot(DateOnly date, GeneralBalanceFigures figures, decimal savedProfitsMro, Guid savedBy, DateTimeOffset now)
    {
        Id = IdGenerator.NewId(now);
        Date = date;
        Apply(figures, savedProfitsMro, savedBy, now);
    }

    public Guid Id { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>Taux du bilan : MRO pour 5 000 FCFA.</summary>
    public decimal Rate { get; private set; }

    public decimal NouakchottCash { get; private set; }

    public decimal NouakchottBanks { get; private set; }

    public decimal NouakchottDebtors { get; private set; }

    public decimal NouakchottCreditors { get; private set; }

    public decimal NouakchottUndelivered { get; private set; }

    public decimal NouakchottOwnerContributions { get; private set; }

    public decimal DakarCash { get; private set; }

    public decimal DakarBanks { get; private set; }

    public decimal DakarDebtors { get; private set; }

    public decimal DakarCreditors { get; private set; }

    public decimal DakarUndelivered { get; private set; }

    public decimal DakarOwnerContributions { get; private set; }

    /// <summary>Bilan général, en MRO.</summary>
    public decimal GeneralBalance { get; private set; }

    public decimal? Capital { get; private set; }

    public decimal? CapitalUsed { get; private set; }

    public decimal? NetResult { get; private set; }

    public decimal? NetResultPercent { get; private set; }

    /// <summary>Bénéfices du jour enregistrés des deux agences (MRO), à titre d'information : hors formule.</summary>
    public decimal SavedProfitsMro { get; private set; }

    public Guid SavedBy { get; private set; }

    public DateTimeOffset SavedAt { get; private set; }

    public static GeneralBalanceSnapshot Create(
        DateOnly date, GeneralBalanceFigures figures, decimal savedProfitsMro, Guid savedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(figures);
        return new GeneralBalanceSnapshot(date, figures, savedProfitsMro, savedBy, now);
    }

    public void Replace(GeneralBalanceFigures figures, decimal savedProfitsMro, Guid savedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(figures);
        Apply(figures, savedProfitsMro, savedBy, now);
    }

    private void Apply(GeneralBalanceFigures figures, decimal savedProfitsMro, Guid savedBy, DateTimeOffset now)
    {
        Rate = figures.Rate.MroPer5000Xof;
        NouakchottCash = figures.Nouakchott.Cash;
        NouakchottBanks = figures.Nouakchott.Banks;
        NouakchottDebtors = figures.Nouakchott.Debtors;
        NouakchottCreditors = figures.Nouakchott.Creditors;
        NouakchottUndelivered = figures.Nouakchott.Undelivered;
        NouakchottOwnerContributions = figures.Nouakchott.OwnerContributions;
        DakarCash = figures.Dakar.Cash;
        DakarBanks = figures.Dakar.Banks;
        DakarDebtors = figures.Dakar.Debtors;
        DakarCreditors = figures.Dakar.Creditors;
        DakarUndelivered = figures.Dakar.Undelivered;
        DakarOwnerContributions = figures.Dakar.OwnerContributions;
        GeneralBalance = figures.GeneralBalance;
        Capital = figures.Capital;
        CapitalUsed = figures.CapitalUsed;
        NetResult = figures.NetResult;
        NetResultPercent = figures.NetResultPercent;
        SavedProfitsMro = savedProfitsMro;
        SavedBy = savedBy;
        SavedAt = now;
    }
}
