using Identity.Application.Abstractions;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

internal sealed class UserAccountRepository(IdentityModuleDbContext db) : IUserAccountRepository
{
    public Task<UserAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<UserAccount?> GetByLoginAsync(LoginIdentifier login, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(login);

        // La valeur est déjà normalisée par LoginIdentifier : la recréer ne peut pas échouer.
        if (login.Kind == LoginIdentifierKind.Email)
        {
            var email = Email.Create(login.Value).Value;
            return db.Accounts.FirstOrDefaultAsync(a => a.Email == email, cancellationToken);
        }

        var phone = PhoneNumber.Create(login.Value).Value;
        return db.Accounts.FirstOrDefaultAsync(a => a.Phone == phone, cancellationToken);
    }

    public Task<bool> EmailExistsAsync(Email email, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(a => a.Email == email, cancellationToken);

    public Task<bool> PhoneExistsAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(a => a.Phone == phone, cancellationToken);

    public async Task<int> CountActiveSupervisorsForUpdateAsync(CancellationToken cancellationToken)
    {
        // PostgreSQL interdit FOR UPDATE avec COUNT : on verrouille les lignes, puis on compte côté .NET.
        // Le verrou dure jusqu'à la fin de la transaction en cours.
        var ids = await db.Database
            .SqlQuery<Guid>($"""
                SELECT "Id" AS "Value"
                FROM identity.accounts
                WHERE "Role" = {nameof(RoleTemplate.Supervisor)}
                  AND "Status" = {nameof(AccountStatus.Active)}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return ids.Count;
    }

    public void Add(UserAccount account) => db.Accounts.Add(account);
}