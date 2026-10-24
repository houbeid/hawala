using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;
using Treasury.Application.Features.Accounts;
using Treasury.Application.Features.Accounts.CreateTreasuryAccount;
using Treasury.Application.Features.Accounts.DeactivateTreasuryAccount;
using Treasury.Application.Features.Accounts.ReactivateTreasuryAccount;
using Treasury.Application.Features.Accounts.RenameTreasuryAccount;
using Treasury.Application.Features.Accounts.GetTreasuryAccount;
using Treasury.Application.Features.Accounts.GetTreasuryAccountStatement;
using Treasury.Application.Features.Accounts.ListTreasuryAccounts;

namespace Hawala.Api.Endpoints.Treasury;

/// <summary>Comptes de trésorerie : caisses, banques, wallets (« méthodes de paiement »).</summary>
public static class TreasuryEndpoints
{
    /// <param name="Type">Cash, Bank ou Wallet.</param>
    public sealed record CreateTreasuryAccountRequest(string? Name, string? Type, Guid? AgencyId);

    public sealed record RenameTreasuryAccountRequest(string? Name);

    public static IEndpointRouteBuilder MapTreasuryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/treasury-accounts").WithTags("Trésorerie");

        // Lecture : tout compte connecté, dans son périmètre (son agence ; les deux pour le Superviseur).
        // Les soldes ne sont renseignés qu'avec cash.budget.read.
        group.MapGet("", ListTreasuryAccounts).RequireAuthorization();
        group.MapGet("/{id:guid}", GetTreasuryAccount).RequireAuthorization();

        // Relevé (décision du 22 octobre 2026) : montre des montants, donc cash.budget.read, vérifiée par le handler.
        group.MapGet("/{id:guid}/statement", GetTreasuryAccountStatement).RequireAuthorization();

        // Règle validée : seul le Superviseur crée, renomme, désactive ou réactive un compte de trésorerie.
        group.MapPost("", CreateTreasuryAccount).RequirePermission(Permissions.TreasuryManage);
        group.MapPut("/{id:guid}", RenameTreasuryAccount).RequirePermission(Permissions.TreasuryManage);
        group.MapPost("/{id:guid}/deactivate", DeactivateTreasuryAccount).RequirePermission(Permissions.TreasuryManage);
        group.MapPost("/{id:guid}/reactivate", ReactivateTreasuryAccount).RequirePermission(Permissions.TreasuryManage);

        return app;
    }

    private static async Task<IResult> RenameTreasuryAccount(
        Guid id,
        RenameTreasuryAccountRequest request,
        ICommandHandler<RenameTreasuryAccountCommand> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new RenameTreasuryAccountCommand(id, request.Name), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> DeactivateTreasuryAccount(
        Guid id,
        ICommandHandler<DeactivateTreasuryAccountCommand> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new DeactivateTreasuryAccountCommand(id), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> ReactivateTreasuryAccount(
        Guid id,
        ICommandHandler<ReactivateTreasuryAccountCommand> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new ReactivateTreasuryAccountCommand(id), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> ListTreasuryAccounts(
        Guid? agencyId,
        string? type,
        bool? includeInactive,
        ClaimsPrincipal user,
        IQueryHandler<ListTreasuryAccountsQuery, IReadOnlyList<TreasuryAccountSummary>> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var query = new ListTreasuryAccountsQuery(actorId, agencyId, type, includeInactive ?? false);
        var result = await handler.Handle(query, cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetTreasuryAccount(
        Guid id,
        ClaimsPrincipal user,
        IQueryHandler<GetTreasuryAccountQuery, TreasuryAccountSummary> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetTreasuryAccountQuery(actorId, id), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetTreasuryAccountStatement(
        Guid id,
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal user,
        IQueryHandler<GetTreasuryAccountStatementQuery, TreasuryAccountStatement> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(new GetTreasuryAccountStatementQuery(actorId, id, from, to), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> CreateTreasuryAccount(
        CreateTreasuryAccountRequest request,
        ICommandHandler<CreateTreasuryAccountCommand, Guid> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateTreasuryAccountCommand(request.Name, request.Type, request.AgencyId);
        var result = await handler.Handle(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/treasury-accounts/{result.Value}", new { id = result.Value })
            : result.Error.ToProblem();
    }
}
