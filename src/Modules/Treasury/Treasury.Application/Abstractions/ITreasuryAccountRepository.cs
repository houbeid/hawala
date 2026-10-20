using Treasury.Domain.Accounts;

namespace Treasury.Application.Abstractions;

/// <summary>
/// Accès aux comptes de trésorerie. Les lectures passent par le périmètre de l'agence (filtre global) :
/// un agent ne lit jamais un compte de l'autre agence. Enregistrement par <see cref="IUnitOfWork"/>.
/// </summary>
public interface ITreasuryAccountRepository
{
    /// <summary>Un compte visible dans le périmètre du compte connecté (suivi), ou null.</summary>
    Task<TreasuryAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Comptes demandés et visibles dans le périmètre, en une lecture. Lecture seule.</summary>
    Task<IReadOnlyList<TreasuryAccount>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Comptes visibles dans le périmètre, filtrés, triés par agence, type puis nom. Lecture seule.</summary>
    Task<IReadOnlyList<TreasuryAccount>> ListAsync(TreasuryAccountFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Un autre compte de cette agence porte-t-il déjà ce nom (sans tenir compte de la casse) ?
    /// Vérifié sur toutes les agences, quel que soit le périmètre : la règle est la même pour tous.
    /// </summary>
    Task<bool> NameExistsAsync(Guid agencyId, string name, Guid? excludingAccountId, CancellationToken cancellationToken);

    void Add(TreasuryAccount account);
}

/// <summary>Filtres de la liste. Un champ null = pas de filtre sur ce critère.</summary>
/// <param name="IncludeInactive">Faux par défaut : les comptes désactivés sont masqués.</param>
public sealed record TreasuryAccountFilter(
    Guid? AgencyId = null,
    TreasuryAccountType? Type = null,
    bool IncludeInactive = false);
