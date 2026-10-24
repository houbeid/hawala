import { isIsoDay } from '@/shared/format/dates';
import { parseMoneyInput, type InputUnit } from '@/shared/format/moneyInput';
import {
  CASH_MOVEMENT_TYPES,
  type AdjustmentTarget,
  type CashMovementType,
  type CreateBalanceAdjustmentRequest,
  type CreateCashMovementRequest,
} from './types';

// Règles de l'écran Caisse (skill métier, « Caisse », décisions du 21 octobre 2026) :
// - le type fixe le signe ; encaissement et décaissement exigent un tiers, les autres types n'en ont pas ;
// - tout mouvement se fait sur un compte de trésorerie actif de l'agence (approvisionnement et retrait compris :
//   le backend l'exige, la maquette ne le dessinait pas) ; un virement interne va vers un AUTRE compte de l'agence ;
// - montant MRO à 2 décimales, MRU à 3 (× 10), FCFA entier ; jamais d'arrondi ; date jamais future ;
// - un compte peut passer en négatif : avertissement seulement, sans blocage.

/** Types sans tiers (« Approvisionnement de caisse », « Retrait du patron », « Virement interne »). */
export function needsParty(type: CashMovementType): boolean {
  return type === 'Receipt' || type === 'Payment';
}

/** L'argent sort du compte choisi (le compte d'origine pour un virement). */
export function leavesAccount(type: CashMovementType): boolean {
  return type === 'Payment' || type === 'OwnerWithdrawal' || type === 'InternalTransfer';
}

export interface CashSearch {
  agencyId?: string;
  from?: string;
  to?: string;
  type?: CashMovementType;
  account?: string;
  search?: string;
  page?: number;
}

/** Filtres de la liste lus dans l'adresse ; une valeur inconnue est ignorée ; « aujourd'hui » et la page 1 ne s'écrivent pas. */
export function parseCashSearch(search: Record<string, unknown>): CashSearch {
  const result: CashSearch = {};
  if (typeof search.agencyId === 'string' && search.agencyId) result.agencyId = search.agencyId;
  if (isIsoDay(search.from)) result.from = search.from;
  if (isIsoDay(search.to)) result.to = search.to;
  if (CASH_MOVEMENT_TYPES.includes(search.type as CashMovementType)) result.type = search.type as CashMovementType;
  if (typeof search.account === 'string' && search.account) result.account = search.account;
  if (typeof search.search === 'string' && search.search.trim()) result.search = search.search.trim();
  const page = Number(search.page);
  if (Number.isInteger(page) && page > 1) result.page = page;
  return result;
}

/** Type et agence proposés par l'adresse de création (/cash/new?type=Payment&agencyId=…) ; encaissement par défaut. */
/** `fromQueue` : clé d'une opération rejetée de la file hors ligne à corriger (« Corriger »). */
export function parseNewMovementSearch(search: Record<string, unknown>): { type?: CashMovementType; agencyId?: string; fromQueue?: string } {
  const result: { type?: CashMovementType; agencyId?: string; fromQueue?: string } = {};
  if (CASH_MOVEMENT_TYPES.includes(search.type as CashMovementType)) result.type = search.type as CashMovementType;
  if (typeof search.agencyId === 'string' && search.agencyId) result.agencyId = search.agencyId;
  if (typeof search.fromQueue === 'string' && search.fromQueue) result.fromQueue = search.fromQueue;
  return result;
}

export interface CashForm {
  type: CashMovementType;
  accountId: string;
  destinationId: string;
  partyId: string | null;
  amount: string;
  unit: InputUnit;
  note: string;
  /** Valeur d'un champ datetime-local (heure locale) ; vide = maintenant. */
  operationAt: string;
}

export type CashFormErrors = Partial<Record<'account' | 'destination' | 'party' | 'amount' | 'operationAt', string>>;

