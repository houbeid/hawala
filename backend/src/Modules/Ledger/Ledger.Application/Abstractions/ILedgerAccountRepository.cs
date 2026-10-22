using Ledger.Contracts;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Abstractions;

/// <summary>Accès aux comptes du grand livre. Enregistrement par <see cref="IUnitOfWork"/>.</summary>
public interface ILedgerAccountRepository
{
    /// <summary>Comptes demandés (suivis), en une seule lecture. Un id inconnu est simplement absent du résultat.</summary>
    Task<IReadOnlyList<LedgerAccount>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Le compte système d'un rôle pour une agence, ou null s'il n'a pas encore été créé.</summary>
    Task<LedgerAccount?> GetSystemAccountAsync(SystemAccountRole role, Guid agencyId, CancellationToken cancellationToken);

    void Add(LedgerAccount account);
}
