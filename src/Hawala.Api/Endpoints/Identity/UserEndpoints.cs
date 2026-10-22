using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Application.Features.Users;
using Identity.Application.Features.Users.BlockUser;
using Identity.Application.Features.Users.ChangeUserAgency;
using Identity.Application.Features.Users.ChangeUserRole;
using Identity.Application.Features.Users.CreateUser;
using Identity.Application.Features.Users.GetUser;
using Hawala.Api.Realtime;
using Identity.Application.Features.Sessions;
using Identity.Application.Features.Users.Limits;
using Identity.Application.Features.Users.ListUsers;
using Identity.Application.Features.Users.PermissionOverrides;
using Identity.Application.Features.Users.ResetUserPassword;
using Identity.Application.Features.Users.UnblockUser;
using Identity.Application.Features.Users.UpdateUserProfile;
using Identity.Contracts;

namespace Hawala.Api.Endpoints.Identity;

/// <summary>
/// Gestion des comptes (permission users.manage). Le Superviseur gère tout ; un autre rôle qui a reçu users.manage
/// ne gère que les comptes de son agence, sans rôle, agence ni permissions (contrôlé dans les handlers, à partir du JWT).
/// </summary>
public static class UserEndpoints
{
    public sealed record CreateUserRequest(string? Email, string? Phone, string? FullName, string? Role, Guid? AgencyId);

    /// <param name="AgencyId">Obligatoire pour un agent, absent pour un Superviseur.</param>
    public sealed record ChangeUserRoleRequest(string? Role, Guid? AgencyId);

    public sealed record ChangeUserAgencyRequest(Guid? AgencyId);

    /// <param name="Phone">Vide ou absent = téléphone retiré : toujours renvoyer le téléphone actuel pour le garder.</param>
    public sealed record UpdateUserProfileRequest(string? Email, string? Phone, string? FullName);

    /// <param name="Permissions">Liste complète des permissions voulues (cases cochées) ; vide = aucune.</param>
    public sealed record SetUserPermissionsRequest(IReadOnlyList<string?>? Permissions);

    /// <summary>Plafonds dans la devise de l'agence du compte ; absent ou null = pas de limite (état complet).</summary>
    public sealed record SetUserLimitsRequest(decimal? PerTransfer, decimal? PerDay, decimal? CreditPerDay);

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users")
            .WithTags("Comptes")
            .RequirePermission(Permissions.UsersManage);

        group.MapGet("", ListUsers);
        group.MapGet("/{id:guid}", GetUser);
        group.MapPost("", CreateUser);
        group.MapPut("/{id:guid}", UpdateUserProfile);
        group.MapPost("/{id:guid}/block", BlockUser);
        group.MapPost("/{id:guid}/unblock", UnblockUser);
        group.MapPost("/{id:guid}/change-role", ChangeUserRole);
        group.MapPost("/{id:guid}/change-agency", ChangeUserAgency);
        group.MapPost("/{id:guid}/reset-password", ResetUserPassword);
        group.MapGet("/{id:guid}/permissions", GetUserPermissions);
        group.MapPut("/{id:guid}/permissions", SetUserPermissions);
        group.MapGet("/{id:guid}/limits", GetUserLimits);
        group.MapGet("/{id:guid}/sessions", GetUserSessions);
        group.MapPost("/{id:guid}/sessions/{sessionId:guid}/revoke", CloseUserSession);
        group.MapPut("/{id:guid}/limits", SetUserLimits);