export function operationAtIso(value: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** Clés de traduction des erreurs de saisie ; vide = formulaire valable. */
export function validateCashForm(form: CashForm, now: Date = new Date()): CashFormErrors {
  const errors: CashFormErrors = {};
  if (!form.accountId) errors.account = 'cash.form.errors.accountRequired';
  if (form.type === 'InternalTransfer') {
    if (!form.destinationId) errors.destination = 'cash.form.errors.destinationRequired';
    else if (form.destinationId === form.accountId) errors.destination = 'cash.form.errors.sameAccount';
  }
  if (needsParty(form.type) && !form.partyId) errors.party = 'cash.form.errors.partyRequired';
  const amount = parseMoneyInput(form.amount, form.unit);
  if (!amount.ok) errors.amount = amount.error === 'tooPrecise' ? `cash.form.errors.tooPrecise${form.unit}` : `cash.form.errors.amount.${amount.error}`;
  if (form.operationAt) {
    const iso = operationAtIso(form.operationAt);
    if (iso === null) errors.operationAt = 'cash.form.errors.dateInvalid';
    else if (new Date(iso) > now) errors.operationAt = 'cash.form.errors.futureDate';
  }
  return errors;
}

/**
 * Requête envoyée au serveur. Le montant part en MRO (ou FCFA) déjà converti sur le texte (MRU × 10 exact) :
 * aucune unité n'est envoyée. agencyId : seulement quand le compte connecté choisit l'agence (Superviseur).
 */
export function buildCashRequest(form: CashForm, agencyId: string | null): CreateCashMovementRequest {
  const amount = parseMoneyInput(form.amount, form.unit);
  if (!amount.ok) throw new Error('Formulaire non validé.');
  return {
    agencyId,
    type: form.type,
    treasuryAccountId: form.accountId,
    partyId: needsParty(form.type) ? form.partyId : null,
    amount: amount.value,
    unit: null,
    note: form.note.trim() || null,
    operationAt: operationAtIso(form.operationAt),
    destinationTreasuryAccountId: form.type === 'InternalTransfer' ? form.destinationId : null,
  };
}

// Ajustement de solde (Superviseur, cash.manage, décision du 21 octobre 2026) : différence signée choisie en mots
// (« augmenter » / « baisser » le solde), motif obligatoire, contre « Écarts et ajustements ».

export type AdjustmentDirection = 'up' | 'down';

export interface AdjustmentForm {
  target: AdjustmentTarget;
  targetId: string | null;
  direction: AdjustmentDirection;
  amount: string;
  unit: InputUnit;
  reason: string;
  operationAt: string;
}

export type AdjustmentErrors = Partial<Record<'target' | 'amount' | 'reason' | 'operationAt', string>>;

export function validateAdjustment(form: AdjustmentForm, now: Date = new Date()): AdjustmentErrors {
  const errors: AdjustmentErrors = {};
  if (!form.targetId) errors.target = form.target === 'Party' ? 'cash.adjustment.errors.partyRequired' : 'cash.form.errors.accountRequired';
  const amount = parseMoneyInput(form.amount, form.unit);
  if (!amount.ok) errors.amount = amount.error === 'tooPrecise' ? `cash.form.errors.tooPrecise${form.unit}` : `cash.form.errors.amount.${amount.error}`;
  if (!form.reason.trim()) errors.reason = 'cash.adjustment.errors.reasonRequired';
  if (form.operationAt) {
    const iso = operationAtIso(form.operationAt);
    if (iso === null) errors.operationAt = 'cash.form.errors.dateInvalid';
    else if (new Date(iso) > now) errors.operationAt = 'cash.form.errors.futureDate';
  }
  return errors;
}

/** Montant signé envoyé : positif pour augmenter le solde, négatif pour le baisser ; null si la saisie n'est pas valable. */
export function signedAdjustment(form: Pick<AdjustmentForm, 'direction' | 'amount' | 'unit'>): number | null {
  const amount = parseMoneyInput(form.amount, form.unit);
  if (!amount.ok) return null;
  return form.direction === 'up' ? amount.value : -amount.value;
}

export function buildAdjustmentRequest(form: AdjustmentForm, agencyId: string): CreateBalanceAdjustmentRequest {
  const amount = signedAdjustment(form);
  if (amount === null || !form.targetId) throw new Error('Formulaire non validé.');
  return {
    agencyId,
    target: form.target,
    targetId: form.targetId,
    amount,
    reason: form.reason.trim(),
    operationAt: operationAtIso(form.operationAt),
  };
}

/** Nouveau solde du compte s'il passe sous zéro par ce mouvement ; null sinon ou si le solde n'est pas connu. */
export function overdraftAfter(type: CashMovementType, balance: number | null, amount: number | null): number | null {
  if (!leavesAccount(type) || balance === null || amount === null) return null;
  const after = (Math.round(balance * 100) - Math.round(amount * 100)) / 100;
  return after < 0 ? after : null;
}
