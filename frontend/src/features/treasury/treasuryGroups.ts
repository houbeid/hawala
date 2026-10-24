import type { Currency } from '@/features/organization/types';
import type { TreasuryAccount } from './types';

// Mise en page de la maquette 14 : une section par agence, puis « Caisses » (Cash) et « Banques et wallets »
// (Bank, Wallet) ; actifs d'abord, puis par nom ; un compte désactivé reste visible. Le total d'une agence
// ne compte que ses comptes actifs et ne mélange jamais deux agences (deux devises).

export interface AgencyTreasury {
  agencyId: string;
  currency: Currency;
  cash: TreasuryAccount[];
  banks: TreasuryAccount[];
  /** Somme des soldes des comptes actifs ; null si un solde n'est pas visible (sans cash.budget.read). */
  activeTotal: number | null;
}

function byStatusThenName(a: TreasuryAccount, b: TreasuryAccount): number {
  if (a.isActive !== b.isActive) return a.isActive ? -1 : 1;
  return a.name.localeCompare(b.name, 'fr', { sensitivity: 'base' });
}

/** Additionne en centimes pour ne pas accumuler d'erreur de virgule flottante (MRO à 2 décimales). */
function sumBalances(accounts: TreasuryAccount[]): number | null {
  let cents = 0;
  for (const account of accounts) {
    if (account.balance === null) return null;
    cents += Math.round(account.balance * 100);
  }
  return cents / 100;
}

/** Les comptes d'une agence, rangés pour l'écran ; une agence sans compte donne des listes vides et un total nul. */
export function agencyTreasury(accounts: TreasuryAccount[], agency: { id: string; currency: Currency }): AgencyTreasury {
  const own = accounts.filter((a) => a.agencyId === agency.id).sort(byStatusThenName);
  return {
    agencyId: agency.id,
    currency: agency.currency,
    cash: own.filter((a) => a.type === 'Cash'),
    banks: own.filter((a) => a.type !== 'Cash'),
    activeTotal: sumBalances(own.filter((a) => a.isActive)),
  };
}
