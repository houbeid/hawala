using Audit.Contracts;
using Hawala.SharedKernel.Results;

namespace Audit.Domain;

/// <summary>
/// Une ligne du journal d'audit : qui, quoi, sur quelle entité, quand, d'où, avec l'avant / après des champs changés.
/// Insérée seulement : jamais modifiée ni supprimée (aucune méthode pour le faire ; déclencheur en base en plus).
/// </summary>
public sealed class AuditEntry
{
    public const int ActionMaxLength = 100;
    public const int EntityTypeMaxLength = 100;
    public const int ReasonMaxLength = 500;
    public const int FieldMaxLength = 100;
    public const int ValueMaxLength = 4000;

    /// <summary>Une adresse IPv6 complète tient en 45 caractères.</summary>
    public const int IpAddressMaxLength = 45;

    /// <summary>Le User-Agent vient du navigateur, sans limite : on garde ses 512 premiers caractères.</summary>
    public const int UserAgentMaxLength = 512;

    // Rempli par EF Core au chargement (colonne JSONB « Changes ») : pas readonly.
    private List<AuditChange> _changes = [];

    // Constructeur réservé à EF Core.
    private AuditEntry()
    {
        Action = null!;
        EntityType = null!;
    }

    private AuditEntry(
        Guid id,
        DateTimeOffset occurredAt,
        string action,
        string entityType,
        Guid? entityId,
        Guid? actorId,
        Guid? agencyId,
        string? reason,
        IEnumerable<AuditChange> changes,
        string? ipAddress,
        string? userAgent)
    {
        Id = id;
        OccurredAt = occurredAt;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        ActorId = actorId;
        AgencyId = agencyId;
        Reason = reason;
        _changes = [.. changes];
        IpAddress = ipAddress;
        UserAgent = userAgent;
    }

    public Guid Id { get; private set; }

    /// <summary>Instant de l'action, en UTC.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public Guid? EntityId { get; private set; }

    /// <summary>Null pour une action du système (démarrage, tâche planifiée).</summary>
    public Guid? ActorId { get; private set; }

    public Guid? AgencyId { get; private set; }

    public string? Reason { get; private set; }

    public IReadOnlyList<AuditChange> Changes => _changes;

    /// <summary>Null hors d'une requête HTTP.</summary>
    public string? IpAddress { get; private set; }

    /// <summary>Appareil (User-Agent du navigateur), null hors d'une requête HTTP.</summary>
    public string? UserAgent { get; private set; }

    public static Result<AuditEntry> Create(
        Guid id,
        DateTimeOffset occurredAt,
        AuditRecord record,
        string? ipAddress,
        string? userAgent)
    {
        ArgumentNullException.ThrowIfNull(record);

        var action = record.Action?.Trim();
        if (string.IsNullOrEmpty(action))
        {
            return AuditErrors.Entry.ActionEmpty;
        }

        if (action.Length > ActionMaxLength)
        {
            return AuditErrors.Entry.ActionTooLong;
        }

        var entityType = record.EntityType?.Trim();
        if (string.IsNullOrEmpty(entityType))
        {
            return AuditErrors.Entry.EntityTypeEmpty;
        }

        if (entityType.Length > EntityTypeMaxLength)
        {
            return AuditErrors.Entry.EntityTypeTooLong;
        }

        var reason = string.IsNullOrWhiteSpace(record.Reason) ? null : record.Reason.Trim();
        if (reason is { Length: > ReasonMaxLength })
        {
            return AuditErrors.Entry.ReasonTooLong;
        }

        var changes = new List<AuditChange>();
        foreach (var change in record.Changes ?? [])
        {
            var field = change.Field?.Trim();
            if (string.IsNullOrEmpty(field))
            {
                return AuditErrors.Change.FieldEmpty;
            }

            if (field.Length > FieldMaxLength)
            {
                return AuditErrors.Change.FieldTooLong;
            }

            if (changes.Exists(c => string.Equals(c.Field, field, StringComparison.Ordinal)))
            {
                return AuditErrors.Change.FieldDuplicate;
            }

            if (change.Before is { Length: > ValueMaxLength } || change.After is { Length: > ValueMaxLength })
            {
                return AuditErrors.Change.ValueTooLong;
            }

            if (string.Equals(change.Before, change.After, StringComparison.Ordinal))
            {
                return AuditErrors.Change.Unchanged;
            }

            changes.Add(change with { Field = field });
        }

        return new AuditEntry(
            id,
            occurredAt,
            action,
            entityType,
            record.EntityId,
            record.ActorId,
            record.AgencyId,
            reason,
            changes,
            Clip(ipAddress, IpAddressMaxLength),
            Clip(userAgent, UserAgentMaxLength));
    }

    // Données techniques venues de la requête : coupées plutôt que refusées (l'action reste tracée).
    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
