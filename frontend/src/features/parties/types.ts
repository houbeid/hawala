// Contrats du module Tiers, copiés sur le backend :
// - PartySummary, PartyAgencyBalance, PartyList, PartyTotals : Parties.Application/Features/Parties/PartySummary.cs
// - CreatePartyRequest, UpdatePartyRequest, SetOpeningBalanceRequest, SetCreditLimitRequest :
//   Hawala.Api/Endpoints/Parties/PartyEndpoints.cs (création : réponse 201 { id } ; le reste : 204)
// - PartyType : Parties.Domain/Parties/PartyType.cs (type = compte comptable)
// - filtres de la liste : Parties.Application/Features/Parties/ListParties/ListPartiesQuery.cs
// - PartyStatement, PartyStatementLine : Parties.Application/Features/Parties/GetPartyStatement/GetPartyStatementQuery.cs
import type { Currency } from '@/features/organization/types';

export type PartyType = 'Client' | 'Supplier' | 'Staff' | 'Other';
export const PARTY_TYPES: readonly PartyType[] = ['Client', 'Supplier', 'Staff', 'Other'];

/** Solde d'un tiers dans une agence : débit − crédit ; positif = rouge (il nous doit), négatif = vert (on lui doit). */
export interface PartyAgencyBalance {
  agencyId: string;
  currency: Currency;
  balance: number;
  /** 0 = aucun crédit ; null = pas de plafond. */
  creditLimit: number | null;
  /** Solde initial retenu dans cette agence (même sens que le solde) ; ajouté le 22 octobre 2026 (décision A). */
  openingBalance: number;
}

export interface PartySummary {
  id: string;
  name: string;
  type: PartyType;
  accountCode: string | null;
  phone: string | null;
  nni: string | null;
  address: string | null;
  note: string | null;
  inBalanceSheet: boolean;
  isArchived: boolean;
  /** Null sans parties.view_balance ; seulement les agences du périmètre. */
  balances: PartyAgencyBalance[] | null;
}

export interface PartyTotals {
  currency: Currency;
  red: number;
  green: number;
  net: number;
}

export interface PartyList {
  items: PartySummary[];
  count: number;
  totalCount: number;
  totals: PartyTotals[] | null;
}

export interface CreatePartyRequest {
  name: string;
  type: PartyType;
  phone: string | null;
  nni: string | null;
  address: string | null;
  note: string | null;
}

/** « Dans le bilan » : All (défaut), In, Out. */
export type BalanceSheetFilter = 'All' | 'In' | 'Out';
export const BALANCE_SHEET_FILTERS: readonly BalanceSheetFilter[] = ['All', 'In', 'Out'];

/** Solde sur une agence : All (défaut), Red (عليه, il nous doit), Green (له, on lui doit). Exige une agence. */
export type BalanceFilter = 'All' | 'Red' | 'Green';
export const BALANCE_FILTERS: readonly BalanceFilter[] = ['All', 'Red', 'Green'];

/** GET /parties : paramètres de la requête. */
export type PartyListParams = {
  search?: string;
  type?: string;
  balanceSheet?: BalanceSheetFilter;
  balance?: BalanceFilter;
  includeArchived?: boolean;
  agencyId?: string;
};

/** Une écriture du relevé. Débit : augmente ce qu'il nous doit ; crédit : augmente ce qu'on lui doit. */
export interface PartyStatementLine {
  operationDate: string;
  recordedAt: string;
  /** Type d'écriture stable (Parties.OpeningBalance, Transfers.Created, Cash.Receipt…), traduit par l'interface. */
  entryType: string;
  /** Libellé écrit par le serveur au grand livre (en français). */
  label: string;
  reason: string | null;
  isReversal: boolean;
  debit: number;
  credit: number;
  balanceAfter: number;
}

/** GET /parties/{id}/statement (parties.view_balance) : soldes au sens débit − crédit (positif = il nous doit). */
export interface PartyStatement {
  partyId: string;
  partyName: string;
  agencyId: string;
  currency: Currency;
  from: string;
  to: string;
  openingBalance: number;
  lines: PartyStatementLine[];
  closingBalance: number;
}

/** PUT /parties/{id} (parties.manage) : ÉTAT COMPLET de la fiche ; un champ facultatif absent est retiré. */
export interface UpdatePartyRequest {
  name: string;
  type: PartyType;
  phone: string | null;
  nni: string | null;
  address: string | null;
  note: string | null;
  inBalanceSheet: boolean;
}

/** PUT /parties/{id}/agencies/{agencyId}/opening-balance : NOUVEAU solde initial (le serveur écrit la différence). */
export interface SetOpeningBalanceRequest {
  /** Devise de l'agence ; positif = il nous doit, négatif = on lui doit. */
  amount: number;
  /** aaaa-mm-jj ; null = aujourd'hui. Jamais dans le futur. */
  operationDate: string | null;
}

/** PUT /parties/{id}/agencies/{agencyId}/credit-limit : positif ou nul ; 0 = aucun crédit ; null = pas de plafond. */
export interface SetCreditLimitRequest {
  creditLimit: number | null;
}

/** Longueurs au plus (Parties.Domain) : nom 30, NNI 20, adresse 200, note 500. */
export const PARTY_LIMITS = { name: 30, nni: 20, address: 200, note: 500 } as const;
