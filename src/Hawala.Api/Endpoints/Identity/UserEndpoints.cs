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
using Identity.Application.Features.Users.ListUsers;
using Identity.Application.Features.Users.ResetUserPassword;
using Identity.Application.Features.Users.UnblockUser;
using Identity.Application.Features.Users.UpdateUserProfile;
using Identity.Contracts;

namespace Hawala.Api.Endpoints.Identity;

/// <summary>Gestion des comptes par le Superviseur (permission users.manage).</summary>
public static class UserEndpoints
{
    public sealed record CreateUserRequest(string? Email, string? Phone, string? FullName, string? Role, Guid? AgencyId);

    /// <param name="AgencyId">Obligatoire pour un agent, absent pour un Superviseur.</param>
    public sealed record ChangeUserRoleRequest(string? Role, Guid? AgencyId);

    public sealed record ChangeUserAgencyRequest(Guid? AgencyId);

    /// <param name="Phone">Vide ou absent = téléphone retiré : toujours renvoyer le téléphone actuel pour le garder.</param>
    public sealed record UpdateUserProfileRequest(string? Email, string? Phone, string? FullName);

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

        return app;
    }

    private static async Task<IResult> ResetUserPassword(
        Guid id,
        ClaimsPrincipal user,
        ICommandHandler<ResetUserPasswordCommand, ResetPassword> handler,
        CancellationToken cancellationToken)
    {
        // L'auteur vient uniquement du JWT : il ne peut pas réinitialiser son propre mot de passe.
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new ResetUserPasswordCommand(actorId, id), cancellationToken);

        // Le nouveau mot de passe temporaire n'est renvoyé qu'une fois, dans cette réponse.
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> UpdateUserProfile(
        Guid id,
        UpdateUserProfileRequest request,
        ICommandHandler<UpdateUserProfileCommand> handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateUserProfileCommand(id, request.Email, request.Phone, request.FullName);
        var result = await handler.Handle(command, cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> ChangeUserAgency(
        Guid id,
        ChangeUserAgencyRequest request,
        ICommandHandler<ChangeUserAgencyCommand> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new ChangeUserAgencyCommand(id, request.AgencyId), cancellationToken);

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
        CancellationToken cancellationToken)
    {
        // L'auteur vient uniquement du JWT : il ne peut pas se bloquer lui-même.
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new BlockUserCommand(actorId, id), cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> UnblockUser(
        Guid id,
        ICommandHandler<UnblockUserCommand, UnblockedUser> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new UnblockUserCommand(id), cancellationToken);

        // Le nouveau mot de passe temporaire n'est renvoyé qu'une fois, dans cette réponse.
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> ListUsers(
        Guid? agencyId,
        string? role,
        string? status,
        string? search,
        IQueryHandler<ListUsersQuery, IReadOnlyList<UserSummary>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new ListUsersQuery(agencyId, role, status, search), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetUser(
        Guid id,
        IQueryHandler<GetUserQuery, UserSummary> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new GetUserQuery(id), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request,
        ICommandHandler<CreateUserCommand, CreatedUser> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateUserCommand(request.Email, request.Phone, request.FullName, request.Role, request.AgencyId);
        var result = await handler.Handle(command, cancellationToken);

        // Le mot de passe temporaire n'est renvoyé qu'une fois, dans cette réponse.
        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/users/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
