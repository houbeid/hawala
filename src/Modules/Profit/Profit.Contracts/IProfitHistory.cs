namespace Profit.Contracts;

/// <summary>Porte publique du Bénéfice pour le Bilan.</summary>
public interface IProfitHistory
{
    /// <summary>
    /// « الأرباح المحفوظة » : cumul des bénéfices du jour enregistrés pour les deux agences (photos générales),
    /// à titre d'information (validé le 21 octobre 2026 : non ajouté au capital).
    /// </summary>
    Task<decimal> SumSavedProfitsAsync(CancellationToken cancellationToken);
}
