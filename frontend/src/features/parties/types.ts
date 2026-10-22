// Contrats du module Tiers, copiés sur le backend :
// - PartySummary, PartyAgencyBalance, PartyList, PartyTotals : Parties.Application/Features/Parties/PartySummary.cs
// - CreatePartyRequest : Hawala.Api/Endpoints/Parties/PartyEndpoints.cs (réponse 201 { id })
// - PartyType : Parties.Domain/Parties/PartyType.cs (type = compte comptable)
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
