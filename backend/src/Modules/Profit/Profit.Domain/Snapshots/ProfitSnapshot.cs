using Hawala.SharedKernel.Ids;

namespace Profit.Domain.Snapshots;

/// <summary>Les chiffres du bénéfice d'un jour, calculés depuis les transferts (rien n'est saisi).</summary>
/// <param name="AmountXof">Montant envoyé du jour, en FCFA.</param>
/// <param name="PurchaseRate">Prix d'achat du jour utilisé ; null si les transferts du jour en ont plusieurs.</param>
/// <param name="ProfitMro">Bénéfice du jour, en pleine précision.</param>
public sealed record ProfitFigures(int TransferCount, decimal AmountXof, decimal? PurchaseRate, decimal ProfitMro);

/// <summary>
/// Photo du bénéfice du jour (validée le 21 octobre 2026) : une par jour pour une agence (transferts qu'elle a envoyés),
/// ou pour les deux (bénéfice général, <see cref="AgencyId"/> null). Le jour courant seulement ; la réenregistrer le même jour
/// remplace la précédente (auteur et heure gardés) ; les jours passés ne changent jamais.
/// </summary>
public sealed class ProfitSnapshot
{
    // Constructeur réservé à EF Core.
    private ProfitSnapshot()
    {
    }

    private ProfitSnapshot(Guid? agencyId, DateOnly date, ProfitFigures figures, Guid savedBy, DateTimeOffset now)
    {
        Id = IdGenerator.NewId(now);
        AgencyId = agencyId;
        Date = date;
        Apply(figures, savedBy, now);
    }

    public Guid Id { get; private set; }

    /// <summary>Agence d'envoi des transferts comptés ; null : les deux agences (bénéfice général).</summary>
    public Guid? AgencyId { get; private set; }

    public DateOnly Date { get; private set; }

    public int TransferCount { get; private set; }

    public decimal AmountXof { get; private set; }

    public decimal? PurchaseRate { get; private set; }

    public decimal ProfitMro { get; private set; }

    public Guid SavedBy { get; private set; }

    public DateTimeOffset SavedAt { get; private set; }

    public static ProfitSnapshot Create(Guid? agencyId, DateOnly date, ProfitFigures figures, Guid savedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(figures);
        return new ProfitSnapshot(agencyId, date, figures, savedBy, now);
    }

    /// <summary>Réenregistrement le même jour : les chiffres, l'auteur et l'heure sont remplacés.</summary>
    public void Replace(ProfitFigures figures, Guid savedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(figures);
        Apply(figures, savedBy, now);
    }

    private void Apply(ProfitFigures figures, Guid savedBy, DateTimeOffset now)
    {
        TransferCount = figures.TransferCount;
        AmountXof = figures.AmountXof;
        PurchaseRate = figures.PurchaseRate;
        ProfitMro = figures.ProfitMro;
        SavedBy = savedBy;
        SavedAt = now;
    }
}
