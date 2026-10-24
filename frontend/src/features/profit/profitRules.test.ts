import { describe, expect, it } from 'vitest';
import { costAtPurchaseRate, parseProfitSearch, periodDays, presets } from './profitRules';

describe('règles du bénéfice', () => {
  it('lit la période dans l’adresse et ignore une date invalide', () => {
    expect(parseProfitSearch({ from: '2026-10-15', to: '2026-10-21' })).toEqual({ from: '2026-10-15', to: '2026-10-21' });
    expect(parseProfitSearch({ from: '15/10/2026', to: 3 })).toEqual({});
  });

  it('propose aujourd’hui, 7 et 30 derniers jours, mois en cours', () => {
    expect(presets('2026-10-24')).toEqual([
      { id: 'today', from: '2026-10-24', to: '2026-10-24' },
      { id: 'last7', from: '2026-10-18', to: '2026-10-24' },
      { id: 'last30', from: '2026-09-25', to: '2026-10-24' },
      { id: 'month', from: '2026-10-01', to: '2026-10-24' },
    ]);
    expect(periodDays('2026-10-18', '2026-10-24')).toBe(7);
  });

  it('calcule le coût au prix d’achat (exemple validé : 5 000 000 FCFA à 3 600 = 3 600 000 MRO)', () => {
    expect(costAtPurchaseRate(5_000_000, 3600)).toBe(3_600_000);
    expect(costAtPurchaseRate(1_500_000, 3600)).toBe(1_080_000);
  });
});
