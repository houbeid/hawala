using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;
using Profit.Application.Features;

namespace Hawala.Api.Endpoints.Profit;

/// <summary>Écran « Bénéfice » : réservé au Superviseur (profit.view).</summary>
public static class ProfitEndpoints
{
    public static IEndpointRouteBuilder MapProfitEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/profit").WithTags("Bénéfice");

        group.MapGet("", GetProfitReport).RequirePermission(Permissions.ProfitView);
        group.MapGet("/days/{date}", GetProfitDay).RequirePermission(Permissions.ProfitView);

        // « Enregistrer le bénéfice du jour » et l'historique des photos.
        group.MapPost("/snapshots", SaveProfitSnapshot).RequirePermission(Permissions.ProfitView);
        group.MapGet("/snapshots", ListProfitSnapshots).RequirePermission(Permissions.ProfitView);

        return app;
    }

    /// <param name="agencyId">Agence d'envoi ; absente : les deux agences (bénéfice général).</param>
    /// <param name="from">Premier jour, aujourd'hui par défaut.</param>
    /// <param name="to">Dernier jour inclus, aujourd'hui par défaut.</param>
    private static async Task<IResult> GetProfitReport(
        Guid? agencyId,
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal user,
        IQueryHandler<GetProfitReportQuery, ProfitReport> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetProfitReportQuery(actorId, agencyId, from, to), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <param name="AgencyId">Agence d'envoi ; absente : les deux agences (bénéfice général).</param>
    public sealed record SaveProfitSnapshotRequest(Guid? AgencyId);

    /// <summary>200 avec la photo du jour (créée, ou remplacée si elle existait déjà).</summary>
    private static async Task<IResult> SaveProfitSnapshot(
        SaveProfitSnapshotRequest? request,
        ClaimsPrincipal user,
        ICommandHandler<SaveProfitSnapshotCommand, ProfitSnapshotView> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new SaveProfitSnapshotCommand(actorId, request?.AgencyId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <param name="from">Premier jour ; 30 derniers jours par défaut.</param>
    private static async Task<IResult> ListProfitSnapshots(
        Guid? agencyId,
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal user,
        IQueryHandler<ListProfitSnapshotsQuery, IReadOnlyList<ProfitSnapshotView>> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new ListProfitSnapshotsQuery(actorId, agencyId, from, to), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetProfitDay(
        DateOnly date,
        Guid? agencyId,
        ClaimsPrincipal user,
        IQueryHandler<GetProfitDayQuery, ProfitDayDetail> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetProfitDayQuery(actorId, agencyId, date), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
