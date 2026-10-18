using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Organization.Application.Agencies.GetAgencies;
using Organization.Contracts;

namespace Hawala.Api.Endpoints.Organization;

public static class AgencyEndpoints
{
    public static IEndpointRouteBuilder MapAgencyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/agencies").WithTags("Organisation");

        // Politique standard : connexion obligatoire, mot de passe temporaire refusé.
        group.MapGet("", GetAgencies).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetAgencies(
        IQueryHandler<GetAgenciesQuery, IReadOnlyList<AgencySummary>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new GetAgenciesQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}