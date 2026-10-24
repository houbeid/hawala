import { formatAmount, formatRate, type Currency } from '@/shared/format/money';
import { COHERENCE_ALERT_KINDS, type CashBudgetSnapshot, type CoherenceAlert, type CoherenceAlertKind } from './types';

// Règles d'affichage du budget de la caisse (skill « Budget de la caisse » ; décisions du 21 octobre 2026).
// Le contrôle de cohérence ne bloque jamais : il liste ce qui a été ENREGISTRÉ depuis le dernier budget.

export interface AlertGroup {
  kind: CoherenceAlertKind;
  alerts: CoherenceAlert[];
}

/** Alertes regroupées par famille, dans l'ordre de la maquette 10 ; les familles vides sont omises. */
export function groupAlerts(alerts: CoherenceAlert[]): AlertGroup[] {
  return COHERENCE_ALERT_KINDS.map((kind) => ({ kind, alerts: alerts.filter((a) => a.kind === kind) })).filter((g) => g.alerts.length > 0);
}

export interface AlertDetail {
  /** Objet concerné (référence d'un transfert, nom d'un compte ou d'un tiers) ; null si le titre suffit. */
  subject: string | null;
  /** Montant ou taux mis en forme ; null si sans objet. */
  value: string | null;
  reason: string | null;
}

/** Partie du libellé du serveur après « … : » (« Solde initial : Boutique » → « Boutique »), sinon le libellé entier. */
function afterColon(label: string): string {
  const index = label.indexOf(' : ');
  return index >= 0 ? label.slice(index + 3) : label;
}

/** Partie du libellé avant « : » (« TR-NKC-… : 3900 pour 3650 le jour même » → « TR-NKC-… »). */
function beforeColon(label: string): string {
  const index = label.indexOf(' : ');
  return index >= 0 ? label.slice(0, index) : label;
}

/**
 * Détail lisible d'une alerte. Le libellé du serveur est en français : on n'en garde que la référence ou le nom,
 * le reste vient des traductions (titre de la famille) et des montants.
 * POINT OUVERT : l'alerte ne renvoie ni la référence ni le nom dans un champ à part ; on les lit dans le libellé.
 */
export function alertDetail(alert: CoherenceAlert, currency: Currency): AlertDetail {
  const money = (signed: boolean) => (alert.amount === null ? null : formatAmount(alert.amount, currency, { signed }));
  switch (alert.kind) {
    case 'UnusualRate':
      return { subject: beforeColon(alert.label), value: alert.amount === null ? null : formatRate(alert.amount), reason: null };
    case 'BalanceAdjustment':
      return { subject: null, value: money(true), reason: alert.reason };
    case 'OpeningBalanceChanged':
      return { subject: afterColon(alert.label), value: money(true), reason: null };
    case 'CashMovementCancelled':
      return { subject: null, value: money(false), reason: alert.reason };
    case 'NegativeBalance':
      return { subject: alert.label, value: money(false), reason: null };
    default:
      return { subject: alert.label, value: null, reason: alert.reason };
  }
}

/** Budget déjà enregistré aujourd'hui (le réenregistrer le remplace, règle validée) ; null sinon. */
export function snapshotOf(snapshots: CashBudgetSnapshot[], day: string): CashBudgetSnapshot | null {
  return snapshots.find((s) => s.date === day) ?? null;
}
