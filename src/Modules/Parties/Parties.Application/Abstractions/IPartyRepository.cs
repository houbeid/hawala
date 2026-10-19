using Parties.Domain.Parties;

namespace Parties.Application.Abstractions;

/// <summary>
/// Accès aux tiers. Pas de filtre par agence : un tiers est commun aux deux agences (règle validée) ;
/// c'est la lecture des SOLDES qui est limitée à l'agence. Enregistrement par <see cref="IUnitOfWork"/>.
/// </summary>
public interface IPartyRepository
{
    /// <summary>Un tiers avec ses comptes par agence (suivi), ou null.</summary>
    Task<Party?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Tiers correspondant aux filtres qui ne dépendent pas du solde, triés par nom. Lecture seule.
    /// POINT OUVERT : pas de pagination (quelques centaines de tiers) ; à ajouter si la liste grossit.
    /// </summary>
    Task<IReadOnlyList<Party>> ListAsync(PartyFilter filter, CancellationToken cancellationToken);

    /// <summary>Nombre total de tiers (archivés compris ou non), pour l'affichage « 12 / 73 ».</summary>
    Task<int> CountAsync(bool includeArchived, CancellationToken cancellationToken);

    /// <summary>Un autre tiers a-t-il déjà ce téléphone (E.164) ? Contrôle pour un message clair ; l'index unique reste le garde-fou.</summary>
    Task<bool> PhoneExistsAsync(PartyPhone phone, Guid? excludingPartyId, CancellationToken cancellationToken);

    /// <summary>Un autre tiers a-t-il déjà ce NNI ?</summary>
    Task<bool> NniExistsAsync(string nni, Guid? excludingPartyId, CancellationToken cancellationToken);

    void Add(Party party);
}

/// <summary>Filtres de la liste qui ne dépendent pas du solde. Un champ null = pas de filtre.</summary>
/// <param name="Search">Texte cherché dans le nom, ou chiffres cherchés dans le téléphone.</param>
/// <param name="InBalanceSheet">Vrai : « dans le bilan » ; faux : « hors bilan » ; null : tous.</param>
public sealed record PartyFilter(
    string? Search = null,
    PartyType? Type = null,
    bool? InBalanceSheet = null,
    bool IncludeArchived = false);
