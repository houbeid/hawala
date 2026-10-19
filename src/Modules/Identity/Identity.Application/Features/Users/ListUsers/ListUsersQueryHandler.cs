using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;
using Organization.Contracts;

namespace Identity.Application.Features.Users.ListUsers;

/// <summary>
/// Trois lectures quelle que soit la taille de la liste : les comptes, les agences, les verrouillages.
/// </summary>
public sealed class ListUsersQueryHandler(
    IUserAccountRepository accounts,
    ICredentialStore credentials,
    IAgencyDirectory agencies,
    IClock clock)
    : IQueryHandler<ListUsersQuery, IReadOnlyList<UserSummary>>
{
    public async Task<Result<IReadOnlyList<UserSummary>>> Handle(ListUsersQuery query, CancellationToken cancellationToken)
    {
        RoleTemplate? role = null;
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            if (!EnumNames.TryParse<RoleTemplate>(query.Role, out var parsedRole))
            {
                return IdentityErrors.Account.RoleInvalid;
            }

            role = parsedRole;
        }

        AccountStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!EnumNames.TryParse<AccountStatus>(query.Status, out var parsedStatus))
            {
                return IdentityErrors.Account.StatusInvalid;
            }

            status = parsedStatus;
        }

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var filter = new UserAccountFilter(query.AgencyId, role, status, search);

        var found = await accounts.ListAsync(filter, cancellationToken);
        if (found.Count == 0)
        {
            return Result.Success<IReadOnlyList<UserSummary>>([]);
        }

        var agencyById = (await agencies.GetAllAsync(cancellationToken)).ToDictionary(a => a.Id);
        var lockoutEnds = await credentials.GetLockoutEndsAsync(
            [.. found.Select(a => a.Id)], clock.UtcNow, cancellationToken);

        return Result.Success<IReadOnlyList<UserSummary>>(
            [.. found.Select(a => UserSummaryMapper.ToSummary(a, agencyById, lockoutEnds))]);
    }
}
