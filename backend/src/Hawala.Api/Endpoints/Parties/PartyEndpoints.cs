using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;
using Parties.Application.Features.Parties;
using Parties.Application.Features.Parties.ArchiveParty;
using Parties.Application.Features.Parties.CreateParty;
using Parties.Application.Features.Parties.GetParty;
using Parties.Application.Features.Parties.GetPartyStatement;
using Parties.Application.Features.Parties.ListParties;
using Parties.Application.Features.Parties.SetCreditLimit;
using Parties.Application.Features.Parties.SetOpeningBalance;
using Parties.Application.Features.Parties.UpdateParty;

namespace Hawala.Api.Endpoints.Parties;

/// <summary>Tiers : clients, fournisseurs, personnel. Communs aux deux agences, un solde par agence.</summary>
public static class PartyEndpoints
{
    /// <param name="Type">Client, Supplier, Staff ou Other.</param>
    /// <param name="Phone">Format international (+222…, +221…), facultatif.</param>
    public sealed record CreatePartyRequest(
        string? Name,
        string? Type,
        string? Phone,
        string? Nni,
        string? Address,
        string? Note);

    /// <summary>État complet de la fiche : un téléphone, un NNI, une adresse ou une note absents sont retirés.</summary>
    /// <param name="InBalanceSheet">Obligatoire (true ou false).</param>
    public sealed record UpdatePartyRequest(
        string? Name,
        string? Type,
        string? Phone,
        string? Nni,
        string? Address,
        string? Note,
        bool? InBalanceSheet);

    /// <param name="Amount">Dans la devise de l'agence : positif = il nous doit (rouge), négatif = on lui doit (vert).</param>
    /// <param name="OperationDate">Date de l'écriture d'ouverture (aaaa-mm-jj) ; aujourd'hui si absente.</param>
    public sealed record SetOpeningBalanceRequest(decimal? Amount, DateOnly? OperationDate);

    /// <param name="CreditLimit">Devise de l'agence ; positif ou nul (0 = aucun crédit) ; null ou absent = pas de plafond.</param>
    public sealed record SetCreditLimitRequest(decimal? CreditLimit);

    public static IEndpointRouteBuilder MapPartyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/parties").WithTags("Tiers");

        // Lecture : tout compte connecté (un tiers est commun aux deux agences).
        // Les soldes ne sont renseignés qu'avec parties.view_balance, et seulement pour les agences du périmètre.
        group.MapGet("", ListParties).RequireAuthorization();

        // Export Excel : mêmes filtres, périmètre et soldes visibles que la liste.
        group.MapGet("/export", PartyExport.ExportAsync).RequirePermission(Permissions.Export);
        group.MapGet("/{id:guid}", GetParty).RequireAuthorization();

        // Le relevé montre des montants : parties.view_balance est vérifiée par le handler (code d'erreur précis).
        group.MapGet("/{id:guid}/statement", GetPartyStatement).RequireAuthorization();

        // Règle validée : un agent peut créer un tiers ; seul le Superviseur le modifie.
        group.MapPost("", CreateParty).RequirePermission(Permissions.PartiesCreate);
        group.MapPut("/{id:guid}", UpdateParty).RequirePermission(Permissions.PartiesManage);
        group.MapPut("/{id:guid}/agencies/{agencyId:guid}/opening-balance", SetOpeningBalance)
            .RequirePermission(Permissions.PartiesManage);
        group.MapPut("/{id:guid}/agencies/{agencyId:guid}/credit-limit", SetCreditLimit)
            .RequirePermission(Permissions.PartiesManage);
        group.MapPost("/{id:guid}/archive", ArchiveParty).RequirePermission(Permissions.PartiesManage);
        group.MapPost("/{id:guid}/unarchive", UnarchiveParty).RequirePermission(Permissions.PartiesManage);

        return app;
    }

    private static async Task<IResult> ArchiveParty(
        Guid id, ClaimsPrincipal user, ICommandHandler<ArchivePartyCommand> handler, CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new ArchivePartyCommand(actorId, id), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> UnarchiveParty(
        Guid id, ClaimsPrincipal user, ICommandHandler<UnarchivePartyCommand> handler, CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new UnarchivePartyCommand(actorId, id), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> SetCreditLimit(
        Guid id,
        Guid agencyId,
        SetCreditLimitRequest request,
        ClaimsPrincipal user,
        ICommandHandler<SetCreditLimitCommand> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new SetCreditLimitCommand(actorId, id, agencyId, request.CreditLimit), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> SetOpeningBalance(
        Guid id,
        Guid agencyId,
        SetOpeningBalanceRequest request,
        ClaimsPrincipal user,
        ICommandHandler<SetOpeningBalanceCommand> handler,
        CancellationToken cancellationToken)
    {
        // L'auteur de l'écriture vient uniquement du JWT.
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var command = new SetOpeningBalanceCommand(actorId, id, agencyId, request.Amount, request.OperationDate);
        var result = await handler.Handle(command, cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> UpdateParty(
        Guid id,
        UpdatePartyRequest request,
        ClaimsPrincipal user,
        ICommandHandler<UpdatePartyCommand> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var command = new UpdatePartyCommand(
            actorId, id, request.Name, request.Type, request.Phone, request.Nni, request.Address, request.Note, request.InBalanceSheet);
        var result = await handler.Handle(command, cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> ListParties(
        Guid? agencyId,
        string? search,
        string? type,
        string? balanceSheet,
        string? balance,
        bool? includeArchived,
        ClaimsPrincipal user,
        IQueryHandler<ListPartiesQuery, PartyList> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var query = new ListPartiesQuery(actorId, agencyId, search, type, balanceSheet, balance, includeArchived ?? false);
        var result = await handler.Handle(query, cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetParty(
        Guid id,
        Guid? agencyId,
        ClaimsPrincipal user,
        IQueryHandler<GetPartyQuery, PartySummary> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetPartyQuery(actorId, id, agencyId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetPartyStatement(
        Guid id,
        Guid? agencyId,
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal user,
        IQueryHandler<GetPartyStatementQuery, PartyStatement> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetPartyStatementQuery(actorId, id, agencyId, from, to), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> CreateParty(
        CreatePartyRequest request,
        ICommandHandler<CreatePartyCommand, Guid> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreatePartyCommand(
            request.Name, request.Type, request.Phone, request.Nni, request.Address, request.Note);
        var result = await handler.Handle(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/parties/{result.Value}", new { id = result.Value })
            : result.Error.ToProblem();
    }
}
