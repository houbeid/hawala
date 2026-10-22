// Contrats de lecture des transferts, copiés sur le backend (JSON camelCase, énumérations en texte) :
// - TransferView, TransferList : Transfers.Application/Features/Transfers/TransferView.cs
// - TransferSummary, SummaryAmount : Transfers.Application/Features/Transfers/GetTransferSummary/GetTransferSummaryQuery.cs
// - TransferStatus : Transfers.Domain/Transfers/TransferPayment.cs
import type { Currency } from '@/features/organization/types';

export type TransferStatus = 'NotDelivered' | 'Delivered' | 'Cancelled';
export const TRANSFER_STATUSES: readonly TransferStatus[] = ['NotDelivered', 'Delivered', 'Cancelled'];

/** Envoyé ou reçu, vu depuis l'agence du compte (ou celle choisie par le Superviseur). */
export type TransferDirection = 'Sent' | 'Received';
export const TRANSFER_DIRECTIONS: readonly TransferDirection[] = ['Sent', 'Received'];

export interface TransferPayment {
  treasuryAccountId: string;
  treasuryAccountName: string | null;
  amount: number;
  currency: Currency;
}

export interface TransferCredit {
  partyId: string;
  partyName: string | null;
  amount: number;
  currency: Currency;
}

export interface Transfer {
  id: string;
  reference: string;
  status: TransferStatus;
  /** Null quand aucune agence n'est choisie (Superviseur sur « les deux agences »). */
  direction: TransferDirection | null;
  sourceAgencyId: string;
  sourceCurrency: Currency;
  destinationAgencyId: string;
  destinationCurrency: Currency;
  /** FCFA, entier. */
  amountXof: number;
  /** MRO, arrondi une fois à la création. */
  amountMro: number;
  saleRate: number;
  /** Null sans profit.view. */
  purchaseRate: number | null;
  /** Pleine précision ; null sans profit.view. */
  profitMro: number | null;
  senderInfo: string;
  beneficiaryInfo: string;
  note: string | null;
  operationAt: string;
  createdAt: string;
  /** Null pour l'agence de destination (non visible) ; vide pour un crédit total. */
  payments: TransferPayment[] | null;
  credit: TransferCredit | null;
  deliveryOperationAt: string | null;
  isCodeLocked: boolean;
  deliveredWithoutCode: boolean;
  cancellationOperationAt: string | null;
  cancellationReason: string | null;
  version: number;
}

export interface TransferList {
  items: Transfer[];
  page: number;
  pageSize: number;
  /** Transferts qui répondent aux filtres. */
  totalCount: number;
  /** Tous les transferts visibles (« 24 / 392 »). */
  allCount: number;
}

export interface SummaryAmount {
  amount: number;
  count: number;
}

export interface TransferSummary {
  agencyId: string;
  currency: Currency;
  otherAgencyId: string;
  otherCurrency: Currency;
  from: string;
  to: string;
  /** Payé à l'agence pour les transferts qu'elle a envoyés (sa devise). */
  incoming: SummaryAmount;
  /** Remis par l'agence sur les transferts qu'elle a reçus, livrés seulement (sa devise). */
  outgoing: SummaryAmount;
  /** Null sans profit.view. */
  sentByOther: SummaryAmount | null;
  /** Null sans profit.view. */
  deliveredByOther: SummaryAmount | null;
  /** Ce que l'agence doit encore payer (sa devise). */
  notDelivered: SummaryAmount;
  profitMro: number | null;
  saleRate: number | null;
  purchaseRate: number | null;
}

/** Filtres de la liste (mêmes noms que les paramètres de l'API et de l'adresse de la page). */
export interface TransferFilters {
  from?: string;
  to?: string;
  status?: TransferStatus;
  direction?: TransferDirection;
  search?: string;
  page?: number;
}

/** Une caisse du tableau de bord, sur les transferts qu'elle a envoyés (annulés exclus), dans sa devise. */
export interface CaisseDashboard {
  agencyId: string;
  currency: Currency;
  /** Envoyé = livré + non livré. */
  sent: SummaryAmount;
  notDelivered: SummaryAmount;
  delivered: SummaryAmount;
  /** Livrés / envoyés × 100, en nombre de transferts ; null sans envoi. */
  deliveryRatePercent: number | null;
}

/** Réponse de GET /transfers/dashboard (balance.view) : GetTransferDashboardQuery.cs. */
export interface TransferDashboard {
  from: string;
  to: string;
  caisses: CaisseDashboard[];
}

/** Corps de POST /transfers (CreateTransferRequest, TransferEndpoints.cs). */
export interface CreateTransferRequest {
  /** Facultatif pour un agent (son agence), obligatoire pour le Superviseur. */
  sourceAgencyId: string | null;
  destinationAgencyId: string;
  /** Montant de référence, FCFA entier. */
  amountXof: number;
  senderInfo: string;
  beneficiaryInfo: string;
  note: string | null;
  /** Null = maintenant (heure du serveur). Jamais dans le futur. */
  operationAt: string | null;
  /** Répartition du paiement du client, dans la devise de l'agence d'envoi. */
  payments: { treasuryAccountId: string; amount: number }[];
  /** Crédit activé : tiers qui porte la part non payée. */
  creditPartyId: string | null;
}

/** Réponse de POST /transfers (CreatedTransfer, CreateTransferCommand.cs). */
export interface CreatedTransfer {
  id: string;
  reference: string;
  /** 6 chiffres, renvoyé UNE seule fois ; null quand la clé d'idempotence avait déjà servi. */
  withdrawalCode: string | null;
  amountXof: number;
  amountMro: number;
  saleRate: number;
  /** Part à crédit, devise de l'agence d'envoi (0 sans crédit). */
  creditAmount: number;
  /** Vrai : rien n'a été créé, c'est le transfert déjà enregistré avec cette clé. */
  replayed: boolean;
}
