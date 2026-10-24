import type { Role } from '@/shared/auth/types';
import { parseMoneyInput, type InputUnit } from '@/shared/format/moneyInput';
import type { AccountStatus, UserListParams, UserSummary } from './types';

// Règles de l'écran « Comptes » (skill, « Comptes et permissions ») ; le serveur reste le seul vrai contrôle :
// - un compte de RÔLE Superviseur crée tous les rôles, change rôle et agence ; un non-Superviseur avec users.manage
//   ne gère que son agence et ne crée que Agent, Caissier, Consultation (décision du 21 octobre 2026) ;
// - personne ne bloque, ne réinitialise ni ne change le rôle de son propre compte ;
// - un bouton interdit est absent, jamais grisé.

export const ROLES: readonly Role[] = ['Supervisor', 'Agent', 'Cashier', 'ReadOnly'];
export const STATUSES: readonly AccountStatus[] = ['Active', 'Blocked'];

/** Filtres de la liste lus dans l'adresse ; une valeur inconnue est ignorée. */
export function parseUserSearch(search: Record<string, unknown>): UserListParams {
  const result: UserListParams = {};
  if (typeof search.agencyId === 'string' && search.agencyId) result.agencyId = search.agencyId;
  if (ROLES.includes(search.role as Role)) result.role = search.role as Role;
  if (STATUSES.includes(search.status as AccountStatus)) result.status = search.status as AccountStatus;
  if (typeof search.search === 'string' && search.search.trim()) result.search = search.search.trim();
  return result;
}

/** Rôles proposés à la création selon le rôle du compte connecté. */
export function creatableRoles(actorRole: Role): readonly Role[] {
  return actorRole === 'Supervisor' ? ROLES : ROLES.filter((r) => r !== 'Supervisor');
}

export interface UserActions {
  editProfile: boolean;
  block: boolean;
  unblock: boolean;
  resetPassword: boolean;
  roleAndAgency: boolean;
}

/** Actions affichées sur la fiche d'un compte, pour le compte connecté (actor). */
export function availableUserActions(target: UserSummary, actor: { id: string; role: Role }): UserActions {
  const self = target.id === actor.id;
  return {
    editProfile: true,
    block: !self && target.status === 'Active',
    unblock: !self && target.status === 'Blocked',
    resetPassword: !self,
    roleAndAgency: !self && actor.role === 'Supervisor',
  };
}

export type StatusMark = 'active' | 'blocked' | 'temporaryPassword' | 'locked';

/** Mentions d'état écrites sur une ligne (jamais la couleur seule). */
export function statusMarks(user: UserSummary, now: Date = new Date()): StatusMark[] {
  const marks: StatusMark[] = [user.status === 'Blocked' ? 'blocked' : 'active'];
  if (user.mustChangePassword) marks.push('temporaryPassword');
  if (user.lockedUntil && new Date(user.lockedUntil) > now) marks.push('locked');
  return marks;
}

/**
 * Liste COMPLÈTE des permissions voulues (PUT /users/{id}/permissions) : l'état actuel, modifié par les bascules
 * de l'écran (permission → accordée ou non). Triée pour une requête stable.
 */
export function grantedPermissions(states: { permission: string; granted: boolean }[], changes: Record<string, boolean>): string[] {
  return states
    .filter((s) => changes[s.permission] ?? s.granted)
    .map((s) => s.permission)
    .sort();
}

export type LimitInput = { ok: true; value: number | null } | { ok: false; error: 'invalid' | 'tooPrecise' };

/** Plafond facultatif, dans la devise de l'agence : vide = pas de limite ; zéro permis ; trop de décimales refusé. */
export function parseLimit(raw: string, unit: InputUnit): LimitInput {
  if (!raw.trim()) return { ok: true, value: null };
  const parsed = parseMoneyInput(raw, unit);
  if (parsed.ok) return { ok: true, value: parsed.value };
  if (parsed.error === 'notPositive') return { ok: true, value: 0 };
  return { ok: false, error: parsed.error === 'tooPrecise' ? 'tooPrecise' : 'invalid' };
}
