namespace Profit.Contracts;

/// <summary>Porte publique du Bénéfice pour le Bilan.</summary>
public interface IProfitHistory
{
    /// <summary>
    /// « الأرباح المحفوظة » d'une caisse : cumul des bénéfices du jour enregistrés pour cette agence (transferts qu'elle a envoyés),
    /// en MRO, à titre d'information (validé le 21 octobre 2026 : non ajouté au capital).
    /// </summary>
    Task<decimal> SumSavedProfitsAsync(Guid agencyId, CancellationToken cancellationToken);
}
