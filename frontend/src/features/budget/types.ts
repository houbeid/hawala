// Budget de la caisse, copiés sur le backend :
// - CashBudgetItem, CashBudgetLine, CashBudgetView : Cash.Application/Features/Budgets/CashBudgetCalculator.cs
// - CashBudgetSnapshotView : Cash.Application/Features/Budgets/CashBudgetFeatures.cs
// - CoherenceCheckView : Cash.Application/Features/Budgets/CoherenceChecker.cs
// - CoherenceAlert, CoherenceAlertKind : Cash.Domain/Budgets/CoherenceAlert.cs
import type { Currency } from '@/features/organization/types';

/** Un compte ou un tiers dans le détail d'une ligne. */
export interface CashBudgetItem {
  id: string;
  name: string;
  balance: number;
}

export interface CashBudgetLine {
  amount: number;
  items: CashBudgetItem[];
}

/** GET /cash-budget?agencyId= (cash.budget.read) : budget en direct, dans la devise de l'agence. */
export interface CashBudgetView {
  agencyId: string;
  currency: Currency;
  cash: CashBudgetLine;
  banks: CashBudgetLine;
  /** Tiers « dans le bilan » en rouge (ils nous doivent). */
  debtors: CashBudgetLine;
  /** Tiers « dans le bilan » en vert (on leur doit), montants en positif. */
  creditors: CashBudgetLine;
  /** Transferts que l'agence doit encore payer. */
  undelivered: number;
  /** espèces + banques + débiteurs − créditeurs − non livrés. */
  budget: number;
}

export type CoherenceAlertKind =
  | 'UnusualRate'
  | 'BalanceAdjustment'
  | 'OpeningBalanceChanged'
  | 'CashMovementCancelled'
  | 'TransferCancelled'
  | 'DeliveredWithoutCode'
  | 'NegativeBalance';

/** Ordre d'affichage des familles d'alertes (maquette 10). */
export const COHERENCE_ALERT_KINDS: readonly CoherenceAlertKind[] = [
  'UnusualRate',
  'BalanceAdjustment',
  'OpeningBalanceChanged',
  'CashMovementCancelled',
  'TransferCancelled',
  'DeliveredWithoutCode',
  'NegativeBalance',
];

export interface CoherenceAlert {
  kind: CoherenceAlertKind;
  at: string;
  sourceId: string | null;
  /** Texte court écrit par le serveur (référence, nom du compte ou du tiers, libellé). */
  label: string;
  /** Montant dans la devise de l'agence, ou taux du transfert (UnusualRate) ; null si sans objet. */
  amount: number | null;
  reason: string | null;
}

/** GET /cash-budget/coherence?agencyId= : alertes depuis le dernier budget enregistré (non bloquantes). */
export interface CoherenceCheckView {
  agencyId: string;
  since: string;
  alerts: CoherenceAlert[];
}

/** Photo enregistrée du budget (une par agence et par jour, remplacée le même jour). */
export interface CashBudgetSnapshot {
  id: string;
  agencyId: string;
  date: string;
  currency: Currency;
  cash: number;
  banks: number;
  debtors: number;
  creditors: number;
  undelivered: number;
  budget: number;
  savedBy: string;
  savedAt: string;
  checkedSince: string | null;
  alerts: CoherenceAlert[];
}
