using Identity.Domain.Accounts;

namespace Identity.Application.Abstractions;

/// <summary>Accès aux comptes. Les modifications sont enregistrées par <see cref="IUnitOfWork"/>.</summary>
public interface IUserAccountRepository
{
    Task<UserAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Comptes qui correspondent aux filtres, triés par nom. Lecture seule (non suivis).
    /// POINT OUVERT : pas de pagination, deux agences et quelques dizaines de comptes au plus.
    /// À ajouter si la liste dépasse quelques centaines de comptes.
    /// </summary>
    Task<IReadOnlyList<UserAccount>> ListAsync(UserAccountFilter filter, CancellationToken cancellationToken);

    /// <summary>Cherche par email ou par téléphone selon le type de l'identifiant.</summary>
    Task<UserAccount?> GetByLoginAsync(LoginIdentifier login, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(Email email, CancellationToken cancellationToken);

    Task<bool> PhoneExistsAsync(PhoneNumber phone, CancellationToken cancellationToken);

    /// <summary>L'email est-il utilisé par un autre compte que <paramref name="excludingAccountId"/> ? (modification du profil)</summary>
    Task<bool> EmailExistsAsync(Email email, Guid excludingAccountId, CancellationToken cancellationToken);

    /// <summary>Le téléphone est-il utilisé par un autre compte que <paramref name="excludingAccountId"/> ? (modification du profil)</summary>
    Task<bool> PhoneExistsAsync(PhoneNumber phone, Guid excludingAccountId, CancellationToken cancellationToken);

    /// <summary>
    /// Nombre de Superviseurs actifs. Doit verrouiller ces lignes jusqu'à la fin de la transaction
    /// (SELECT … FOR UPDATE), pour que deux Superviseurs ne puissent pas se bloquer en même temps.
    /// </summary>
    Task<int> CountActiveSupervisorsForUpdateAsync(CancellationToken cancellationToken);

    void Add(UserAccount account);
}