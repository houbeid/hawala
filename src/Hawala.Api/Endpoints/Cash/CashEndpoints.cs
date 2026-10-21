using System.Security.Claims;
using Cash.Application.Features.Adjustments;
using Cash.Application.Features.Budgets;
using Cash.Application.Features.Movements;
using Cash.Application.Features.Movements.CancelCashMovement;
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
    /// <param name="Type">Receipt (encaissement), Payment (décaissement), OwnerContribution, OwnerWithdrawal, InternalTransfer.</param>
    /// <param name="TreasuryAccountId">Méthode de paiement (compte de trésorerie de l'agence).</param>
    /// <param name="PartyId">Tiers (« Source ») : obligatoire pour Receipt / Payment, absent sinon.</param>
    /// <param name="Unit">MRO (défaut) ou MRU (converti × 10).</param>
    /// <param name="OperationAt">Date et heure ; maintenant si absente. Jamais dans le futur.</param>
    /// <param name="DestinationTreasuryAccountId">Virement interne (InternalTransfer) : compte qui reçoit, de la même agence.</param>
    public sealed record CreateCashMovementRequest(
        Guid? AgencyId,
        string? Type,
        Guid? TreasuryAccountId,
        Guid? PartyId,
        decimal? Amount,
        string? Unit,
        string? Note,
        DateTimeOffset? OperationAt,
        Guid? DestinationTreasuryAccountId);

    public static IEndpointRouteBuilder MapCashEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cash-movements").WithTags("Caisse");

        // Lecture des mouvements : comme les soldes de caisse, avec cash.budget.read.
        group.MapGet("", ListCashMovements).RequirePermission(Permissions.CashBudgetRead);
        group.MapGet("/{id:guid}", GetCashMovement).RequirePermission(Permissions.CashBudgetRead);

        group.MapPost("", CreateCashMovement).RequirePermission(Permissions.CashCreate);

        // Corrections du Superviseur (règle validée) : annuler un mouvement, ajuster un solde.
        group.MapPost("/{id:guid}/cancel", CancelCashMovement).RequirePermission(Permissions.CashManage);

        var adjustments = app.MapGroup("/api/v1/balance-adjustments").WithTags("Caisse");
        adjustments.MapPost("", CreateBalanceAdjustment).RequirePermission(Permissions.CashManage);
        adjustments.MapGet("", ListBalanceAdjustments).RequirePermission(Permissions.CashBudgetRead);

        // Budget de la caisse : calcul en direct et historique (cash.budget.read) ; photo du jour (cash.budget.save).
        var budget = app.MapGroup("/api/v1/cash-budget").WithTags("Caisse");
        budget.MapGet("", GetCashBudget).RequirePermission(Permissions.CashBudgetRead);
        budget.MapGet("/snapshots", ListCashBudgetSnapshots).RequirePermission(Permissions.CashBudgetRead);
        budget.MapGet("/coherence", GetCoherenceCheck).RequirePermission(Permissions.CashBudgetRead);
        budget.MapPost("/snapshots", SaveCashBudget).RequirePermission(Permissions.CashBudgetSave);

        return app;
    }

    /// <param name="Reason">Motif obligatoire.</param>
    public sealed record CancelCashMovementRequest(string? Reason);

    /// <param name="AgencyId">Agence : facultative pour un agent (la sienne), obligatoire pour le Superviseur.</param>
    public sealed record SaveCashBudgetRequest(Guid? AgencyId);

    private static async Task<IResult> GetCashBudget(
        Guid? agencyId,
        ClaimsPrincipal user,
        IQueryHandler<GetCashBudgetQuery, CashBudgetView> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetCashBudgetQuery(actorId, agencyId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <summary>200 avec la photo du jour (créée, ou remplacée si elle existait déjà).</summary>
    private static async Task<IResult> SaveCashBudget(
        SaveCashBudgetRequest? request,
        ClaimsPrincipal user,
        ICommandHandler<SaveCashBudgetCommand, CashBudgetSnapshotView> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new SaveCashBudgetCommand(actorId, request?.AgencyId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <param name="since">Début de la période analysée ; le dernier budget enregistré par défaut.</param>
    private static async Task<IResult> GetCoherenceCheck(
        Guid? agencyId,
        DateTimeOffset? since,
        ClaimsPrincipal user,
        IQueryHandler<GetCoherenceCheckQuery, CoherenceCheckView> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetCoherenceCheckQuery(actorId, agencyId, since), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <param name="from">Premier jour ; 30 derniers jours par défaut.</param>
    private static async Task<IResult> ListCashBudgetSnapshots(
        Guid? agencyId,
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal user,
        IQueryHandler<ListCashBudgetSnapshotsQuery, IReadOnlyList<CashBudgetSnapshotView>> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new ListCashBudgetSnapshotsQuery(actorId, agencyId, from, to), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <param name="Target">TreasuryAccount ou Party.</param>
    /// <param name="Amount">Différence signée : positive = le solde monte, négative = il baisse.</param>
    public sealed record CreateBalanceAdjustmentRequest(
        Guid? AgencyId, string? Target, Guid? TargetId, decimal? Amount, string? Reason, DateTimeOffset? OperationAt);

    /// <summary>204 : écriture du mouvement contre-passée au jour de l'annulation.</summary>
    private static async Task<IResult> CancelCashMovement(
        Guid id,
        CancelCashMovementRequest request,
        ClaimsPrincipal user,
        ICommandHandler<CancelCashMovementCommand> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new CancelCashMovementCommand(actorId, id, request.Reason), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    /// <summary>201 avec l'id de l'ajustement.</summary>
    private static async Task<IResult> CreateBalanceAdjustment(
        CreateBalanceAdjustmentRequest request,
        ClaimsPrincipal user,
        ICommandHandler<CreateBalanceAdjustmentCommand, Guid> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(
            new CreateBalanceAdjustmentCommand(
                actorId, request.AgencyId, request.Target, request.TargetId, request.Amount, request.Reason, request.OperationAt),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/balance-adjustments/{result.Value}", new { id = result.Value })
            : result.Error.ToProblem();
    }

    private static async Task<IResult> ListBalanceAdjustments(
        Guid? agencyId,
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal user,
        IQueryHandler<ListBalanceAdjustmentsQuery, IReadOnlyList<BalanceAdjustmentView>> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new ListBalanceAdjustmentsQuery(actorId, agencyId, from, to), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
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
                request.Amount, request.Unit, request.Note, request.OperationAt, request.DestinationTreasuryAccountId),
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
