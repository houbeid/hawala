import type { TreasuryAccount } from '@/features/treasury/types';
import { overdraftWarnings, validateCode, validateRemise, validateWithoutCode } from './deliverForm';

const accounts: TreasuryAccount[] = [
  { id: 'cash', name: 'Espèces', type: 'Cash', agencyId: 'dkr', currency: 'XOF', isActive: true, balance: 800000 },
  { id: 'wave', name: 'Wave', type: 'Wallet', agencyId: 'dkr', currency: 'XOF', isActive: true, balance: null },
];

describe('livraison', () => {
  it('exige un code de 6 chiffres', () => {
    expect(validateCode('482917')).toBeNull();
    expect(validateCode('48291')).toBe('transfers.deliver.validation.code');
    expect(validateCode('48291a')).toBe('transfers.deliver.validation.code');
  });

  it('exige la pièce d’identité et le motif pour livrer sans code', () => {
    expect(validateWithoutCode({ idDocument: ' ', reason: '' })).toEqual({
      idDocument: 'transfers.deliver.validation.idDocument',
      reason: 'transfers.detail.reasonRequired',
    });
  });

  it('exige que le total remis égale le montant à livrer', () => {
    const lines = [
      { accountId: 'cash', amount: '1 000 000', unit: 'XOF' as const },
      { accountId: 'wave', amount: '250 000', unit: 'XOF' as const },
    ];
    expect(validateRemise(lines, 1250000)).toEqual({});
    expect(validateRemise(lines, 1300000).payments).toBe('transfers.deliver.validation.totalMismatch');
    expect(validateRemise([], 1250000).payments).toBe('transfers.deliver.validation.remiseRequired');
  });

  it('prévoit une caisse en négatif seulement si son solde est connu', () => {
    const lines = [
      { accountId: 'cash', amount: '1 250 000', unit: 'XOF' as const },
      { accountId: 'wave', amount: '10', unit: 'XOF' as const },
    ];
    expect(overdraftWarnings(lines, accounts)).toEqual([{ accountId: 'cash', name: 'Espèces', after: -450000 }]);
  });
});
