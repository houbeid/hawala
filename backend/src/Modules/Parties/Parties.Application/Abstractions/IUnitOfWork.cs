namespace Parties.Application.Abstractions;

/// <summary>
/// Enregistre les modifications du module dans la connexion partagée de la requête.
/// La transaction commune (Tiers + Grand livre) est ouverte et validée par le décorateur de la commande.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
