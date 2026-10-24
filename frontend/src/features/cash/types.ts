// Contrats de la Caisse, copiés sur le backend :
// - CashMovementView, CashAmount, CashMovementList : Cash.Application/Features/Movements/CashMovementView.cs
// - CreateCashMovementRequest : Hawala.Api/Endpoints/Cash/CashEndpoints.cs (en-tête Idempotency-Key obligatoire)
// - CreatedCashMovement : Cash.Application/Features/Movements/CreateCashMovement/CreateCashMovementCommand.cs
// - BalanceAdjustmentView : Cash.Application/Features/Adjustments/ListBalanceAdjustments.cs ; requêtes d'annulation et
//   d'ajustement : Hawala.Api/Endpoints/Cash/CashEndpoints.cs (cash.manage, Superviseur)
import type { Currency } from '@/features/organization/types';

/** Le type fixe le signe : l'agent ne choisit jamais « + » ou « − ». */
export type CashMovementType = 'Receipt' | 'Payment' | 'OwnerContribution' | 'OwnerWithdrawal' | 'InternalTransfer';
export const CASH_MOVEMENT_TYPES: readonly CashMovementType[] = ['Receipt', 'Payment', 'OwnerContribution', 'OwnerWithdrawal', 'InternalTransfer'];

export type CashMovementStatus = 'Active' | 'Cancelled';

export interface CashMovement {
  id: string;
  agencyId: string;
  type: CashMovementType;
  /** Vrai si l'argent entre dans l'agence (encaissement, approvisionnement). */
  incoming: boolean;
  /** Virement interne : ni entrée ni sortie pour l'agence. */
  isInternal: boolean;
  treasuryAccountId: string;
  treasuryAccountName: string | null;
  destinationTreasuryAccountId: string | null;
  destinationTreasuryAccountName: string | null;
  partyId: string | null;
  partyName: string | null;
  amount: number;
  currency: Currency;
  note: string | null;
  operationAt: string;
  createdAt: string;
  createdBy: string;
  status: CashMovementStatus;
  cancellationReason: string | null;
}

export interface CashAmount {
  amount: number;
  count: number;
}

/** GET /cash-movements : une page de 50, les totaux de la période (annulés exclus) et « 24 / 392 ». */
export interface CashMovementList {
  items: CashMovement[];
  page: number;
  pageSize: number;
  totalCount: number;
  allCount: number;
  incoming: CashAmount;
  outgoing: CashAmount;
  net: number;
  internalTransfers: CashAmount;
}

export type CashListParams = {
  agencyId?: string;
  from?: string;
  to?: string;
  type?: CashMovementType;
  treasuryAccountId?: string;
  search?: string;
  page?: number;
};

export interface CreateCashMovementRequest {
  /** Facultative pour un agent (la sienne) ; obligatoire pour le Superviseur. */
  agencyId: string | null;
  type: CashMovementType;
  treasuryAccountId: string;
  /** Obligatoire pour Receipt / Payment, absent sinon. */
  partyId: string | null;
  amount: number;
  /** MRO (défaut) ou MRU (converti × 10 par le serveur). */
  unit: 'MRO' | 'MRU' | null;
  note: string | null;
  /** ISO ; null = maintenant. Jamais dans le futur. */
  operationAt: string | null;
  /** Virement interne seulement. */
  destinationTreasuryAccountId: string | null;
}

/** Réponse 201 (ou 200 si la même clé a déjà servi : `replayed`). */
export interface CreatedCashMovement {
  id: string;
  type: CashMovementType;
  amount: number;
  currency: Currency;
  replayed: boolean;
}

/** Note : 500 caractères au plus (Cash.Movement.NoteTooLong). */
export const CASH_NOTE_MAX = 500;

/** POST /cash-movements/{id}/cancel : motif obligatoire, contre-passation au jour de l'annulation ; 204. */
export interface CancelCashMovementRequest {
  reason: string;
}

export type AdjustmentTarget = 'TreasuryAccount' | 'Party';

/** POST /balance-adjustments (cash.manage) : 201 { id }. */
export interface CreateBalanceAdjustmentRequest {
  agencyId: string;
  target: AdjustmentTarget;
  targetId: string;
  /** Différence signée : positive = le solde monte (caisse plus pleine, tiers plus en rouge), négative = il baisse. */
  amount: number;
  reason: string;
  /** ISO ; null = maintenant. */
  operationAt: string | null;
}

/** GET /balance-adjustments (cash.budget.read) : ajustements d'une période, aujourd'hui par défaut. */
export interface BalanceAdjustment {
  id: string;
  agencyId: string;
  target: AdjustmentTarget;
  targetId: string;
  targetName: string | null;
  amount: number;
  currency: Currency;
  reason: string;
  operationAt: string;
  createdBy: string;
  createdAt: string;
}

/** Motif d'une annulation ou d'un ajustement : 500 caractères au plus. */
export const CASH_REASON_MAX = 500;
