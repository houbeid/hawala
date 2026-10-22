namespace Audit.Contracts;

/// <summary>
/// Une action à tracer dans le journal d'audit : qui, quoi, sur quelle entité, avec l'avant / après des champs changés.
/// L'instant, l'IP et l'appareil sont ajoutés par le module Audit.
/// </summary>
/// <param name="Action">Code stable de l'action, au format des codes d'erreur (ex. « Identity.Account.Blocked »).</param>
/// <param name="EntityType">Type de l'entité touchée (ex. « UserAccount », « Party »).</param>
/// <param name="EntityId">Id de l'entité, si elle en a un.</param>
/// <param name="ActorId">Compte qui agit (lu dans le JWT par l'appelant) ; null pour le système (démarrage, tâche planifiée).</param>
/// <param name="AgencyId">Agence concernée, si l'action en touche une : servira au filtre de lecture.</param>
/// <param name="Reason">Motif saisi, s'il y en a un.</param>
/// <param name="Changes">Champs changés, avant / après, déjà mis en texte par l'appelant ; vide pour une action sans changement de valeur.</param>
public sealed record AuditRecord(
    string Action,
    string EntityType,
    Guid? EntityId,
    Guid? ActorId,
    Guid? AgencyId = null,
    string? Reason = null,
    IReadOnlyList<AuditChange>? Changes = null);

/// <summary>Un champ changé. Une valeur absente (champ ajouté ou retiré) est null.</summary>
public sealed record AuditChange(string Field, string? Before, string? After);
