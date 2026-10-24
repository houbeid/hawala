import { parseMoneyInput, type InputUnit } from '@/shared/format/moneyInput';

// Solde de départ d'un compte de trésorerie (mise en service, phase 8) : montant tapé positif (zéro permis), « négatif »
// coché pour un découvert ; MRU converti × 10 sur le texte ; la différence avec le solde de départ actuel est montrée
// avant l'envoi, comme pour un tiers. Le serveur écrit seulement la différence.

export type OpeningResult = { ok: true; value: number } | { ok: false; error: 'empty' | 'invalid' | 'tooPrecise' };

export function parseOpening(raw: string, unit: InputUnit, negative: boolean): OpeningResult {
  const parsed = parseMoneyInput(raw, unit);
  if (!parsed.ok) {
    if (parsed.error === 'notPositive') return { ok: true, value: 0 };
    return { ok: false, error: parsed.error };
  }
  return { ok: true, value: negative ? -parsed.value : parsed.value };
}

/** Différence à écrire (au centime près, sans flottant parasite). */
export function openingDifference(target: number, current: number): number {
  return (Math.round(target * 100) - Math.round(current * 100)) / 100;
}
