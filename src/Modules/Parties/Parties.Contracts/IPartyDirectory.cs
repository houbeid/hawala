namespace Parties.Contracts;

/// <summary>Lecture des noms de tiers par les autres modules (ex. le tiers d'un crédit dans la liste des transferts).</summary>
public interface IPartyDirectory
{
    /// <summary>Nom de chaque tiers demandé, archivés compris ; un id inconnu est simplement absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Le compte du tiers dans une agence (sans verrou), ou null si le tiers n'existe pas.</summary>
    Task<PartyAccountRef?> GetAgencyAccountAsync(Guid partyId, Guid agencyId, CancellationToken cancellationToken);
}

/// <param name="LedgerAccountId">Compte au grand livre du tiers dans cette agence.</param>
public sealed record PartyAccountRef(Guid PartyId, string Name, bool IsArchived, Guid LedgerAccountId);
