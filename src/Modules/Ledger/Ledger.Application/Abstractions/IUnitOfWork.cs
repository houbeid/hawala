namespace Ledger.Application.Abstractions;

/// <summary>
/// Enregistre les modifications du module dans la connexion partagée de la requête.
/// Ne valide JAMAIS la transaction : c'est le décorateur de la commande appelante qui valide tout
/// ou annule tout, y compris ce que les autres modules ont écrit (une commande = une transaction).
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
