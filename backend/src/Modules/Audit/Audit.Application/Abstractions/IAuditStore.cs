using Audit.Domain;

namespace Audit.Application.Abstractions;

/// <summary>
/// Stockage des lignes d'audit, sur la connexion partagée de la requête.
/// Ne valide JAMAIS la transaction : c'est le décorateur de la commande appelante qui valide tout ou annule tout.
/// </summary>
public interface IAuditStore
{
    /// <summary>Vrai quand la transaction commune d'une commande est ouverte et rattachée à ce stockage.</summary>
    bool IsInSharedTransaction { get; }

    void Add(AuditEntry entry);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
