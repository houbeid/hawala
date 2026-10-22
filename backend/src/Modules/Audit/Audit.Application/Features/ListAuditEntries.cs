using Audit.Contracts;
using Audit.Domain;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;

namespace Audit.Application.Features;

/// <summary>Filtres du journal. Un champ null = pas de filtre.</summary>
/// <param name="From">Premier jour (inclus) ; aujourd'hui par défaut, comme les autres listes.</param>
/// <param name="To">Dernier jour (inclus) ; le jour de début par défaut.</param>
/// <param name="ActorId">Auteur des actions.</param>
/// <param name="Action">Début du code d'action (ex. « Identity. » ou « Identity.Account.PermissionsChanged »).</param>
/// <param name="EntityType">Type d'entité (« UserAccount », « Party »…), avec ou sans <paramref name="EntityId"/>.</param>
/// <param name="AgencyId">Agence concernée.</param>
/// <param name="OnlyAgency">
/// Vrai pour un lecteur limité à son agence : les lignes sans agence (actions sur un Superviseur, sur toute l'app) sont exclues.
/// </param>
public sealed record ListAuditEntriesQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? ActorId = null,
    string? Action = null,
    string? EntityType = null,
    Guid? EntityId = null,
    Guid? AgencyId = null,
    bool OnlyAgency = false,
    int Page = 1) : IQuery<AuditPage>;

/// <param name="TotalCount">Nombre de lignes de la période avec ces filtres (toutes pages).</param>
public sealed record AuditPage(IReadOnlyList<AuditEntryView> Items, int Page, int TotalCount);

/// <param name="Device">Appareil (User-Agent) ; null hors requête HTTP.</param>
public sealed record AuditEntryView(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    string EntityType,
    Guid? EntityId,
    Guid? ActorId,
    Guid? AgencyId,
    string? Reason,
    IReadOnlyList<AuditChange> Changes,
    string? IpAddress,
    string? Device);

/// <summary>Filtres résolus pour le stockage : période en instants UTC (Nouakchott et Dakar sont en UTC+0).</summary>
public sealed record AuditFilter(
    DateTimeOffset From,
    DateTimeOffset ToExclusive,
    Guid? ActorId,
    string? ActionPrefix,
    string? EntityType,
    Guid? EntityId,
    Guid? AgencyId,
    bool OnlyAgency);

/// <summary>Lecture du journal, du plus récent au plus ancien.</summary>
public interface IAuditReader
{
    Task<(IReadOnlyList<AuditEntry> Items, int TotalCount)> ListAsync(
        AuditFilter filter, int skip, int take, CancellationToken cancellationToken);
}

/// <summary>Journal d'audit (permission audit.view, vérifiée par l'API qui applique aussi le périmètre). 50 lignes par page.</summary>
public sealed class ListAuditEntriesQueryHandler(IAuditReader reader, IClock clock)
    : IQueryHandler<ListAuditEntriesQuery, AuditPage>
{
    public const int PageSize = 50;

    public async Task<Result<AuditPage>> Handle(ListAuditEntriesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var from = query.From ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var to = query.To ?? from;
        if (to < from)
        {
            return AuditErrors.List.PeriodInvalid;
        }

        if (query.Page < 1)
        {
            return AuditErrors.List.PageInvalid;
        }

        var filter = new AuditFilter(
            new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            query.ActorId,
            string.IsNullOrWhiteSpace(query.Action) ? null : query.Action.Trim(),
            string.IsNullOrWhiteSpace(query.EntityType) ? null : query.EntityType.Trim(),
            query.EntityId,
            query.AgencyId,
            query.OnlyAgency);

        var (items, total) = await reader.ListAsync(filter, (query.Page - 1) * PageSize, PageSize, cancellationToken);

        return new AuditPage(
            [.. items.Select(e => new AuditEntryView(
                e.Id, e.OccurredAt, e.Action, e.EntityType, e.EntityId, e.ActorId, e.AgencyId, e.Reason,
                e.Changes, e.IpAddress, e.UserAgent))],
            query.Page,
            total);
    }
}
