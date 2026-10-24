import { describe, expect, it } from 'vitest';
import { formatAmount, formatRate } from '@/shared/format/money';
import { alertDetail, groupAlerts, snapshotOf } from './budgetRules';
import type { CashBudgetSnapshot, CoherenceAlert } from './types';

function alert(overrides: Partial<CoherenceAlert>): CoherenceAlert {
  return { kind: 'NegativeBalance', at: '2026-10-24T10:00:00Z', sourceId: null, label: 'Wallet fictif', amount: -45_000, reason: null, ...overrides };
}

describe('groupAlerts', () => {
  it('regroupe par famille, dans l’ordre de la maquette, sans famille vide', () => {
    const groups = groupAlerts([alert({}), alert({ kind: 'UnusualRate' }), alert({}), alert({ kind: 'BalanceAdjustment' })]);
    expect(groups.map((g) => [g.kind, g.alerts.length])).toEqual([
      ['UnusualRate', 1],
      ['BalanceAdjustment', 1],
      ['NegativeBalance', 2],
    ]);
  });
});

describe('alertDetail', () => {
  it('garde la référence ou le nom du libellé du serveur et met en forme montants et taux', () => {
    expect(alertDetail(alert({ kind: 'UnusualRate', label: 'TR-NKC-20261021-000040 : 3900 pour 3650 le jour même', amount: 3900 }), 'MRO')).toEqual({
      subject: 'TR-NKC-20261021-000040',
      value: formatRate(3900),
      reason: null,
    });
    expect(alertDetail(alert({ kind: 'OpeningBalanceChanged', label: 'Solde initial : Boutique Fictive', amount: 1000 }), 'MRO')).toMatchObject({
      subject: 'Boutique Fictive',
      value: formatAmount(1000, 'MRO', { signed: true }),
    });
    expect(alertDetail(alert({ kind: 'BalanceAdjustment', label: 'Ajustement (Party)', amount: -2500, reason: 'Écart' }), 'XOF')).toEqual({
      subject: null,
      value: formatAmount(-2500, 'XOF', { signed: true }),
      reason: 'Écart',
    });
    expect(alertDetail(alert({}), 'MRO')).toEqual({ subject: 'Wallet fictif', value: formatAmount(-45_000, 'MRO'), reason: null });
    expect(alertDetail(alert({ kind: 'DeliveredWithoutCode', label: 'TR-DKR-1', amount: null, reason: 'Code perdu' }), 'MRO')).toEqual({
      subject: 'TR-DKR-1',
      value: null,
      reason: 'Code perdu',
    });
  });
});

describe('snapshotOf', () => {
  it('trouve le budget enregistré du jour', () => {
    const snapshot = { date: '2026-10-24' } as CashBudgetSnapshot;
    expect(snapshotOf([{ date: '2026-10-23' } as CashBudgetSnapshot, snapshot], '2026-10-24')).toBe(snapshot);
    expect(snapshotOf([], '2026-10-24')).toBeNull();
  });
});
