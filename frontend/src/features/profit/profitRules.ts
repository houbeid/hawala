import { addDays, daysBetween, isIsoDay } from '@/shared/format/dates';

// Règles de l'écran « Bénéfice » (skill, « Phase 5, décisions du 21 octobre 2026 ») : Superviseur (profit.view),
// période sur la date d'opération (aujourd'hui par défaut), une agence ou les deux (bénéfice général), transferts non
// annulés (livrés ou non), pleine précision et seul l'affichage arrondit.

export type PresetId = 'today' | 'last7' | 'last30' | 'month';

export interface ProfitSearch {
  from?: string;
  to?: string;
}

/** Période lue dans l'adresse ; une date invalide est ignorée ; « aujourd'hui » ne s'écrit pas. */
export function parseProfitSearch(search: Record<string, unknown>): ProfitSearch {
  const result: ProfitSearch = {};
  if (isIsoDay(search.from)) result.from = search.from;
  if (isIsoDay(search.to)) result.to = search.to;
  return result;
}

export function presets(today: string): { id: PresetId; from: string; to: string }[] {
  return [
    { id: 'today', from: today, to: today },
    { id: 'last7', from: addDays(today, -6), to: today },
    { id: 'last30', from: addDays(today, -29), to: today },
    { id: 'month', from: `${today.slice(0, 8)}01`, to: today },
  ];
}

/** Nombre de jours de la période, bornes comprises. */
export function periodDays(from: string, to: string): number {
  return daysBetween(from, to) + 1;
}

/** Coût au prix d'achat d'un transfert : FCFA × prix d'achat ÷ 5 000 (MRO, pleine précision). */
export function costAtPurchaseRate(amountXof: number, purchaseRate: number): number {
  return (amountXof * purchaseRate) / 5000;
}
