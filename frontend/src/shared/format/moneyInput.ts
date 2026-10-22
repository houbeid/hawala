import type { Currency } from './money';
import { parseDecimalInput, type DecimalInputError } from './decimalInput';

// Saisie d'un montant (règles d'arrondi validées, skill « Arrondi ») :
// - FCFA : entier ;
// - MRO : 2 décimales au plus ;
// - MRU (saisie seulement, à Nouakchott) : 3 décimales au plus, converti en MRO × 10 EXACT
//   (12,345 MRU = 123,45 MRO), calculé sur le texte et jamais en virgule flottante ;
// - au-delà : refus, jamais d'arrondi.

export type InputUnit = 'MRO' | 'MRU' | 'XOF';

export type MoneyInputResult =
  | { ok: true; /** Montant dans la devise du compte (MRO ou FCFA). */ value: number; /** Forme canonique « 123.45 ». */ text: string }
  | { ok: false; error: DecimalInputError };

function maxDecimals(unit: InputUnit): number {
  if (unit === 'XOF') return 0;
  return unit === 'MRU' ? 3 : 2;
}

/** Multiplie par 10 un nombre écrit « 12.345 » en déplaçant la virgule : « 123.45 ». */
function timesTen(text: string): string {
  const [integer = '0', fraction = ''] = text.split('.');
  const shifted = `${integer}${fraction.slice(0, 1) || '0'}`.replace(/^0+(?=\d)/, '');
  const rest = fraction.slice(1);
  return rest ? `${shifted}.${rest}` : shifted;
}

export function parseMoneyInput(raw: string, unit: InputUnit): MoneyInputResult {
  const parsed = parseDecimalInput(raw, maxDecimals(unit));
  if (!parsed.ok) return parsed;
  if (unit !== 'MRU') return { ok: true, value: parsed.value, text: parsed.text };
  const text = timesTen(parsed.text);
  return { ok: true, value: Number(text), text };
}

/** Unité de saisie par défaut d'une devise de compte. */
export function defaultUnit(currency: Currency): InputUnit {
  return currency === 'XOF' ? 'XOF' : 'MRO';
}

/**
 * Montant MRO d'un transfert, comme le serveur (ExchangeRate.ToTransferMro) : FCFA × taux ÷ 5 000,
 * arrondi UNE fois à l'unité, 0,5 en s'éloignant de zéro. Calcul en entiers (taux en centimes) : exact.
 * Aperçu seulement : le montant enregistré est celui renvoyé par le serveur.
 */
export function transferMroPreview(amountXof: number, saleRate: number): number {
  const rateCents = Math.round(saleRate * 100);
  const product = amountXof * rateCents; // MRO × 500 000
  return Math.floor((product + 250_000) / 500_000);
}

/**
 * FCFA de référence d'un transfert saisi en MRO (décision du 22 octobre 2026) : MRO × 5 000 ÷ taux, arrondi au
 * FCFA le plus proche (0,5 s'éloigne de zéro). Calcul en entiers (taux en centimes) : exact. Comme 1 FCFA vaut
 * moins d'1 MRO (taux < 5 000), transferMroPreview(xofFromMro(m, t), t) === m : le serveur retrouve le MRO tapé.
 */
export function xofFromMro(amountMro: number, saleRate: number): number {
  const rateCents = Math.round(saleRate * 100);
  const numerator = amountMro * 500_000; // FCFA × taux en centimes
  return Math.floor((2 * numerator + rateCents) / (2 * rateCents));
}

/** Somme exacte de montants à 2 décimales au plus (évite 0,1 + 0,2 = 0,30000000000000004). */
export function sumAmounts(values: number[]): number {
  return values.reduce((total, value) => total + Math.round(value * 100), 0) / 100;
}
