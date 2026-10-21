using System.Security.Claims;
using Balance.Application.Features;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;

namespace Hawala.Api.Endpoints.Balance;

/// <summary>Bilan par caisse (deux caisses, deux capitaux, sans total converti) : réservé au Superviseur.</summary>
public static class BalanceEndpoints
{
    /// <param name="AgencyId">La caisse (l'agence) à enregistrer.</param>
    public sealed record SaveBalanceRequest(Guid? AgencyId);

    /// <param name="AgencyId">La caisse (l'agence).</param>
    /// <param name="Amount">Capital initial dans la devise de la caisse (MRO à Nouakchott, FCFA à Dakar).</param>
    /// <param name="Reason">Motif facultatif.</param>
    public sealed record SetCapitalRequest(Guid? AgencyId, decimal? Amount, string? Reason);

    public static IEndpointRouteBuilder MapBalanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/balance").WithTags("Bilan");

        group.MapGet("", GetBalance).RequirePermission(Permissions.BalanceView);
        group.MapGet("/snapshots", ListSnapshots).RequirePermission(Permissions.BalanceView);
        group.MapPost("/snapshots", SaveBalance).RequirePermission(Permissions.BalanceManage);
        group.MapGet("/capital", GetCapital).RequirePermission(Permissions.BalanceView);
        group.MapPut("/capital", SetCapital).RequirePermission(Permissions.BalanceManage);

        return app;
    }

    /// <summary>Les deux caisses, chacune dans sa devise.</summary>
    private static async Task<IResult> GetBalance(
        ClaimsPrincipal user, IQueryHandler<GetBalanceQuery, BalanceOverview> handler, CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetBalanceQuery(actorId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <summary>200 avec la photo du jour (créée, ou remplacée si elle existait déjà).</summary>
    private static async Task<IResult> SaveBalance(
        SaveBalanceRequest? request,
        ClaimsPrincipal user,
        ICommandHandler<SaveBalanceCommand, BalanceSnapshotView> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new SaveBalanceCommand(actorId, request?.AgencyId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> ListSnapshots(
        Guid? agencyId,
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal user,
        IQueryHandler<ListBalanceSnapshotsQuery, IReadOnlyList<BalanceSnapshotView>> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new ListBalanceSnapshotsQuery(actorId, agencyId, from, to), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetCapital(
        ClaimsPrincipal user, IQueryHandler<GetCapitalQuery, IReadOnlyList<CaisseCapitalView>> handler, CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetCapitalQuery(actorId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <summary>204 : nouvelle saisie du capital initial (les précédentes sont gardées).</summary>
    private static async Task<IResult> SetCapital(
        SetCapitalRequest request, ClaimsPrincipal user, ICommandHandler<SetInitialCapitalCommand> handler, CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(
            new SetInitialCapitalCommand(actorId, request.AgencyId, request.Amount, request.Reason), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }
}
