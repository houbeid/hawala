namespace Hawala.SharedKernel.Time;

/// <summary>
/// Source de l'heure courante pour tout le code métier.
/// On n'appelle jamais DateTime.Now / DateTimeOffset.UtcNow directement :
/// avec une horloge injectée, les tests peuvent figer le temps
/// (expiration d'un refresh token, clôture du jour, référence TR-NKC-AAAAMMJJ-…).
/// </summary>
public interface IClock
{
    /// <summary>Instant courant, toujours en UTC. La conversion en heure locale se fait à l'affichage.</summary>
    DateTimeOffset UtcNow { get; }
}