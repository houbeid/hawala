// Comptes de trésorerie (caisses, banques, wallets), copiés sur
// Treasury.Application/Features/Accounts/TreasuryAccountSummary.cs.
import type { Currency } from '@/features/organization/types';

export type TreasuryAccountType = 'Cash' | 'Bank' | 'Wallet';

export interface TreasuryAccount {
  id: string;
  name: string;
  type: TreasuryAccountType;
  agencyId: string;
  currency: Currency;
  isActive: boolean;
  /** Null sans la permission cash.budget.read. */
  balance: number | null;
}
