using Ledger.Domain.Entries;

namespace Ledger.Application.Abstractions;

/// <summary>
/// Accès aux écritures. Pas de méthode de modification ni de suppression : les écritures sont immuables
/// (et la base refuse tout UPDATE / DELETE). Enregistrement par <see cref="IUnitOfWork"/>.
/// </summary>
public interface IJournalEntryRepository
{
    /// <summary>Une écriture avec ses lignes, ou null.</summary>
    Task<JournalEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Une contre-passation de cette écriture existe-t-elle déjà ? Contrôle pour un message clair ;
    /// l'index unique en base reste le vrai garde-fou si deux contre-passations arrivent en même temps.
    /// </summary>
    Task<bool> IsReversedAsync(Guid entryId, CancellationToken cancellationToken);

    /// <summary>
    /// Σ des montants signés (débit − crédit) par compte, calculée en base sans charger les écritures.
    /// Jusqu'à la date d'opération <paramref name="asOf"/> incluse si elle est donnée.
    /// Un compte sans aucune ligne est absent du résultat (solde nul).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetBalancesAsync(
        IReadOnlyCollection<Guid> accountIds,
        DateOnly? asOf,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lignes d'un compte entre deux dates d'opération incluses, avec l'en-tête de leur écriture, dans l'ordre du relevé
    /// (date d'opération, instant d'enregistrement, puis id). Lecture seule.
    /// </summary>
    Task<IReadOnlyList<AccountLine>> GetAccountLinesAsync(
        Guid accountId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    void Add(JournalEntry entry);
}
