// Formatage des montants et des taux, règles des maquettes et du skill métier :
// chiffres occidentaux (même en arabe), groupes de trois séparés par une espace fine
// insécable, virgule décimale, unité toujours écrite. Le MRO est un code interne
// (pas ISO) : on ne délègue jamais l'unité à Intl.

export type Currency = 'MRO' | 'XOF';

/** Unité affichée : XOF s'écrit « FCFA », MRU ne sert qu'à la saisie. */
export type DisplayUnit = 'MRO' | 'FCFA' | 'MRU';

/** Espace fine insécable (U+202F) entre les groupes de chiffres. */
export const GROUP_SEPARATOR = ' ';
/** Espace insécable (U+00A0) entre le nombre et l'unité, et après un signe. */
export const UNIT_SPACE = ' ';
/** Vrai signe moins (U+2212), plus lisible que le tiret. */
export const MINUS = '−';

export function unitOf(currency: Currency): DisplayUnit {
  return currency === 'XOF' ? 'FCFA' : 'MRO';
}

/** Décimales au plus : FCFA toujours entier, MRO au plus 2 (règles d'arrondi validées). */
export function maxDecimalsOf(currency: Currency): number {
  return currency === 'XOF' ? 0 : 2;
}

// fr-FR donne exactement la virgule décimale ; le séparateur de groupe est remplacé
// ensuite pour ne pas dépendre de la version d'ICU (espace fine ou insécable selon les moteurs).
// Intl arrondit en « halfExpand » (0,5 s'éloigne de zéro), comme MidpointRounding.AwayFromZero.
const formatters = new Map<string, Intl.NumberFormat>();

function formatterFor(minDecimals: number, maxDecimals: number): Intl.NumberFormat {
  const key = `${minDecimals}:${maxDecimals}`;
  let formatter = formatters.get(key);
  if (!formatter) {
    formatter = new Intl.NumberFormat('fr-FR', {
      numberingSystem: 'latn',
      useGrouping: true,
      minimumFractionDigits: minDecimals,
      maximumFractionDigits: maxDecimals,
    });
    formatters.set(key, formatter);
  }
  return formatter;
}

/**
 * Nombre seul, sans signe ni unité (valeur absolue). Les décimales nulles disparaissent :
 * 1 850 000 et non 1 850 000,00 ; 123,4 s'écrit 123,40.
 */
export function formatNumber(value: number, maxDecimals = 2): string {
  const abs = Math.abs(value);
  const rounded = formatterFor(0, maxDecimals).format(abs);
  const hasFraction = maxDecimals > 0 && rounded.includes(',');
  const text = hasFraction ? formatterFor(maxDecimals, maxDecimals).format(abs) : rounded;
  return text.replace(/\s/g, GROUP_SEPARATOR);
}

export interface AmountOptions {
  /** Écrit « + » devant un montant positif (soldes, résultats, bénéfices). */
  signed?: boolean;
  /** Décimales au plus ; par défaut celles de la devise. Mettre 0 pour un total affiché arrondi. */
  maxDecimals?: number;
}

function signOf(value: number, signed: boolean): string {
  if (value < 0) return MINUS + UNIT_SPACE;
  if (signed && value > 0) return '+' + UNIT_SPACE;
  return '';
}

/** Montant avec son unité : « 1 850 000 MRO », « − 1 200 000 FCFA », « + 300 000 MRO ». */
export function formatAmount(value: number, currency: Currency, options: AmountOptions = {}): string {
  const maxDecimals = options.maxDecimals ?? maxDecimalsOf(currency);
  const number = formatNumber(value, maxDecimals);
  // Un montant qui s'arrondit à zéro n'a pas de signe (« 0 MRO », jamais « − 0 MRO »).
  const sign = number === '0' ? '' : signOf(value, options.signed ?? false);
  return `${sign}${number}${UNIT_SPACE}${unitOf(currency)}`;
}

/** Taux au format de toute l'app : « 5 000 FCFA = 3 604,77 MRO » (2 décimales au plus). */
export function formatRate(mroPer5000Fcfa: number): string {
  return `${formatNumber(5000, 0)}${UNIT_SPACE}FCFA = ${formatNumber(mroPer5000Fcfa, 2)}${UNIT_SPACE}MRO`;
}

/** Pourcentage signé, deux décimales au plus : « + 8,57 % », « − 0,16 % ». */
export function formatPercent(value: number, options: { signed?: boolean } = {}): string {
  const number = formatNumber(value, 2);
  const sign = number === '0' ? '' : signOf(value, options.signed ?? true);
  return `${sign}${number}${UNIT_SPACE}%`;
}
