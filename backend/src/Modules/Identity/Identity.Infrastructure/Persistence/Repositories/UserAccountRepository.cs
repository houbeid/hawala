using Identity.Application.Abstractions;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

internal sealed class UserAccountRepository(IdentityModuleDbContext db) : IUserAccountRepository
{
    public Task<UserAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<UserAccount>> ListAsync(UserAccountFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = db.Accounts.AsNoTracking();

        if (filter.AgencyId is { } agencyId)
        {
            query = query.Where(a => a.AgencyId == agencyId);
        }

        if (filter.Role is { } role)
        {
            query = query.Where(a => a.Role == role);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(a => a.Status == status);
        }

        var accounts = await query
            .OrderBy(a => a.FullName)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        // POINT OUVERT : la recherche texte est faite en mémoire, car l'email et le téléphone sont des
        // value objects convertis par EF (un LIKE sur ces colonnes ne se traduit pas proprement).
        // Suffisant pour quelques dizaines de comptes ; à passer en SQL avec la pagination si la liste grossit.
        return string.IsNullOrWhiteSpace(filter.Search)
            ? accounts
            : [.. accounts.Where(a => Matches(a, filter.Search))];
    }

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

    public Task<bool> EmailExistsAsync(Email email, Guid excludingAccountId, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(a => a.Email == email && a.Id != excludingAccountId, cancellationToken);

    public Task<bool> PhoneExistsAsync(PhoneNumber phone, Guid excludingAccountId, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(a => a.Phone == phone && a.Id != excludingAccountId, cancellationToken);

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

    /// <summary>
    /// Nom ou email : contient le texte, sans tenir compte de la casse.
    /// Téléphone : comparé sur les chiffres seuls, pour que « 77 123 45 » trouve « +221771234567 ».
    /// </summary>
    private static bool Matches(UserAccount account, string search)
    {
        var text = search.Trim();
        if (account.FullName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || account.Email.Value.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var digits = new string([.. text.Where(char.IsAsciiDigit)]);
        return digits.Length > 0
            && account.Phone is not null
            && account.Phone.Value.Contains(digits, StringComparison.Ordinal);
    }
}