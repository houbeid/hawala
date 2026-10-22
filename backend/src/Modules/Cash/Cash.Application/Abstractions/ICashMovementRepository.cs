using Cash.Domain.Movements;

namespace Cash.Application.Abstractions;

/// <summary>Accès aux mouvements de caisse. Le périmètre par agence est appliqué par le filtre global.</summary>
public interface ICashMovementRepository
{
    void Add(CashMovement movement);

    /// <summary>Mouvement déjà enregistré par ce compte avec cette clé (double clic, resynchronisation hors ligne).</summary>
    Task<CashMovement?> FindByIdempotencyKeyAsync(Guid createdBy, string idempotencyKey, CancellationToken cancellationToken);

    Task<CashMovement?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Un mouvement visible, suivi et VERROUILLÉ (FOR UPDATE) jusqu'à la fin de la transaction en cours.</summary>
    Task<CashMovement?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Une page, du plus récent au plus ancien (date d'opération), et le nombre total.</summary>
    Task<(IReadOnlyList<CashMovement> Items, int TotalCount)> ListAsync(
        CashMovementFilter filter, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Total et nombre par type des mouvements ACTIFS (annulés exclus), sur les mêmes filtres que la liste.</summary>
    Task<IReadOnlyList<CashMovementTypeTotal>> TotalsAsync(CashMovementFilter filter, CancellationToken cancellationToken);

    /// <summary>Mouvements de l'agence annulés depuis l'instant donné (contrôle de cohérence).</summary>
    Task<IReadOnlyList<CashMovement>> ListCancelledSinceAsync(Guid agencyId, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Nombre de mouvements visibles, sans filtre (le « 392 » de « 24 / 392 »).</summary>
    Task<int> CountVisibleAsync(CancellationToken cancellationToken);
}

/// <param name="From">Début de la période (inclus), sur la date d'opération.</param>
/// <param name="ToExclusive">Fin de la période (exclue).</param>
/// <param name="Search">Texte cherché dans la note.</param>
public sealed record CashMovementFilter(
    DateTimeOffset From,
    DateTimeOffset ToExclusive,
    Guid? AgencyId = null,
    CashMovementType? Type = null,
    Guid? TreasuryAccountId = null,
    string? Search = null);

public sealed record CashMovementTypeTotal(CashMovementType Type, decimal Amount, int Count);
