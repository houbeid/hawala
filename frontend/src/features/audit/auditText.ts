import { isIsoDay } from '@/shared/format/dates';
import { AUDIT_FAMILIES, type AuditChange, type AuditParams } from './types';

// Texte lisible du journal d'audit (architecture : « X a retiré *Annuler un transfert* à Y »), construit par
// l'interface à partir du code de l'action, des noms et des changements renvoyés par l'API. Les clés de traduction
// suivent les codes (« Identity.Account.Blocked » → audit.actions.Identity.Account.Blocked) ; un code inconnu
// affiche le code lui-même, jamais rien.

/** Filtres lus dans l'adresse ; une valeur inconnue est ignorée. */
export function parseAuditSearch(search: Record<string, unknown>): AuditParams {
  const result: AuditParams = {};
  if (isIsoDay(search.from)) result.from = search.from;
  if (isIsoDay(search.to)) result.to = search.to;
  if ((AUDIT_FAMILIES as readonly string[]).includes(search.action as string)) result.action = search.action as string;
  const page = Number(search.page);
  if (Number.isInteger(page) && page > 1) result.page = page;
  return result;
}

export type ChangeView =
  /** Permission accordée ou retirée : « a accordé / a retiré « … » ». */
  | { kind: 'permission'; permission: string; granted: boolean }
  /** Autre champ : nom du champ, valeur avant, valeur après (null = « — »). */
  | { kind: 'field'; field: string; before: string | null; after: string | null };

/** Lit un changement : les permissions s'écrivent « granted » / « not_granted » (SetUserPermissions). */
export function changeView(action: string, change: AuditChange): ChangeView {
  if (action === 'Identity.Account.PermissionsChanged') {
    return { kind: 'permission', permission: change.field, granted: change.after === 'granted' };
  }
  return { kind: 'field', field: change.field, before: change.before, after: change.after };
}

/**
 * Valeur d'un champ, telle que le serveur l'écrit : « set » / « changed » (téléphone, NNI : jamais la valeur en
 * clair), « true » / « false », sinon le texte tel quel (montants en « 0.## », dates, noms).
 * Renvoie une clé de traduction (« audit.values.set ») ou le texte brut.
 */
export function valueKey(value: string | null): { key: string } | { text: string } {
  if (value === null) return { key: 'audit.values.none' };
  if (value === 'set' || value === 'changed' || value === 'true' || value === 'false') return { key: `audit.values.${value}` };
  return { text: value };
}
