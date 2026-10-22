using Transfers.Domain.Transfers;

namespace Transfers.Application.Abstractions;

/// <summary>Accès aux transferts. Le périmètre par agence (envoyés ou reçus) est appliqué par le filtre global.</summary>
public interface ITransferRepository
{
    void Add(Transfer transfer);

    /// <summary>Transfert déjà créé par ce compte avec cette clé (double clic, resynchronisation hors ligne).</summary>
    Task<Transfer?> FindByIdempotencyKeyAsync(Guid createdBy, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Un transfert visible du compte connecté, en lecture seule, ou null.</summary>
    Task<Transfer?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Un transfert visible, suivi et VERROUILLÉ (FOR UPDATE) jusqu'à la fin de la transaction en cours,
    /// pour le modifier (livraison…) sans course entre deux agents. Null s'il est inconnu ou hors périmètre.
    /// </summary>
    Task<Transfer?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Transferts envoyés ou reçus par l'agence qui ont une ligne d'historique depuis l'instant donné (contrôle de cohérence).</summary>
    Task<IReadOnlyList<Transfer>> ListChangedSinceAsync(Guid agencyId, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Tous les transferts visibles qui répondent aux filtres, sans pagination (calcul des cartes).</summary>
    Task<IReadOnlyList<Transfer>> ListAllAsync(TransferFilter filter, CancellationToken cancellationToken);

    /// <summary>Nombre de transferts visibles, sans aucun filtre (le « 392 » de « 24 / 392 »).</summary>
    Task<int> CountVisibleAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Pose un verrou propre à ce compte jusqu'à la fin de la transaction (pg_advisory_xact_lock) : deux transferts
    /// simultanés d'un même agent passent l'un après l'autre, et le total du jour lu ensuite est à jour.
    /// </summary>
    Task LockCreatorAsync(Guid createdBy, CancellationToken cancellationToken);

    /// <summary>
    /// Total, dans la devise de l'agence d'envoi, des transferts non annulés créés par ce compte depuis cette agence et
    /// ENREGISTRÉS dans la période (jour réel, pas la date d'opération) : montant payé et part à crédit.
    /// </summary>
    Task<(decimal Paid, decimal Credit)> SumCreatedAsync(
        Guid createdBy, Guid sourceAgencyId, DateTimeOffset from, DateTimeOffset toExclusive, CancellationToken cancellationToken);

    /// <summary>Une page de transferts visibles, du plus récent au plus ancien (date d'opération), et le nombre total.</summary>
    Task<(IReadOnlyList<Transfer> Items, int TotalCount)> ListAsync(
        TransferFilter filter, int skip, int take, CancellationToken cancellationToken);
}

/// <summary>Filtres de la liste. Un champ null = pas de filtre.</summary>
/// <param name="From">Début de la période (inclus), sur la date d'opération.</param>
/// <param name="ToExclusive">Fin de la période (exclue).</param>
/// <param name="SourceAgencyId">« Envoyés » par cette agence.</param>
/// <param name="DestinationAgencyId">« Reçus » par cette agence.</param>
/// <param name="EitherAgencyId">Envoyés ou reçus par cette agence (Superviseur qui choisit une agence).</param>
/// <param name="Search">Texte cherché dans la référence, l'expéditeur, le bénéficiaire et la note.</param>
public sealed record TransferFilter(
    DateTimeOffset From,
    DateTimeOffset ToExclusive,
    TransferStatus? Status = null,
    Guid? SourceAgencyId = null,
    Guid? DestinationAgencyId = null,
    Guid? EitherAgencyId = null,
    string? Search = null);
