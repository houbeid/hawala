using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;

namespace Ledger.Contracts;

/// <summary>
/// Seule porte d'entrée des autres modules vers le grand livre, seul module qui écrit des montants.
/// Les écritures sont enregistrées dans la connexion partagée de la requête, mais la transaction n'est
/// JAMAIS validée ici : c'est le décorateur de la commande appelante qui valide tout ou annule tout
/// (une commande = une transaction, même sur plusieurs modules).
/// </summary>
public interface ILedger
{
    /// <summary>Ouvre le compte d'une caisse ou d'un tiers. Les comptes système passent par <see cref="GetOrCreateSystemAccountAsync"/>.</summary>
    /// <param name="kind">Treasury ou Party.</param>
    /// <param name="ownerId">Id de la caisse ou du tiers dans son module.</param>
    /// <param name="agencyId">Agence du compte (id du module Organisation) ; elle fixe sa devise.</param>
    Task<Result<Guid>> OpenAccountAsync(
        LedgerAccountKind kind,
        Guid ownerId,
        string name,
        Currency currency,
        Guid agencyId,
        CancellationToken cancellationToken);

    /// <summary>Le compte ne reçoit plus d'écriture (ex. caisse désactivée). Son historique et son solde restent.</summary>
    Task<Result> CloseAccountAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Le compte reçoit de nouveau des écritures (ex. caisse réactivée).</summary>
    Task<Result> ReopenAccountAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Compte système d'une agence : le crée la première fois, le retrouve ensuite (idempotent).</summary>
    Task<Result<Guid>> GetOrCreateSystemAccountAsync(
        SystemAccountRole role,
        Guid agencyId,
        Currency currency,
        CancellationToken cancellationToken);

    /// <summary>Enregistre une écriture équilibrée par devise. Renvoie son id.</summary>
    Task<Result<Guid>> PostAsync(PostEntryRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Contre-passe une écriture : nouvelle écriture aux sens inversés, l'originale reste intacte.
    /// Règles validées : motif obligatoire ; une seule contre-passation par écriture ;
    /// une contre-passation ne peut pas être contre-passée.
    /// </summary>
    /// <param name="operationDate">Date de la contre-passation (calendrier de l'agence).</param>
    Task<Result<Guid>> ReverseAsync(
        Guid entryId,
        DateOnly operationDate,
        Guid authorId,
        string? reason,
        CancellationToken cancellationToken);

    /// <summary>Soldes (débit − crédit) des comptes demandés, jusqu'à une date d'opération incluse si elle est donnée.</summary>
    Task<Result<IReadOnlyList<AccountBalance>>> GetBalancesAsync(
        IReadOnlyCollection<Guid> accountIds,
        DateOnly? asOf,
        CancellationToken cancellationToken);
}
