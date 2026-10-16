using Identity.Domain.Accounts;

namespace Identity.Application.Abstractions;

/// <summary>Accès aux comptes. Les modifications sont enregistrées par <see cref="IUnitOfWork"/>.</summary>
public interface IUserAccountRepository
{
    Task<UserAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Cherche par email ou par téléphone selon le type de l'identifiant.</summary>
    Task<UserAccount?> GetByLoginAsync(LoginIdentifier login, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(Email email, CancellationToken cancellationToken);

    Task<bool> PhoneExistsAsync(PhoneNumber phone, CancellationToken cancellationToken);

    /// <summary>
    /// Nombre de Superviseurs actifs. Doit verrouiller ces lignes jusqu'à la fin de la transaction
    /// (SELECT … FOR UPDATE), pour que deux Superviseurs ne puissent pas se bloquer en même temps.
    /// </summary>
    Task<int> CountActiveSupervisorsForUpdateAsync(CancellationToken cancellationToken);

    void Add(UserAccount account);
}