using Organization.Contracts;
using Audit.Application.Features;
using Audit.Contracts;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Contracts;
using Parties.Contracts;

namespace Hawala.Api.Endpoints.Audit;

/// <summary>
/// Journal d'audit (phase 6, règles validées le 22 octobre 2026) : permission audit.view (Superviseur). Seulement les actions.
/// Le périmètre s'applique comme partout : un compte qui a reçu audit.view sans être Superviseur ne lit que son agence.
/// Les noms (auteur, compte ou tiers concerné) sont ajoutés ici : le module Audit ne dépend d'aucun autre module.
/// Le texte lisible (« X a retiré … à Y ») est construit par l'interface, en français ou en arabe.
/// </summary>
public static class AuditEndpoints
{
    public sealed record AuditActor(Guid Id, string Name);

    /// <param name="EntityName">Nom du compte ou du tiers concerné ; null pour un autre type ou un id inconnu.</param>
    /// <param name="Actor">Auteur ; null pour une action du système.</param>
    public sealed record AuditLine(
        Guid Id,
        DateTimeOffset OccurredAt,
        string Action,
        string EntityType,
        Guid? EntityId,
        string? EntityName,
        AuditActor? Actor,
        Guid? AgencyId,
        string? Reason,
        IReadOnlyList<AuditChange> Changes,
        string? IpAddress,
        string? Device);

    public sealed record AuditResponse(IReadOnlyList<AuditLine> Items, int Page, int TotalCount);

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/audit", ListAudit)
            .WithTags("Audit")
            .RequirePermission(Permissions.AuditView);

        return app;
    }

    private static async Task<IResult> ListAudit(
        DateOnly? from,
        DateOnly? to,
        Guid? actorId,
        string? action,
        string? entityType,
        Guid? entityId,
        Guid? agencyId,
        int? page,
        IAgencyScope scope,
        IQueryHandler<ListAuditEntriesQuery, AuditPage> handler,
        IAccountDirectory accounts,
        IPartyDirectory parties,
        IAgencyDirectory agencies,
        CancellationToken cancellationToken)
    {
        // Hors Superviseur : son agence seulement (une autre agence demandée = liste vide).
        var onlyAgency = !scope.SeesAllAgencies;
        if (onlyAgency)
        {
            if (agencyId is { } requested && requested != scope.AgencyId)
            {
                return TypedResults.Ok(new AuditResponse([], page ?? 1, 0));
            }

            agencyId = scope.AgencyId;
        }

        var result = await handler.Handle(
            new ListAuditEntriesQuery(from, to, actorId, action, entityType, entityId, agencyId, onlyAgency, page ?? 1),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        var items = result.Value.Items;

        var accountNames = new Dictionary<Guid, string>();
        var accountIds = items.Select(i => i.ActorId)
            .Concat(items.Where(i => i.EntityType == "UserAccount").Select(i => i.EntityId))
            .OfType<Guid>()
            .Distinct();
        foreach (var id in accountIds)
        {
            if (await accounts.FindAsync(id, cancellationToken) is { } account)
            {
                accountNames[id] = account.FullName;
            }
        }

        var partyIds = items.Where(i => i.EntityType == "Party").Select(i => i.EntityId).OfType<Guid>().Distinct().ToList();
        var partyNames = partyIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await parties.GetNamesAsync(partyIds, cancellationToken);

        var agencyNames = items.Any(i => i.EntityType == "Agency")
            ? (await agencies.GetAllAsync(cancellationToken)).ToDictionary(a => a.Id, a => a.Name)
            : new Dictionary<Guid, string>();

        var lines = items.Select(i => new AuditLine(
                i.Id,
                i.OccurredAt,
                i.Action,
                i.EntityType,
                i.EntityId,
                NameOf(i, accountNames, partyNames, agencyNames),
                i.ActorId is { } actor ? new AuditActor(actor, accountNames.GetValueOrDefault(actor, "?")) : null,
                i.AgencyId,
                i.Reason,
                i.Changes,
                i.IpAddress,
                i.Device))
            .ToList();

        return TypedResults.Ok(new AuditResponse(lines, result.Value.Page, result.Value.TotalCount));
    }

    private static string? NameOf(
        AuditEntryView entry,
        Dictionary<Guid, string> accounts,
        IReadOnlyDictionary<Guid, string> parties,
        Dictionary<Guid, string> agencies) =>
        entry.EntityId is not { } id
            ? null
            : entry.EntityType switch
            {
                "UserAccount" => accounts.GetValueOrDefault(id),
                "Party" => parties.GetValueOrDefault(id),
                "Agency" => agencies.GetValueOrDefault(id),
                _ => null,
            };
}
