using System.Security.Claims;
using Balance.Application.Features;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;

namespace Hawala.Api.Endpoints.Balance;

/// <summary>
/// Bilan par caisse (deux caisses, deux capitaux) et bilan général (les deux caisses en MRO au taux du bilan, capital général) :
/// réservés au Superviseur.
/// </summary>
public static class BalanceEndpoints
{
    /// <param name="AgencyId">La caisse (l'agence) à enregistrer.</param>
    public sealed record SaveBalanceRequest(Guid? AgencyId);

    /// <param name="AgencyId">La caisse (l'agence).</param>
    /// <param name="Amount">Capital initial dans la devise de la caisse (MRO à Nouakchott, FCFA à Dakar).</param>
    /// <param name="Reason">Motif facultatif.</param>
    public sealed record SetCapitalRequest(Guid? AgencyId, decimal? Amount, string? Reason);

    /// <param name="Rate">Taux du bilan : MRO pour 5 000 FCFA (2 décimales au plus).</param>
    public sealed record SaveGeneralBalanceRequest(decimal? Rate);

    /// <param name="Amount">Capital général, en MRO.</param>
    /// <param name="Reason">Motif facultatif.</param>
    public sealed record SetGeneralCapitalRequest(decimal? Amount, string? Reason);

    public static IEndpointRouteBuilder MapBalanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/balance").WithTags("Bilan");

        group.MapGet("", GetBalance).RequirePermission(Permissions.BalanceView);
        group.MapGet("/snapshots", ListSnapshots).RequirePermission(Permissions.BalanceView);
        group.MapPost("/snapshots", SaveBalance).RequirePermission(Permissions.BalanceManage);
        group.MapGet("/capital", GetCapital).RequirePermission(Permissions.BalanceView);
        group.MapPut("/capital", SetCapital).RequirePermission(Permissions.BalanceManage);

        group.MapGet("/general", GetGeneralBalance).RequirePermission(Permissions.BalanceView);
        group.MapGet("/general/snapshots", ListGeneralSnapshots).RequirePermission(Permissions.BalanceView);
        group.MapPost("/general/snapshots", SaveGeneralBalance).RequirePermission(Permissions.BalanceManage);
        group.MapGet("/general/capital", GetGeneralCapital).RequirePermission(Permissions.BalanceView);
        group.MapPut("/general/capital", SetGeneralCapital).RequirePermission(Permissions.BalanceManage);

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

    /// <summary>Bilan général en direct ; « rate » (taux du bilan) obligatoire.</summary>
    private static async Task<IResult> GetGeneralBalance(
        decimal? rate,
        ClaimsPrincipal user,
        IQueryHandler<GetGeneralBalanceQuery, GeneralBalanceView> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetGeneralBalanceQuery(actorId, rate), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <summary>200 avec la photo du jour (créée, ou remplacée si elle existait déjà).</summary>
    private static async Task<IResult> SaveGeneralBalance(
        SaveGeneralBalanceRequest? request,
        ClaimsPrincipal user,
        ICommandHandler<SaveGeneralBalanceCommand, GeneralBalanceSnapshotView> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new SaveGeneralBalanceCommand(actorId, request?.Rate), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> ListGeneralSnapshots(
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal user,
        IQueryHandler<ListGeneralBalanceSnapshotsQuery, IReadOnlyList<GeneralBalanceSnapshotView>> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new ListGeneralBalanceSnapshotsQuery(actorId, from, to), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetGeneralCapital(
        ClaimsPrincipal user, IQueryHandler<GetGeneralCapitalQuery, GeneralCapitalView> handler, CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetGeneralCapitalQuery(actorId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <summary>204 : nouvelle saisie du capital général (les précédentes sont gardées).</summary>
    private static async Task<IResult> SetGeneralCapital(
        SetGeneralCapitalRequest request, ClaimsPrincipal user, ICommandHandler<SetGeneralCapitalCommand> handler, CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new SetGeneralCapitalCommand(actorId, request.Amount, request.Reason), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }
}
