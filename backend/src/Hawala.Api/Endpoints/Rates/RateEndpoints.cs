using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;
using Rates.Application.Features;
using Rates.Application.Features.GetDailyRate;
using Rates.Application.Features.SetDailyRate;

namespace Hawala.Api.Endpoints.Rates;

/// <summary>Taux du jour au format « 5 000 FCFA = ? MRO » : prix d'achat (Superviseur) et prix de vente.</summary>
public static class RateEndpoints
{
    /// <param name="PurchaseRate">Prix d'achat (taux du marché), MRO pour 5 000 FCFA, au plus 2 décimales.</param>
    /// <param name="SaleRate">Prix de vente (appliqué au client), MRO pour 5 000 FCFA, au plus 2 décimales.</param>
    public sealed record SetDailyRateRequest(decimal? PurchaseRate, decimal? SaleRate);

    public static IEndpointRouteBuilder MapRateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rates").WithTags("Taux");

        // Lecture : tout compte connecté ; le prix d'achat seulement avec profit.view.
        group.MapGet("", ListDailyRates).RequireAuthorization();
        group.MapGet("/today", GetTodayRate).RequireAuthorization();
        group.MapGet("/{date}", GetDailyRate).RequireAuthorization();

        // Règle validée : seul le Superviseur fixe ou change un taux.
        group.MapPut("/{date}", SetDailyRate).RequirePermission(Permissions.RatesSetDaily);

        return app;
    }

    private static async Task<IResult> SetDailyRate(
        DateOnly date,
        SetDailyRateRequest request,
        ClaimsPrincipal user,
        ICommandHandler<SetDailyRateCommand> handler,
        IHubContext<LiveHub> hub,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(
            new SetDailyRateCommand(actorId, date, request.PurchaseRate, request.SaleRate), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        // Temps réel (règle validée) : tout le monde relit le taux, pour que le prix de vente affiché soit à jour.
        // Message léger : la date seulement (le prix d'achat reste lu par l'API avec profit.view).
        await hub.Clients.All.SendAsync(LiveHub.RatesChangedMessage, new { date }, cancellationToken);
        return TypedResults.NoContent();
    }

    private static Task<IResult> GetTodayRate(
        ClaimsPrincipal user, IQueryHandler<GetDailyRateQuery, DailyRateView> handler, CancellationToken cancellationToken) =>
        GetAsync(user, handler, date: null, cancellationToken);

    private static Task<IResult> GetDailyRate(
        DateOnly date, ClaimsPrincipal user, IQueryHandler<GetDailyRateQuery, DailyRateView> handler, CancellationToken cancellationToken) =>
        GetAsync(user, handler, date, cancellationToken);

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal user, IQueryHandler<GetDailyRateQuery, DailyRateView> handler, DateOnly? date, CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetDailyRateQuery(actorId, date), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> ListDailyRates(
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal user,
        IQueryHandler<ListDailyRatesQuery, IReadOnlyList<DailyRateView>> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new ListDailyRatesQuery(actorId, from, to), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
