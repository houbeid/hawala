using Cash.Domain.Adjustments;

namespace Cash.Application.Abstractions;

/// <summary>Accès aux ajustements de solde. Le périmètre par agence est appliqué par le filtre global.</summary>
public interface IBalanceAdjustmentRepository
{
    void Add(BalanceAdjustment adjustment);

    /// <summary>Ajustements de l'agence ENREGISTRÉS depuis l'instant donné (contrôle de cohérence).</summary>
    Task<IReadOnlyList<BalanceAdjustment>> ListRecordedSinceAsync(Guid agencyId, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Ajustements d'une période (date d'opération), du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<BalanceAdjustment>> ListAsync(
        Guid? agencyId, DateTimeOffset from, DateTimeOffset toExclusive, CancellationToken cancellationToken);
}
