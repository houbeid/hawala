using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Application.Abstractions;
using Identity.Domain;

namespace Identity.Application.Features.Users.PermissionOverrides;

/// <summary>Permissions d'un compte, pour l'écran des cases à cocher du Superviseur.</summary>
/// <param name="ActorId">Compte connecté, lu par l'API dans le JWT : Superviseur, ou compte de la même agence avec users.manage.</param>
public sealed record GetUserPermissionsQuery(Guid ActorId, Guid UserId) : IQuery<UserPermissions>;

public sealed class GetUserPermissionsQueryHandler(IUserAccountRepository accounts)
    : IQueryHandler<GetUserPermissionsQuery, UserPermissions>
{
    public async Task<Result<UserPermissions>> Handle(GetUserPermissionsQuery query, CancellationToken cancellationToken)
    {
        var manager = await AccountManager.LoadAsync(accounts, query.ActorId, cancellationToken);
        var account = await accounts.GetByIdAsync(query.UserId, cancellationToken);

        return account is null || !manager.CanManage(account)
            ? IdentityErrors.Account.NotFound
            : UserPermissionsMapper.ToView(account);
    }
}
