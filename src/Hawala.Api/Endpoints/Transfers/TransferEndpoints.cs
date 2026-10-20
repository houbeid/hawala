using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;
using Microsoft.AspNetCore.Mvc;
using Transfers.Application.Features.Transfers.CreateTransfer;

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

        group.MapPost("", CreateTransfer).RequirePermission(Permissions.TransfersCreate);

        return app;
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
