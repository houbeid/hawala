using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;
using Parties.Application.Features.Scopes;

namespace Hawala.Api.Endpoints.Parties;

/// <summary>
/// « Limiter les tiers » d'un compte (phase 6) : la liste des tiers qu'il voit et utilise.
/// Route rangée avec les comptes (users.manage), mais seul un compte de rôle Superviseur l'utilise (vérifié par le handler).
/// </summary>
public static class AccountPartyScopeEndpoints
{
    /// <param name="Limited">Obligatoire : true = le compte ne voit que les tiers listés ; false = tous (la liste est vidée).</param>
    /// <param name="PartyIds">Tiers autorisés (état complet), ignorés si le compte n'est pas limité.</param>
    public sealed record SetAccountPartyScopeRequest(bool? Limited, IReadOnlyList<Guid>? PartyIds);

    public static IEndpointRouteBuilder MapAccountPartyScopeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users/{id:guid}/party-scope")
            .WithTags("Comptes")
            .RequirePermission(Permissions.UsersManage);

        group.MapGet("", GetScope);
        group.MapPut("", SetScope);

        return app;
    }

    private static async Task<IResult> GetScope(
        Guid id,
        ClaimsPrincipal user,
        IQueryHandler<GetAccountPartyScopeQuery, AccountPartyScopeView> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetAccountPartyScopeQuery(actorId, id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> SetScope(
        Guid id,
        SetAccountPartyScopeRequest request,
        ClaimsPrincipal user,
        ICommandHandler<SetAccountPartyScopeCommand, AccountPartyScopeView> handler,
        CancellationToken cancellationToken)
    {
        // L'auteur vient uniquement du JWT : le handler vérifie qu'il est Superviseur.
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var command = new SetAccountPartyScopeCommand(actorId, id, request.Limited, request.PartyIds);
        var result = await handler.Handle(command, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
