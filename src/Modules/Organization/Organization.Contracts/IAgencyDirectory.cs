namespace Organization.Contracts;

/// <summary>
/// Seule porte d'entrée des autres modules vers les agences.
/// Exemple : le module Identité l'utilisera pour vérifier qu'un compte est rattaché à une agence qui existe.
/// </summary>
public interface IAgencyDirectory
{
    Task<IReadOnlyList<AgencySummary>> GetAllAsync(CancellationToken cancellationToken);

    Task<AgencySummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}