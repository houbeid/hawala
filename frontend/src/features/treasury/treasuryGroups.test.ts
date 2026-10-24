import { describe, expect, it } from 'vitest';
import { agencyTreasury } from './treasuryGroups';
import type { TreasuryAccount } from './types';

const NKC = { id: 'nkc', currency: 'MRO' as const };
const DKR = { id: 'dkr', currency: 'XOF' as const };

function account(overrides: Partial<TreasuryAccount>): TreasuryAccount {
  return { id: 'x', name: 'Compte', type: 'Cash', agencyId: 'nkc', currency: 'MRO', isActive: true, balance: 0, ...overrides };
}

describe('agencyTreasury', () => {
  it('sépare caisses et banques, actifs d\'abord puis par nom', () => {
    const nkc = agencyTreasury(
      [
        account({ id: '1', name: 'Sedad', type: 'Wallet', isActive: false }),
        account({ id: '2', name: 'Bankily', type: 'Wallet' }),
        account({ id: '3', name: 'Banque fictive', type: 'Bank' }),
        account({ id: '4', name: 'Caisse principale' }),
      ],
      NKC,
    );
    expect(nkc.cash.map((a) => a.id)).toEqual(['4']);
    expect(nkc.banks.map((a) => a.id)).toEqual(['2', '3', '1']);
  });

  it('ne compte que les comptes actifs de chaque agence, sans mélanger les devises', () => {
    const accounts = [
      account({ balance: 1_850_000.1 }),
      account({ type: 'Bank', balance: 350_000.2 }),
      account({ type: 'Bank', balance: 999, isActive: false }),
      account({ agencyId: 'dkr', currency: 'XOF', balance: 4_400_000 }),
    ];
    expect(agencyTreasury(accounts, NKC).activeTotal).toBe(2_200_000.3);
    expect(agencyTreasury(accounts, DKR).activeTotal).toBe(4_400_000);
  });

  it('ne donne pas de total quand un solde est masqué', () => {
    const nkc = agencyTreasury([account({ balance: null })], NKC);
    expect(nkc.activeTotal).toBeNull();
  });

  it('garde une section vide pour une agence sans compte', () => {
    expect(agencyTreasury([account({})], DKR)).toMatchObject({ agencyId: 'dkr', cash: [], banks: [], activeTotal: 0 });
  });
});
