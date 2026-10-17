using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Application.Abstractions;
using Identity.Domain;

namespace Identity.Application.Features.Accounts.GetCurrentAccount;

public sealed class GetCurrentAccountQueryHandler(IUserAccountRepository accounts)
    : IQueryHandler<GetCurrentAccountQuery, CurrentAccount>
{
    public async Task<Result<CurrentAccount>> Handle(GetCurrentAccountQuery query, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return IdentityErrors.Account.NotFound;
        }

        return new CurrentAccount(
            account.Id,
            account.Email.Value,
            account.Phone?.Value,
            account.FullName,
            account.Role.ToString(),
            account.MustChangePassword);
    }
}