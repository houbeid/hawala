namespace Profit.Domain.Snapshots;

/// <summary>Cumuls des bénéfices du jour enregistrés.</summary>
public static class SavedProfits
{
    /// <summary>
    /// Total des deux agences, pour le bilan général (décision du 31 octobre 2026) : jour par jour, la photo « des deux agences »
    /// (sans agence) fait foi si elle existe ; sinon on additionne les photos de chaque agence de ce jour. Jamais compté deux fois.
    /// </summary>
    public static decimal OfBothAgencies(IEnumerable<ProfitSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        return snapshots
            .GroupBy(s => s.Date)
            .Sum(day => day.FirstOrDefault(s => s.AgencyId is null) is { } both
                ? both.ProfitMro
                : day.Sum(s => s.ProfitMro));
    }
}
