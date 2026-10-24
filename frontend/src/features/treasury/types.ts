// Comptes de trésorerie (caisses, banques, wallets), copiés sur
// Treasury.Application/Features/Accounts/TreasuryAccountSummary.cs et
// Hawala.Api/Endpoints/Treasury/TreasuryEndpoints.cs (requêtes) et
// Treasury.Application/Features/Accounts/GetTreasuryAccountStatement/GetTreasuryAccountStatementQuery.cs (relevé).
import type { Currency } from '@/features/organization/types';

export type TreasuryAccountType = 'Cash' | 'Bank' | 'Wallet';

export const TREASURY_ACCOUNT_TYPES: readonly TreasuryAccountType[] = ['Cash', 'Bank', 'Wallet'];

/** Treasury.Domain : le nom d'un compte dépasse 50 caractères = Treasury.Account.NameTooLong. */
export const TREASURY_NAME_MAX = 50;

export interface TreasuryAccount {
  id: string;
  name: string;
  type: TreasuryAccountType;
  agencyId: string;
  currency: Currency;
  isActive: boolean;
  /** Null sans la permission cash.budget.read. */
  balance: number | null;
  /**
   * Solde de départ retenu (somme des écritures d'ouverture, lue au grand livre) : seulement sur la fiche, même
   * visibilité que le solde ; null (ou absent) dans la liste.
   */
  openingBalance?: number | null;
}

/** PUT /treasury-accounts/{id}/opening-balance (treasury.manage) : nouveau solde de départ, date facultative (aaaa-mm-jj). */
export interface SetTreasuryOpeningBalanceRequest {
  amount: number;
  operationDate: string | null;
}

/** POST /treasury-accounts (treasury.manage) : la devise découle de l'agence, jamais envoyée. */
export interface CreateTreasuryAccountRequest {
  name: string;
  type: TreasuryAccountType;
  agencyId: string;
}

/** Réponse 201 de la création. */
export interface CreatedTreasuryAccount {
  id: string;
}

/** PUT /treasury-accounts/{id} : renommer. */
export interface RenameTreasuryAccountRequest {
  name: string;
}

/** Une écriture du relevé. Débit = argent entré sur le compte ; crédit = argent sorti. */
export interface TreasuryStatementLine {
  operationDate: string;
  recordedAt: string;
  /** Type d'écriture stable (Transfers.Created, Cash.Receipt…), traduit par l'interface. */
  entryType: string;
  /** Libellé écrit en français au grand livre (référence d'un transfert…). */
  label: string;
  reason: string | null;
  isReversal: boolean;
  debit: number;
  credit: number;
  balanceAfter: number;
}

/** GET /treasury-accounts/{id}/statement (cash.budget.read) ; sans dates, le mois en cours. */
export interface TreasuryAccountStatement {
  accountId: string;
  accountName: string;
  type: TreasuryAccountType;
  agencyId: string;
  currency: Currency;
  isActive: boolean;
  from: string;
  to: string;
  openingBalance: number;
  lines: TreasuryStatementLine[];
  closingBalance: number;
}
