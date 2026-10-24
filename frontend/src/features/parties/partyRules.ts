import { isIsoDay } from '@/shared/format/dates';
import { parseMoneyInput, type InputUnit, type MoneyInputResult } from '@/shared/format/moneyInput';
import { BALANCE_FILTERS, BALANCE_SHEET_FILTERS, PARTY_TYPES, type BalanceFilter, type BalanceSheetFilter, type PartyType } from './types';

// Règles d'affichage des tiers (skill métier, « Tiers ») :
// - solde débit − crédit : positif = rouge (il nous doit), négatif = vert (on lui doit), zéro = ni l'un ni l'autre ;
// - plafond de crédit par agence : null = pas de plafond, 0 = aucun crédit ; contrôle sur le solde (rouge) du tiers.

export type BalanceState = 'red' | 'green' | 'zero';

export function balanceState(balance: number): BalanceState {
  if (balance > 0) return 'red';
  if (balance < 0) return 'green';
  return 'zero';
}

export type CreditUsage =
  | { kind: 'noLimit' }
  | { kind: 'noCredit' }
  /** used = ce qu'il nous doit (0 s'il est vert) ; remaining négatif = plafond dépassé ; percent plafonné à 100 pour la barre. */
  | { kind: 'limit'; limit: number; used: number; remaining: number; percent: number };

export function creditUsage(balance: number, creditLimit: number | null): CreditUsage {
  if (creditLimit === null) return { kind: 'noLimit' };
  if (creditLimit === 0) return { kind: 'noCredit' };
  const used = Math.max(balance, 0);
  // En centimes : 1 500 000 − 1 200 000,10 ne doit pas donner 299 999,8999…
  const remaining = (Math.round(creditLimit * 100) - Math.round(used * 100)) / 100;
  const percent = Math.min(100, Math.round((used / creditLimit) * 100));
  return { kind: 'limit', limit: creditLimit, used, remaining, percent };
}

/** Deux premières lettres (une par mot) pour la pastille d'une ligne : « Boutique El Wafa » → « BE ». */
export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  return words
    .slice(0, 2)
    .map((w) => Array.from(w)[0] ?? '')
    .join('')
    .toLocaleUpperCase();
}

export interface PartySearch {
  search?: string;
  type?: PartyType;
  balanceSheet?: Exclude<BalanceSheetFilter, 'All'>;
  balance?: Exclude<BalanceFilter, 'All'>;
  archived?: true;
}

/** Filtres de la liste lus dans l'adresse (/parties?type=Client&balance=Red…). Une valeur inconnue est ignorée ; « Tous » ne s'écrit pas. */
export function parsePartySearch(search: Record<string, unknown>): PartySearch {
  const result: PartySearch = {};
  if (typeof search.search === 'string' && search.search.trim()) result.search = search.search.trim();
  if (PARTY_TYPES.includes(search.type as PartyType)) result.type = search.type as PartyType;
  if (BALANCE_SHEET_FILTERS.includes(search.balanceSheet as BalanceSheetFilter) && search.balanceSheet !== 'All') {
    result.balanceSheet = search.balanceSheet as 'In' | 'Out';
  }
  if (BALANCE_FILTERS.includes(search.balance as BalanceFilter) && search.balance !== 'All') result.balance = search.balance as 'Red' | 'Green';
  if (search.archived === true || search.archived === 'true') result.archived = true;
  return result;
}

/** Nombre de filtres actifs (la recherche n'en est pas un : elle a son propre champ). */
export function activeFilterCount(search: PartySearch): number {
  return (search.type ? 1 : 0) + (search.balanceSheet ? 1 : 0) + (search.balance ? 1 : 0) + (search.archived ? 1 : 0);
}

/** Relevé : agence et période lues dans l'adresse ; sans dates, le serveur prend le mois en cours. */
export interface StatementSearch {
  agencyId?: string;
  from?: string;
  to?: string;
}

export function parseStatementSearch(search: Record<string, unknown>): StatementSearch {
  const result: StatementSearch = {};
  if (typeof search.agencyId === 'string' && search.agencyId) result.agencyId = search.agencyId;
  if (isIsoDay(search.from)) result.from = search.from;
  if (isIsoDay(search.to)) result.to = search.to;
  return result;
}

/**
 * Montant d'un solde initial ou d'un plafond : mêmes règles que toute saisie (FCFA entier, MRO à 2 décimales,
 * MRU à 3 décimales × 10, jamais d'arrondi), mais ZÉRO est une valeur valable (solde initial remis à zéro,
 * plafond « aucun crédit »). Le sens (il nous doit / on lui doit) est choisi à part : le montant reste positif.
 */
export function parseAmountOrZero(raw: string, unit: InputUnit): MoneyInputResult {
  const parsed = parseMoneyInput(raw, unit);
  if (!parsed.ok && parsed.error === 'notPositive') return { ok: true, value: 0, text: '0' };
  return parsed;
}

/** Clé de traduction d'une erreur de saisie d'un montant (le message « trop précis » dépend de l'unité). */
export function amountErrorKey(error: 'empty' | 'invalid' | 'tooPrecise' | 'notPositive', unit: InputUnit): string {
  if (error === 'tooPrecise') return `parties.amount.tooPrecise${unit}`;
  return `parties.amount.${error === 'empty' ? 'empty' : 'invalid'}`;
}
