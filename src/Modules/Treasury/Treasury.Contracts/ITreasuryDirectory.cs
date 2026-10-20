using Hawala.SharedKernel.Money;

namespace Treasury.Contracts;

/// <summary>
/// Seule porte d'entrée des autres modules vers les comptes de trésorerie (ex. les Transferts, pour les méthodes de paiement).
/// Respecte le périmètre du compte connecté : un compte de l'autre agence n'est pas renvoyé à un agent.
/// </summary>
public interface ITreasuryDirectory
{
    /// <summary>Les comptes demandés et visibles ; un id inconnu ou hors périmètre est simplement absent.</summary>
    Task<IReadOnlyList<TreasuryAccountRef>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

/// <param name="LedgerAccountId">Compte au grand livre qui porte l'argent de ce compte de trésorerie.</param>
public sealed record TreasuryAccountRef(
    Guid Id,
    string Name,
    Guid AgencyId,
    Currency Currency,
    Guid LedgerAccountId,
    bool IsActive);
