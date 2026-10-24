using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;
using Ledger.Application.StartDate;

namespace Hawala.Api.Endpoints.Setup;

/// <summary>
/// Date de démarrage (phase 8, décision du 24 octobre 2026) : lue par tout compte connecté ; fixée une seule fois par le
/// Superviseur (balance.manage). Ensuite, le grand livre refuse toute opération datée avant (Ledger.Entry.BeforeStartDate).
/// </summary>
public static class StartDateEndpoints
{
    /// <param name="StartDate">Jour de mise en service (aaaa-mm-jj), jamais futur.</param>
    public sealed record SetStartDateRequest(DateOnly? StartDate);

    public static IEndpointRouteBuilder MapStartDateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/setup/start-date").WithTags("Mise en service");
        group.MapGet("", GetStartDate).RequireAuthorization();
        group.MapPut("", SetStartDate).RequirePermission(Permissions.BalanceManage);
        return app;
    }

    private static async Task<IResult> GetStartDate(IQueryHandler<GetStartDateQuery, StartDateView> handler, CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new GetStartDateQuery(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <summary>200 avec la date fixée. Déjà fixée : 409 ; écritures datées avant : 409 (count) ; future : 400.</summary>
    private static async Task<IResult> SetStartDate(
        SetStartDateRequest request,
        ClaimsPrincipal user,
        ICommandHandler<SetStartDateCommand, StartDateView> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new SetStartDateCommand(actorId, request.StartDate), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
