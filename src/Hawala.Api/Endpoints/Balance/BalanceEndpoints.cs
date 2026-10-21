using System.Security.Claims;
using Balance.Application.Features;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;

namespace Hawala.Api.Endpoints.Balance;

/// <summary>Bilan général (« الميزانية العامة ») : réservé au Superviseur.</summary>
public static class BalanceEndpoints
{
    /// <param name="Rate">Taux du bilan (MRO pour 5 000 FCFA) ; absent : prix d'achat du jour.</param>
    public sealed record SaveBalanceRequest(decimal? Rate);

    /// <param name="Amount">Capital initial en MRO.</param>
    /// <param name="Reason">Motif facultatif.</param>
    public sealed record SetCapitalRequest(decimal? Amount, string? Reason);

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

    /// <param name="rate">Taux du bilan ; absent : prix d'achat du jour (« Actualiser » avec un autre taux).</param>
    private static async Task<IResult> GetBalance(
        decimal? rate, ClaimsPrincipal user, IQueryHandler<GetBalanceQuery, BalanceView> handler, CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetBalanceQuery(actorId, rate), cancellationToken);
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

        var result = await handler.Handle(new SaveBalanceCommand(actorId, request?.Rate), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> ListSnapshots(
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

        var result = await handler.Handle(new ListBalanceSnapshotsQuery(actorId, from, to), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetCapital(
        ClaimsPrincipal user, IQueryHandler<GetCapitalQuery, CapitalView> handler, CancellationToken cancellationToken)
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

        var result = await handler.Handle(new SetInitialCapitalCommand(actorId, request.Amount, request.Reason), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }
}
