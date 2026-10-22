using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Application.Abstractions;
using Identity.Domain;
using Organization.Contracts;

namespace Identity.Application.Features.Accounts.GetCurrentAccount;

public sealed class GetCurrentAccountQueryHandler(
    IUserAccountRepository accounts,
    IAgencyDirectory agencies)
    : IQueryHandler<GetCurrentAccountQuery, CurrentAccount>
{
    public async Task<Result<CurrentAccount>> Handle(GetCurrentAccountQuery query, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return IdentityErrors.Account.NotFound;
        }

        CurrentAccountAgency? agency = null;
        if (account.AgencyId is { } agencyId)
        {
            // Lu via les Contracts d'Organisation : Identité ne voit jamais la base de l'autre module.
            var summary = await agencies.GetByIdAsync(agencyId, cancellationToken);
            if (summary is not null)
            {
                agency = new CurrentAccountAgency(summary.Id, summary.Code, summary.Name, summary.Color);
            }
        }

        return new CurrentAccount(
            account.Id,
            account.Email.Value,
            account.Phone?.Value,
            account.FullName,
            account.Role.ToString(),
            account.MustChangePassword,
            agency,
            [.. account.EffectivePermissions.Order(StringComparer.Ordinal)]);
    }
}