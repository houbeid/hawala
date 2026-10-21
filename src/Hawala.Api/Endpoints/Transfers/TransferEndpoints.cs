using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;
using Microsoft.AspNetCore.Mvc;
using Transfers.Application.Features.Transfers;
using Transfers.Application.Features.Transfers.CancelTransfer;
using Transfers.Application.Features.Transfers.CreateTransfer;
using Transfers.Application.Features.Transfers.DeliverTransfer;
using Transfers.Application.Features.Transfers.GetTransfer;
using Transfers.Application.Features.Transfers.GetTransferSummary;
using Transfers.Application.Features.Transfers.ListTransfers;

namespace Hawala.Api.Endpoints.Transfers;

/// <summary>Transferts entre Nouakchott (MRO) et Dakar (FCFA).</summary>
public static class TransferEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <param name="SourceAgencyId">Agence d'envoi : facultative pour un agent (la sienne), obligatoire pour le Superviseur.</param>
    /// <param name="AmountXof">Montant en FCFA entier.</param>
    /// <param name="OperationAt">Date et heure de l'opération ; maintenant si absente. Jamais dans le futur.</param>
    /// <param name="Payments">Répartition du paiement du client, dans la devise de l'agence d'envoi.</param>
    /// <param name="CreditPartyId">Crédit activé : tiers qui doit la part non payée (transfers.credit) ; absent = comptant.</param>
    public sealed record CreateTransferRequest(
        Guid? SourceAgencyId,
        Guid? DestinationAgencyId,
        decimal? AmountXof,
        string? SenderInfo,
        string? BeneficiaryInfo,
        string? Note,
        DateTimeOffset? OperationAt,
        IReadOnlyList<CreateTransferPayment>? Payments,
        Guid? CreditPartyId);

    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/transfers").WithTags("Transferts");

        // Lecture : tout compte connecté, dans son périmètre (envoyés et reçus par son agence) ;
        // prix d'achat, bénéfice, répartition et crédit masqués selon les règles (voir TransferView).
        group.MapGet("", ListTransfers).RequireAuthorization();
        group.MapGet("/{id:guid}", GetTransfer).RequireAuthorization();

        // Cartes du panneau (entrées, sorties, envoyé, livré, non livrés, bénéfice, taux) : Superviseur seulement.
        group.MapGet("/summary", GetTransferSummary).RequirePermission(Permissions.ProfitView);

        group.MapPost("", CreateTransfer).RequirePermission(Permissions.TransfersCreate);
        group.MapPost("/{id:guid}/deliver", DeliverTransfer).RequirePermission(Permissions.TransfersDeliver);
        group.MapPost("/{id:guid}/cancel", CancelTransfer).RequirePermission(Permissions.TransfersCancel);

        return app;
    }

    /// <param name="Reason">Motif obligatoire (500 caractères au plus).</param>
    /// <param name="OperationAt">Date et heure de l'annulation ; maintenant si absente.</param>
    public sealed record CancelTransferRequest(string? Reason, DateTimeOffset? OperationAt);

    /// <summary>204 : annulé, écriture de création contre-passée. Livré ou déjà annulé : 409.</summary>
    private static async Task<IResult> CancelTransfer(
        Guid id,
        CancelTransferRequest request,
        ClaimsPrincipal user,
        ICommandHandler<CancelTransferCommand> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(
            new CancelTransferCommand(actorId, id, request.Reason, request.OperationAt), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    /// <param name="Code">Code de retrait donné par le bénéficiaire.</param>
    /// <param name="OperationAt">Date et heure de la livraison ; maintenant si absente.</param>
    /// <param name="Payments">Remise répartie sur les caisses de l'agence de destination ; total = montant à livrer.</param>
    public sealed record DeliverTransferRequest(
        string? Code,
        DateTimeOffset? OperationAt,
        IReadOnlyList<CreateTransferPayment>? Payments,
        string? Note);

    /// <summary>
    /// 200 si livré (avec les caisses passées en négatif, sans blocage). Code faux : 400
    /// « Transfers.Delivery.WrongCode » avec « remainingAttempts » ; au 5e, 409 « Transfers.Delivery.CodeLocked ».
    /// </summary>
    private static async Task<IResult> DeliverTransfer(
        Guid id,
        DeliverTransferRequest request,
        ClaimsPrincipal user,
        ICommandHandler<DeliverTransferCommand, DeliveryOutcome> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(
            new DeliverTransferCommand(actorId, id, request.Code, request.OperationAt, request.Payments, request.Note),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        return result.Value.Refusal is { } refusal
            ? refusal.ToProblem(new Dictionary<string, object?> { ["remainingAttempts"] = result.Value.RemainingAttempts })
            : TypedResults.Ok(result.Value.Delivered);
    }

    /// <param name="from">Premier jour (aaaa-mm-jj), aujourd'hui par défaut.</param>
    /// <param name="to">Dernier jour inclus, aujourd'hui par défaut.</param>
    /// <param name="status">NotDelivered, Delivered ou Cancelled.</param>
    /// <param name="direction">Sent ou Received.</param>
    /// <param name="page">À partir de 1 ; 50 transferts par page.</param>
    private static async Task<IResult> ListTransfers(
        DateOnly? from,
        DateOnly? to,
        string? status,
        string? direction,
        Guid? agencyId,
        string? search,
        int? page,
        ClaimsPrincipal user,
        IQueryHandler<ListTransfersQuery, TransferList> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(
            new ListTransfersQuery(actorId, from, to, status, direction, agencyId, search, page), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <param name="agencyId">Agence choisie (obligatoire).</param>
    /// <param name="from">Premier jour, aujourd'hui par défaut.</param>
    /// <param name="to">Dernier jour inclus, aujourd'hui par défaut.</param>
    private static async Task<IResult> GetTransferSummary(
        Guid? agencyId,
        DateOnly? from,
        DateOnly? to,
        string? search,
        ClaimsPrincipal user,
        IQueryHandler<GetTransferSummaryQuery, TransferSummary> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetTransferSummaryQuery(actorId, agencyId, from, to, search), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetTransfer(
        Guid id,
        ClaimsPrincipal user,
        IQueryHandler<GetTransferQuery, TransferView> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetTransferQuery(actorId, id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <summary>
    /// 201 avec le code de retrait (affiché une seule fois). La même Idempotency-Key renvoie 200 et le transfert
    /// déjà créé, sans son code : un double clic ne crée pas deux transferts.
    /// </summary>
    private static async Task<IResult> CreateTransfer(
        CreateTransferRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        ClaimsPrincipal user,
        ICommandHandler<CreateTransferCommand, CreatedTransfer> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(
            new CreateTransferCommand(
                actorId,
                idempotencyKey,
                request.SourceAgencyId,
                request.DestinationAgencyId,
                request.AmountXof,
                request.SenderInfo,
                request.BeneficiaryInfo,
                request.Note,
                request.OperationAt,
                request.Payments,
                request.CreditPartyId),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        return result.Value.Replayed
            ? TypedResults.Ok(result.Value)
            : TypedResults.Created($"/api/v1/transfers/{result.Value.Id}", result.Value);
    }
}
