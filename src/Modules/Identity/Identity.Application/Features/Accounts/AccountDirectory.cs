using Identity.Application.Abstractions;
using Identity.Contracts;
using Identity.Domain.Roles;

namespace Identity.Application.Features.Accounts;

/// <summary>Implémentation de la porte publique de lecture des comptes.</summary>
public sealed class AccountDirectory(IUserAccountRepository accounts) : IAccountDirectory
{
    public async Task<AccountInfo?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(accountId, cancellationToken);

        return account is null
            ? null
            : new AccountInfo(
                account.Id,
                account.FullName,
                account.Role == RoleTemplate.Supervisor,
                account.AgencyId,
                new AccountLimitsInfo(account.LimitPerTransfer, account.LimitPerDay, account.CreditLimitPerDay));
    }
}