        return app;
    }

    private static async Task<IResult> GetUserSessions(
        Guid id,
        ClaimsPrincipal user,
        IQueryHandler<ListSessionsQuery, IReadOnlyList<SessionView>> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new ListSessionsQuery(actorId, id, user.GetSessionId()), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> CloseUserSession(
        Guid id,
        Guid sessionId,
        ClaimsPrincipal user,
        ICommandHandler<CloseSessionCommand> handler,
        SessionNotifications notifications,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new CloseSessionCommand(actorId, id, sessionId), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        await notifications.SessionClosedAsync(sessionId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetUserLimits(
        Guid id,
        ClaimsPrincipal user,
        IQueryHandler<GetUserLimitsQuery, UserLimits> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetUserLimitsQuery(actorId, id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> SetUserLimits(
        Guid id,
        SetUserLimitsRequest request,
        ClaimsPrincipal user,
        ICommandHandler<SetUserLimitsCommand, UserLimits> handler,
        CancellationToken cancellationToken)
    {
        // L'auteur vient uniquement du JWT : le handler vérifie qu'il est Superviseur.
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var command = new SetUserLimitsCommand(actorId, id, request.PerTransfer, request.PerDay, request.CreditPerDay);
        var result = await handler.Handle(command, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetUserPermissions(
        Guid id,
        ClaimsPrincipal user,
        IQueryHandler<GetUserPermissionsQuery, UserPermissions> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetUserPermissionsQuery(actorId, id), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> SetUserPermissions(
        Guid id,
        SetUserPermissionsRequest request,
        ClaimsPrincipal user,
        ICommandHandler<SetUserPermissionsCommand, UserPermissions> handler,
        CancellationToken cancellationToken)
    {
        // L'auteur vient uniquement du JWT : le handler vérifie qu'il est Superviseur (users.manage ne suffit pas).
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var command = new SetUserPermissionsCommand(actorId, id, request.Permissions);
        var result = await handler.Handle(command, cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> ResetUserPassword(
        Guid id,
        ClaimsPrincipal user,
        ICommandHandler<ResetUserPasswordCommand, ResetPassword> handler,
        SessionNotifications notifications,
        CancellationToken cancellationToken)
    {
        // L'auteur vient uniquement du JWT : il ne peut pas réinitialiser son propre mot de passe.
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new ResetUserPasswordCommand(actorId, id), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        await notifications.SignedOutAsync(id, cancellationToken);

        // Le nouveau mot de passe temporaire n'est renvoyé qu'une fois, dans cette réponse.
        return TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> UpdateUserProfile(
        Guid id,
        UpdateUserProfileRequest request,
        ClaimsPrincipal user,
        ICommandHandler<UpdateUserProfileCommand> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var command = new UpdateUserProfileCommand(actorId, id, request.Email, request.Phone, request.FullName);
        var result = await handler.Handle(command, cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> ChangeUserAgency(
        Guid id,
        ChangeUserAgencyRequest request,
        ClaimsPrincipal user,
        ICommandHandler<ChangeUserAgencyCommand> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new ChangeUserAgencyCommand(actorId, id, request.AgencyId), cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> ChangeUserRole(
        Guid id,
        ChangeUserRoleRequest request,
        ClaimsPrincipal user,
        ICommandHandler<ChangeUserRoleCommand> handler,
        CancellationToken cancellationToken)
    {
        // L'auteur vient uniquement du JWT : il ne peut pas changer son propre rôle.
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var command = new ChangeUserRoleCommand(actorId, id, request.Role, request.AgencyId);
        var result = await handler.Handle(command, cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> BlockUser(
        Guid id,
        ClaimsPrincipal user,
        ICommandHandler<BlockUserCommand> handler,
        SessionNotifications notifications,
        CancellationToken cancellationToken)
    {
        // L'auteur vient uniquement du JWT : il ne peut pas se bloquer lui-même.
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new BlockUserCommand(actorId, id), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        await notifications.SignedOutAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> UnblockUser(
        Guid id,
        ClaimsPrincipal user,
        ICommandHandler<UnblockUserCommand, UnblockedUser> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new UnblockUserCommand(actorId, id), cancellationToken);

        // Le nouveau mot de passe temporaire n'est renvoyé qu'une fois, dans cette réponse.
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> ListUsers(
        Guid? agencyId,
        string? role,
        string? status,
        string? search,
        ClaimsPrincipal user,
        IQueryHandler<ListUsersQuery, IReadOnlyList<UserSummary>> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new ListUsersQuery(actorId, agencyId, role, status, search), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetUser(
        Guid id,
        ClaimsPrincipal user,
        IQueryHandler<GetUserQuery, UserSummary> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetUserQuery(actorId, id), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request,
        ClaimsPrincipal user,
        ICommandHandler<CreateUserCommand, CreatedUser> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var command = new CreateUserCommand(actorId, request.Email, request.Phone, request.FullName, request.Role, request.AgencyId);
        var result = await handler.Handle(command, cancellationToken);

        // Le mot de passe temporaire n'est renvoyé qu'une fois, dans cette réponse.
        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/users/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
