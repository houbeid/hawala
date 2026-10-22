import { amountXofValue, stepForError, validateAmountStep, validatePaymentStep, validatePeopleStep, type CreateForm } from './createForm';

const base: CreateForm = {
  sourceAgencyId: 'nkc',
  amount: '1 250 000',
  amountUnit: 'XOF',
  operationAt: '',
  payments: [{ accountId: 'cash', amount: '912 500', unit: 'MRO' }],
  senderInfo: 'Expéditeur',
  beneficiaryInfo: 'Bénéficiaire',
  note: '',
};

describe('étape 1 · montant', () => {
  it('accepte un FCFA entier, maintenant par défaut', () => {
    expect(validateAmountStep(base)).toEqual({});
  });

  it('refuse un montant à décimales (FCFA ou MRO), vide ou nul', () => {
    expect(validateAmountStep({ ...base, amount: '1000,5' }).amount).toBe('transfers.create.validation.amount.tooPreciseXOF');
    expect(validateAmountStep({ ...base, amount: '1000,5', amountUnit: 'MRO' }).amount).toBe('transfers.create.validation.amount.tooPreciseMRO');
    expect(validateAmountStep({ ...base, amount: '' }).amount).toBe('transfers.create.validation.amount.empty');
    expect(validateAmountStep({ ...base, amount: '0' }).amount).toBe('transfers.create.validation.amount.notPositive');
  });

  it('déduit le FCFA de référence d’un montant saisi en MRO, avec le taux', () => {
    expect(amountXofValue({ amount: '912 500', amountUnit: 'MRO' }, 3700)).toBe(1233108);
    expect(amountXofValue({ amount: '912 500', amountUnit: 'MRO' }, null)).toBeNull();
    expect(amountXofValue({ amount: '1 250 000', amountUnit: 'XOF' }, null)).toBe(1250000);
  });

  it('refuse une date future', () => {
    const now = new Date('2026-10-22T10:00:00Z');
    expect(validateAmountStep({ ...base, operationAt: '2026-10-23T09:00' }, now).operationAt).toBe('transfers.create.validation.dateFuture');
  });
});

describe('étape 2 · paiement', () => {
  it('exige que le total réparti égale le montant à payer, sans crédit', () => {
    expect(validatePaymentStep(base, 912500, false)).toEqual({});
    const short = { ...base, payments: [{ accountId: 'cash', amount: '800 000', unit: 'MRO' as const }] };
    expect(validatePaymentStep(short, 912500, false).payments).toBe('transfers.create.validation.totalMismatch');
  });

  it('accepte une répartition en MRU convertie exactement', () => {
    const split = {
      ...base,
      payments: [
        { accountId: 'cash', amount: '612 500', unit: 'MRO' as const },
        { accountId: 'bankily', amount: '30 000', unit: 'MRU' as const },
      ],
    };
    expect(validatePaymentStep(split, 912500, false)).toEqual({});
  });

  it('refuse un compte en double et un montant trop précis', () => {
    const errors = validatePaymentStep(
      {
        ...base,
        payments: [
          { accountId: 'cash', amount: '500 000', unit: 'MRO' },
          { accountId: 'cash', amount: '1,234', unit: 'MRO' },
        ],
      },
      912500,
      false,
    );
    expect(errors['payments.1.accountId']).toBe('transfers.create.validation.accountDuplicated');
    expect(errors['payments.1.amount']).toBe('transfers.create.validation.payment.tooPrecise');
  });

  it('refuse de payer plus que le montant', () => {
    const over = { ...base, payments: [{ accountId: 'cash', amount: '1 000 000', unit: 'MRO' as const }] };
    expect(validatePaymentStep(over, 912500, false).payments).toBe('transfers.create.validation.overpaid');
  });
});

describe('étape 3 · personnes', () => {
  it('exige expéditeur et bénéficiaire', () => {
    expect(validatePeopleStep({ ...base, senderInfo: ' ', beneficiaryInfo: '' })).toEqual({
      senderInfo: 'transfers.create.validation.senderRequired',
      beneficiaryInfo: 'transfers.create.validation.beneficiaryRequired',
    });
  });
});

describe('refus du serveur : étape où revenir', () => {
  it('ramène au paiement, aux personnes ou au montant', () => {
    expect(stepForError('Transfers.Payment.TotalMismatch')).toBe(2);
    expect(stepForError('Transfers.Credit.LimitExceeded')).toBe(2);
    expect(stepForError('Transfers.Transfer.BeneficiaryRequired')).toBe(3);
    expect(stepForError('Transfers.Limits.PerTransferExceeded')).toBe(1);
    expect(stepForError('Transfers.Transfer.RateNotSet')).toBe(1);
  });
});
