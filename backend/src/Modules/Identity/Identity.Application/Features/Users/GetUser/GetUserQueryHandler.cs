using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain;
using Organization.Contracts;

namespace Identity.Application.Features.Users.GetUser;

public sealed class GetUserQueryHandler(
    IUserAccountRepository accounts,
    ICredentialStore credentials,
    IAgencyDirectory agencies,
    IClock clock)
    : IQueryHandler<GetUserQuery, UserSummary>
{
    public async Task<Result<UserSummary>> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        var manager = await AccountManager.LoadAsync(accounts, query.ActorId, cancellationToken);
        var account = await accounts.GetByIdAsync(query.UserId, cancellationToken);
        // Hors de son périmètre, le compte n'existe pas pour lui (même réponse qu'un id inconnu).
        if (account is null || !manager.CanManage(account))
        {
            return IdentityErrors.Account.NotFound;
        }

        var agencyById = new Dictionary<Guid, AgencySummary>();
        if (account.AgencyId is { } agencyId
            && await agencies.GetByIdAsync(agencyId, cancellationToken) is { } agency)
        {
            agencyById[agency.Id] = agency;
        }

        var lockoutEnds = await credentials.GetLockoutEndsAsync([account.Id], clock.UtcNow, cancellationToken);

        return UserSummaryMapper.ToSummary(account, agencyById, lockoutEnds);
    }
}
