namespace Parties.Contracts;

/// <summary>Lecture des noms de tiers par les autres modules (ex. le tiers d'un crédit dans la liste des transferts).</summary>
public interface IPartyDirectory
{
    /// <summary>Nom de chaque tiers demandé, archivés compris ; un id inconnu est simplement absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
