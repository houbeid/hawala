using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;
using Organization.Application.Agencies.ChangeAgencyColor;
using Organization.Application.Agencies.GetAgencies;
using Organization.Contracts;

namespace Hawala.Api.Endpoints.Organization;

public static class AgencyEndpoints
{
    /// <param name="Color">Format #RRGGBB.</param>
    public sealed record ChangeAgencyColorRequest(string? Color);

    public static IEndpointRouteBuilder MapAgencyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/agencies").WithTags("Organisation");

        // Politique standard : connexion obligatoire, mot de passe temporaire refusé.
        // Tout compte connecté lit les agences (badge et couleur de son interface).
        group.MapGet("", GetAgencies).RequireAuthorization();

        // Permission sur la route seule, pas sur le groupe : la lecture ci-dessus reste ouverte aux agents.
        group.MapPost("/{id:guid}/change-color", ChangeAgencyColor)
            .RequirePermission(Permissions.AgenciesManage);

        return app;
    }

    private static async Task<IResult> GetAgencies(
        IQueryHandler<GetAgenciesQuery, IReadOnlyList<AgencySummary>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new GetAgenciesQuery(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> ChangeAgencyColor(
        Guid id,
        ChangeAgencyColorRequest request,
        ClaimsPrincipal user,
        ICommandHandler<ChangeAgencyColorCommand> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new ChangeAgencyColorCommand(actorId, id, request.Color), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }
}
