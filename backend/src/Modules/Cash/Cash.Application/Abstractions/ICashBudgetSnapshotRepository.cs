using Cash.Domain.Budgets;

namespace Cash.Application.Abstractions;

/// <summary>Photos du budget de la caisse. Le périmètre par agence est appliqué par le filtre global.</summary>
public interface ICashBudgetSnapshotRepository
{
    void Add(CashBudgetSnapshot snapshot);

    /// <summary>Dernière photo enregistrée AVANT le jour donné, ou null (début de la période du contrôle de cohérence).</summary>
    Task<CashBudgetSnapshot?> GetLastBeforeAsync(Guid agencyId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>La photo d'un jour, suivie et VERROUILLÉE (deux enregistrements simultanés ne créent pas deux photos), ou null.</summary>
    Task<CashBudgetSnapshot?> GetForUpdateAsync(Guid agencyId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Photos d'une période, du plus ancien au plus récent (ordre de la courbe).</summary>
    Task<IReadOnlyList<CashBudgetSnapshot>> ListAsync(Guid agencyId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
