import { dkr, nkc } from '@/test/fixtures';
import type { Transfer } from '../types';
import { buildRequest, compute, initialForm, saleRateValue } from './editAmounts';

const transfer = {
  id: 't1',
  version: 3,
  status: 'NotDelivered',
  sourceAgencyId: nkc.id,
  sourceCurrency: 'MRO',
  destinationAgencyId: dkr.id,
  destinationCurrency: 'XOF',
  amountXof: 1250000,
  amountMro: 912500,
  saleRate: 3650,
  senderInfo: 'E',
  beneficiaryInfo: 'B',
  note: null,
  payments: [{ treasuryAccountId: 'acc-cash', treasuryAccountName: 'Caisse', amount: 912500, currency: 'MRO' }],
  credit: null,
} as unknown as Transfer;

describe('modification d’argent', () => {
  it('part de l’état actuel, montant dans la devise de l’agence d’envoi', () => {
    const form = initialForm(transfer);
    expect(form.amount).toBe('912500');
    expect(form.amountUnit).toBe('MRO');
    expect(form.payments).toEqual([{ accountId: 'acc-cash', amount: '912500', unit: 'MRO' }]);
  });

  it('recalcule au nouveau prix de vente : le MRO tapé est gardé, le FCFA suit', () => {
    expect(compute({ amount: '912 500', amountUnit: 'MRO' }, 3640, transfer)).toEqual({
      amountXof: 1253434,
      amountMro: 912500,
      toPay: 912500,
    });
    expect(compute({ amount: '1 250 000', amountUnit: 'XOF' }, 3640, transfer)).toEqual({
      amountXof: 1250000,
      amountMro: 910000,
      toPay: 910000,
    });
  });

  it('refuse un prix de vente à 3 décimales', () => {
    expect(saleRateValue('3640,555')).toBeNull();
    expect(saleRateValue('3 640,5')).toBe(3640.5);
  });

  it('n’envoie que ce qui change pour le montant, le prix et la date, mais toujours la répartition', () => {
    const form = initialForm(transfer);
    expect(buildRequest(transfer, form, { amountXof: 1250000, saleRate: 3640, creditPartyId: null, reason: 'Taux négocié' })).toEqual({
      version: 3,
      amountXof: null,
      saleRate: 3640,
      operationAt: null,
      payments: [{ treasuryAccountId: 'acc-cash', amount: 912500 }],
      creditPartyId: null,
      reason: 'Taux négocié',
    });
  });
});
