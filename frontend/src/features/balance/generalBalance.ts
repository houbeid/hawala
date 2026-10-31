import { parseDecimalInput } from '@/shared/format/decimalInput';
import type { BalanceComponents } from './types';

// Bilan général (décisions du 31 octobre 2026) : lecture du taux saisi et lignes de composants. Les calculs sont faits par
// le serveur, en pleine précision ; l'écran additionne seulement deux composants déjà renvoyés (trésorerie, résultat tiers).

/** ExchangeRate.MaxDecimals. */
export const BALANCE_RATE_DECIMALS = 2;

/** Taux du bilan saisi (« 5 000 FCFA = ? MRO ») ; null s'il est vide ou mal saisi. */
export function parseBalanceRate(text: string): number | null {
  const parsed = parseDecimalInput(text, BALANCE_RATE_DECIMALS);
  return parsed.ok ? parsed.value : null;
}

/** Clé de traduction de l'erreur du taux saisi, ou null s'il est valable. */
export function balanceRateError(text: string): string | null {
  const parsed = parseDecimalInput(text, BALANCE_RATE_DECIMALS);
  return parsed.ok ? null : `rates.validation.${parsed.error}`;
}

export type ComponentKey = 'cash' | 'banks' | 'debtors' | 'creditors' | 'undelivered';

/** Ordre du détail : ce qu'on a, ce qu'on nous doit, puis ce qu'on doit (le signe est écrit, jamais seulement coloré). */
export const COMPONENT_ROWS: { key: ComponentKey; sign: '+' | '−' }[] = [
  { key: 'cash', sign: '+' },
  { key: 'banks', sign: '+' },
  { key: 'debtors', sign: '+' },
  { key: 'creditors', sign: '−' },
  { key: 'undelivered', sign: '−' },
];

/** Trésorerie toutes caisses : espèces + banques et wallets. */
export function treasuryOf(c: BalanceComponents): number {
  return c.cash + c.banks;
}

/** Résultat tiers : créances − dettes. */
export function partiesResultOf(c: BalanceComponents): number {
  return c.debtors - c.creditors;
}
