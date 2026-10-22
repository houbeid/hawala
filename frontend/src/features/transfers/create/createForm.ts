import type { Currency } from '@/features/organization/types';
import { parseDecimalInput } from '@/shared/format/decimalInput';
import { parseMoneyInput, sumAmounts, xofFromMro, type InputUnit } from '@/shared/format/moneyInput';

// Formulaire de création d'un transfert (maquette 04) et sa validation étape par étape. Ce ne sont que des
// contrôles de saisie : les règles métier (taux, plafonds, comptes, crédit) restent au serveur, et ses refus
// ramènent à l'étape concernée (stepForError).

export const SENDER_MAX = 200; // Transfer.PartyInfoMaxLength
export const NOTE_MAX = 500; // Transfer.NoteMaxLength

export interface PaymentLine {
  accountId: string;
  amount: string;
  unit: InputUnit;
}

export interface CreateForm {
  sourceAgencyId: string;
  /**
   * Montant tel que saisi, entier, dans l'unité choisie (décision du 22 octobre 2026) : MRO par défaut à
   * Nouakchott (ce que le client paie), FCFA à Dakar. Le FCFA entier reste la référence envoyée au serveur.
   */
  amount: string;
  amountUnit: 'MRO' | 'XOF';
  /** « aaaa-mm-jjThh:mm » (heure locale) ; vide = maintenant. */
  operationAt: string;
  payments: PaymentLine[];
  senderInfo: string;
  beneficiaryInfo: string;
  note: string;
}

export type Step = 1 | 2 | 3;
/** Clés de traduction des erreurs de saisie, par champ. */
export type FieldErrors = Record<string, string>;

/** Montant saisi (entier), dans son unité ; null s'il n'est pas valable. */
export function enteredAmount(form: Pick<CreateForm, 'amount'>): number | null {
  const parsed = parseDecimalInput(form.amount, 0);
  return parsed.ok ? parsed.value : null;
}

/**
 * FCFA de référence envoyé au serveur : le montant tapé en FCFA, ou déduit du MRO tapé (xofFromMro, le serveur
 * retrouve alors exactement ce MRO). Null sans montant valable, ou sans taux pour une saisie en MRO.
 */
export function amountXofValue(form: Pick<CreateForm, 'amount' | 'amountUnit'>, saleRate: number | null): number | null {
  const entered = enteredAmount(form);
  if (entered === null) return null;
  if (form.amountUnit === 'XOF') return entered;
  return saleRate === null ? null : xofFromMro(entered, saleRate);
}

/** Date d'opération envoyée au serveur (ISO) ; null = maintenant. */
export function operationAtIso(form: Pick<CreateForm, 'operationAt'>): string | null {
  if (!form.operationAt) return null;
  const date = new Date(form.operationAt);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** Jour dont le taux s'applique (date UTC de l'opération, comme le serveur). */
export function rateDay(form: Pick<CreateForm, 'operationAt'>, now: Date = new Date()): string {
  return (operationAtIso(form) ?? now.toISOString()).slice(0, 10);
}

export function validateAmountStep(form: CreateForm, now: Date = new Date()): FieldErrors {
  const errors: FieldErrors = {};
  if (!form.sourceAgencyId) errors.sourceAgencyId = 'transfers.create.validation.sourceRequired';
  const parsed = parseDecimalInput(form.amount, 0);
  if (!parsed.ok) {
    // Un transfert est toujours entier dans les deux devises (MRO arrondi à l'unité, FCFA entier).
    errors.amount = `transfers.create.validation.amount.${parsed.error}${parsed.error === 'tooPrecise' ? form.amountUnit : ''}`;
  }
  const iso = operationAtIso(form);
  if (form.operationAt && !iso) errors.operationAt = 'transfers.create.validation.dateInvalid';
  if (iso && new Date(iso) > now) errors.operationAt = 'transfers.create.validation.dateFuture';
  return errors;
}

export function paymentValues(lines: PaymentLine[]): (number | null)[] {
  return lines.map((line) => {
    const parsed = parseMoneyInput(line.amount, line.unit);
    return parsed.ok ? parsed.value : null;
  });
}

export function paidTotal(lines: PaymentLine[]): number {
  return sumAmounts(paymentValues(lines).filter((value): value is number => value !== null));
}

/**
 * Répartition : chaque ligne a un compte (une seule fois) et un montant valable ; sans crédit, le total
 * réparti égale le montant à payer. Avec crédit (2e livraison), le reste devient la part à crédit.
 */
export function validatePaymentStep(form: CreateForm, amountToPay: number, credit: boolean): FieldErrors {
  const errors: FieldErrors = {};
  const seen = new Set<string>();
  form.payments.forEach((line, index) => {
    if (!line.accountId) errors[`payments.${index}.accountId`] = 'transfers.create.validation.accountRequired';
    else if (seen.has(line.accountId)) errors[`payments.${index}.accountId`] = 'transfers.create.validation.accountDuplicated';
    seen.add(line.accountId);
    const parsed = parseMoneyInput(line.amount, line.unit);
    if (!parsed.ok) errors[`payments.${index}.amount`] = `transfers.create.validation.payment.${parsed.error}`;
  });
  if (Object.keys(errors).length > 0) return errors;

  const total = paidTotal(form.payments);
  if (total > amountToPay) errors.payments = 'transfers.create.validation.overpaid';
  else if (!credit && total !== amountToPay) errors.payments = 'transfers.create.validation.totalMismatch';
  else if (credit && total === amountToPay) errors.payments = 'transfers.create.validation.creditNotNeeded';
  else if (!credit && form.payments.length === 0) errors.payments = 'transfers.create.validation.paymentRequired';
  return errors;
}

export function validatePeopleStep(form: CreateForm): FieldErrors {
  const errors: FieldErrors = {};
  if (!form.senderInfo.trim()) errors.senderInfo = 'transfers.create.validation.senderRequired';
  else if (form.senderInfo.trim().length > SENDER_MAX) errors.senderInfo = 'transfers.create.validation.tooLong';
  if (!form.beneficiaryInfo.trim()) errors.beneficiaryInfo = 'transfers.create.validation.beneficiaryRequired';
  else if (form.beneficiaryInfo.trim().length > SENDER_MAX) errors.beneficiaryInfo = 'transfers.create.validation.tooLong';
  if (form.note.trim().length > NOTE_MAX) errors.note = 'transfers.create.validation.noteTooLong';
  return errors;
}

/** Étape où ramener l'agent après un refus du serveur. */
export function stepForError(code: string): Step {
  if (code.startsWith('Transfers.Payment.') || code.startsWith('Transfers.Credit.') || code === 'Transfers.Limits.CreditPerDayExceeded') {
    return 2;
  }
  if (['Transfers.Transfer.SenderRequired', 'Transfers.Transfer.BeneficiaryRequired', 'Transfers.Transfer.TextTooLong'].includes(code)) {
    return 3;
  }
  return 1;
}

/** Montant payé par le client à l'agence d'envoi, dans sa devise (celui que la répartition doit couvrir). */
export function amountToPay(sourceCurrency: Currency, amountXof: number, amountMro: number): number {
  return sourceCurrency === 'XOF' ? amountXof : amountMro;
}
