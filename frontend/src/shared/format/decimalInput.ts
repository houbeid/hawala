// Lecture d'un nombre tapé par l'utilisateur (taux, montant). Règle validée : trop de décimales =
// REFUS, jamais d'arrondi (« un taux saisi avec plus de 2 décimales est refusé »). Le calcul se fait
// sur le texte, pas en virgule flottante, pour que « 3604,77 » reste exactement 3604,77.
//
// Accepté : chiffres, espaces de groupe (normales, insécables, fines), une virgule OU un point décimal.
// Refusé : signe, lettres, plusieurs séparateurs décimaux.

export type DecimalInputError = 'empty' | 'invalid' | 'tooPrecise' | 'notPositive';

export type DecimalInputResult =
  | { ok: true; /** Forme canonique « 3604.77 » (point, sans zéros de tête). */ text: string; value: number }
  | { ok: false; error: DecimalInputError };

export function parseDecimalInput(raw: string, maxDecimals: number): DecimalInputResult {
  const compact = raw.replace(/[\s\u00A0\u202F]/g, '');
  if (compact === '') return { ok: false, error: 'empty' };

  const match = /^(\d+)(?:[.,](\d*))?$/.exec(compact);
  if (!match) return { ok: false, error: 'invalid' };

  const integer = (match[1] ?? '').replace(/^0+(?=\d)/, '');
  const fraction = (match[2] ?? '').replace(/0+$/, '');
  if (fraction.length > maxDecimals) return { ok: false, error: 'tooPrecise' };

  const text = fraction ? `${integer}.${fraction}` : integer;
  const value = Number(text);
  if (!(value > 0)) return { ok: false, error: 'notPositive' };

  return { ok: true, text, value };
}

/** Valeur numérique → texte de champ (virgule décimale, sans groupes) : 3604.77 → « 3604,77 ». */
export function toInputText(value: number): string {
  return String(value).replace('.', ',');
}
