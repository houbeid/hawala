using System.Data.Common;

namespace Hawala.SharedInfrastructure.Persistence;

/// <summary>
/// Implémenté par le DbContext de chaque module d'argent (Ledger, Treasury, Parties…), enregistré dans la DI
/// sous cette interface. Le décorateur de transaction y rattache la transaction commune de la requête :
/// sans cela, EF ouvrirait sa propre transaction à chaque SaveChanges, sur la même connexion, et échouerait.
/// </summary>
public interface ISharedTransactionParticipant
{
    /// <param name="transaction">La transaction commune, ou null pour se détacher à la fin de la commande.</param>
    void UseSharedTransaction(DbTransaction? transaction);
}
