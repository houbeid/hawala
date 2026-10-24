import { describe, expect, it } from 'vitest';
import {
  buildAdjustmentRequest,
  buildCashRequest,
  overdraftAfter,
  parseCashSearch,
  parseNewMovementSearch,
  validateAdjustment,
  validateCashForm,
  type AdjustmentForm,
  type CashForm,
} from './cashRules';

const now = new Date('2026-10-24T12:00:00Z');

function form(overrides: Partial<CashForm> = {}): CashForm {
  return { type: 'Receipt', accountId: 'cash', destinationId: '', partyId: 'p-1', amount: '300000', unit: 'MRO', note: '', operationAt: '', ...overrides };
}

describe('parseCashSearch', () => {
  it('garde les valeurs connues et ignore les autres', () => {
    expect(parseCashSearch({ agencyId: 'a', from: '2026-10-01', to: '2026-10-24', type: 'Payment', account: 'c', search: ' loyer ', page: '2' })).toEqual({
      agencyId: 'a',
      from: '2026-10-01',
      to: '2026-10-24',
      type: 'Payment',
      account: 'c',
      search: 'loyer',
      page: 2,
    });
    expect(parseCashSearch({ from: '24/10/2026', type: 'Gift', page: '1', search: ' ' })).toEqual({});
    expect(parseNewMovementSearch({ type: 'InternalTransfer' })).toEqual({ type: 'InternalTransfer' });
    expect(parseNewMovementSearch({ type: 'Other' })).toEqual({});
  });
});

describe('validateCashForm', () => {
  it('un encaissement exige un compte, un tiers et un montant', () => {
    expect(validateCashForm(form(), now)).toEqual({});
    expect(validateCashForm(form({ accountId: '', partyId: null, amount: '' }), now)).toEqual({
      account: 'cash.form.errors.accountRequired',
      party: 'cash.form.errors.partyRequired',
      amount: 'cash.form.errors.amount.empty',
    });
  });

  it('un approvisionnement se fait sans tiers', () => {
    expect(validateCashForm(form({ type: 'OwnerContribution', partyId: null }), now)).toEqual({});
  });

  it('un virement interne va vers un autre compte', () => {
    expect(validateCashForm(form({ type: 'InternalTransfer', partyId: null }), now)).toEqual({ destination: 'cash.form.errors.destinationRequired' });
    expect(validateCashForm(form({ type: 'InternalTransfer', partyId: null, destinationId: 'cash' }), now)).toEqual({
      destination: 'cash.form.errors.sameAccount',
    });
  });

  it('refuse un montant trop précis selon l’unité, zéro et une date future', () => {
    expect(validateCashForm(form({ amount: '10,5', unit: 'XOF' }), now).amount).toBe('cash.form.errors.tooPreciseXOF');
    expect(validateCashForm(form({ amount: '1,2345', unit: 'MRU' }), now).amount).toBe('cash.form.errors.tooPreciseMRU');
    expect(validateCashForm(form({ amount: '0' }), now).amount).toBe('cash.form.errors.amount.notPositive');
    expect(validateCashForm(form({ operationAt: '2026-10-25T09:00' }), now).operationAt).toBe('cash.form.errors.futureDate');
  });
});

describe('buildCashRequest', () => {
  it('envoie le montant déjà converti en MRO, sans unité, et retire ce qui ne sert pas au type', () => {
    expect(buildCashRequest(form({ amount: '12,345', unit: 'MRU', note: '  Loyer  ' }), null)).toEqual({
      agencyId: null,
      type: 'Receipt',
      treasuryAccountId: 'cash',
      partyId: 'p-1',
      amount: 123.45,
      unit: null,
      note: 'Loyer',
      operationAt: null,
      destinationTreasuryAccountId: null,
    });
    expect(buildCashRequest(form({ type: 'InternalTransfer', destinationId: 'bank', partyId: 'p-1' }), 'nkc')).toMatchObject({
      agencyId: 'nkc',
      partyId: null,
      destinationTreasuryAccountId: 'bank',
    });
  });
});

describe('overdraftAfter', () => {
  it('avertit seulement quand l’argent sort et que le compte passe sous zéro', () => {
    expect(overdraftAfter('Payment', 100_000, 145_000)).toBe(-45_000);
    expect(overdraftAfter('Payment', 100_000, 100_000)).toBeNull();
    expect(overdraftAfter('Receipt', 0, 500)).toBeNull();
    expect(overdraftAfter('InternalTransfer', 10.1, 10.2)).toBe(-0.1);
    expect(overdraftAfter('OwnerWithdrawal', null, 5)).toBeNull();
  });
});

describe('ajustement de solde', () => {
  function adjustment(overrides: Partial<AdjustmentForm> = {}): AdjustmentForm {
    return { target: 'TreasuryAccount', targetId: 'cash', direction: 'down', amount: '2500,5', unit: 'MRO', reason: ' Écart de comptage ', operationAt: '', ...overrides };
  }

  it('exige une cible, un montant non nul et un motif', () => {
    expect(validateAdjustment(adjustment(), now)).toEqual({});
    expect(validateAdjustment(adjustment({ target: 'Party', targetId: null, amount: '0', reason: ' ' }), now)).toEqual({
      target: 'cash.adjustment.errors.partyRequired',
      amount: 'cash.form.errors.amount.notPositive',
      reason: 'cash.adjustment.errors.reasonRequired',
    });
  });

  it('envoie une différence signée selon le sens choisi', () => {
    expect(buildAdjustmentRequest(adjustment(), 'nkc')).toEqual({
      agencyId: 'nkc',
      target: 'TreasuryAccount',
      targetId: 'cash',
      amount: -2500.5,
      reason: 'Écart de comptage',
      operationAt: null,
    });
    expect(buildAdjustmentRequest(adjustment({ direction: 'up', amount: '100', unit: 'MRU' }), 'nkc').amount).toBe(1000);
  });
});
