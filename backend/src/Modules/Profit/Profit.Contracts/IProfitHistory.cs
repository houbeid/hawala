namespace Profit.Contracts;

/// <summary>Porte publique du Bénéfice pour le Bilan.</summary>
public interface IProfitHistory
{
    /// <summary>
    /// « الأرباح المحفوظة » d'une caisse : cumul des bénéfices du jour enregistrés pour cette agence (transferts qu'elle a envoyés),
    /// en MRO, à titre d'information (validé le 21 octobre 2026 : non ajouté au capital).
    /// </summary>
    Task<decimal> SumSavedProfitsAsync(Guid agencyId, CancellationToken cancellationToken);

    /// <summary>
    /// Bénéfices enregistrés des deux agences, pour le bilan général (décision du 31 octobre 2026), en MRO : jour par jour, la photo
    /// « des deux agences » fait foi si elle existe, sinon la somme des photos de chaque agence.
    /// </summary>
    Task<decimal> SumSavedProfitsOfBothAgenciesAsync(CancellationToken cancellationToken);
}
