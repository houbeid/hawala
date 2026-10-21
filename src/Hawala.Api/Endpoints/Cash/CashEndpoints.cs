using System.Security.Claims;
using Cash.Application.Features.Movements;
using Cash.Application.Features.Movements.CreateCashMovement;
using Cash.Application.Features.Movements.GetCashMovement;
using Cash.Application.Features.Movements.ListCashMovements;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Hawala.Api.Endpoints.Cash;

/// <summary>Caisse : mouvements manuels (encaissement, décaissement, approvisionnement, retrait du patron).</summary>
public static class CashEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <param name="AgencyId">Agence : facultative pour un agent (la sienne), obligatoire pour le Superviseur.</param>
    /// <param name="Type">Receipt (encaissement), Payment (décaissement), OwnerContribution, OwnerWithdrawal.</param>
    /// <param name="TreasuryAccountId">Méthode de paiement (compte de trésorerie de l'agence).</param>
    /// <param name="PartyId">Tiers (« Source ») : obligatoire pour Receipt / Payment, absent sinon.</param>
    /// <param name="Unit">MRO (défaut) ou MRU (converti × 10).</param>
    /// <param name="OperationAt">Date et heure ; maintenant si absente. Jamais dans le futur.</param>
    public sealed record CreateCashMovementRequest(
        Guid? AgencyId,
        string? Type,
        Guid? TreasuryAccountId,
        Guid? PartyId,
        decimal? Amount,
        string? Unit,
        string? Note,
        DateTimeOffset? OperationAt);

    public static IEndpointRouteBuilder MapCashEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cash-movements").WithTags("Caisse");

        // Lecture des mouvements : comme les soldes de caisse, avec cash.budget.read.
        group.MapGet("", ListCashMovements).RequirePermission(Permissions.CashBudgetRead);
        group.MapGet("/{id:guid}", GetCashMovement).RequirePermission(Permissions.CashBudgetRead);

        group.MapPost("", CreateCashMovement).RequirePermission(Permissions.CashCreate);

        return app;
    }

    /// <summary>201. La même Idempotency-Key renvoie 200 et le mouvement déjà enregistré.</summary>
    private static async Task<IResult> CreateCashMovement(
        CreateCashMovementRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        ClaimsPrincipal user,
        ICommandHandler<CreateCashMovementCommand, CreatedCashMovement> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(
            new CreateCashMovementCommand(
                actorId, idempotencyKey, request.AgencyId, request.Type, request.TreasuryAccountId, request.PartyId,
                request.Amount, request.Unit, request.Note, request.OperationAt),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        return result.Value.Replayed
            ? TypedResults.Ok(result.Value)
            : TypedResults.Created($"/api/v1/cash-movements/{result.Value.Id}", result.Value);
    }

    /// <param name="from">Premier jour (aaaa-mm-jj), aujourd'hui par défaut.</param>
    /// <param name="to">Dernier jour inclus, aujourd'hui par défaut.</param>
    /// <param name="page">À partir de 1 ; 50 mouvements par page.</param>
    private static async Task<IResult> ListCashMovements(
        Guid? agencyId,
        DateOnly? from,
        DateOnly? to,
        string? type,
        Guid? treasuryAccountId,
        string? search,
        int? page,
        ClaimsPrincipal user,
        IQueryHandler<ListCashMovementsQuery, CashMovementList> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(
            new ListCashMovementsQuery(actorId, agencyId, from, to, type, treasuryAccountId, search, page), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetCashMovement(
        Guid id,
        ClaimsPrincipal user,
        IQueryHandler<GetCashMovementQuery, CashMovementView> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetCashMovementQuery(actorId, id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
