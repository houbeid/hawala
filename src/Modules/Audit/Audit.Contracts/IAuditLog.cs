using Hawala.SharedKernel.Results;

namespace Audit.Contracts;

/// <summary>
/// Seule porte d'entrée des autres modules vers le journal d'audit.
/// La ligne est écrite dans la transaction commune de la commande appelante (connexion partagée) :
/// elle n'existe que si la commande est validée, et disparaît avec elle si elle échoue.
/// Appelée hors d'une transaction commune, elle lève une exception : une ligne d'audit ne doit jamais
/// survivre à une commande annulée (ni l'inverse).
/// </summary>
public interface IAuditLog
{
    /// <summary>Ajoute une ligne au journal (jamais modifiée ni supprimée, vérifié aussi en base). Renvoie son id.</summary>
    Task<Result<Guid>> RecordAsync(AuditRecord record, CancellationToken cancellationToken);
}
