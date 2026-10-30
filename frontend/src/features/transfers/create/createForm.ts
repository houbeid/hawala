import type { Currency } from '@/features/organization/types';
import { localInputFromIso } from '@/shared/format/dates';
import { parseDecimalInput, toInputText } from '@/shared/format/decimalInput';
import { defaultUnit, parseMoneyInput, sumAmounts, xofFromMro, type InputUnit } from '@/shared/format/moneyInput';
import type { CreateTransferRequest } from '../types';

// Formulaire de création d'un transfert (maquette 04) et sa validation étape par étape. Ce ne sont que des
// contrôles de saisie : les règles métier (taux, plafonds, comptes, crédit) restent au serveur, et ses refus
// ramènent à l'étape concernée (stepForError).

export const SENDER_MAX = 200; // Transfer.PartyInfoMaxLength
export const NOTE_MAX = 500; // Transfer.NoteMaxLength
export const SALE_RATE_DECIMALS = 2; // ExchangeRate.MaxDecimals
/** Écart avec le prix du jour au-delà duquel le contrôle de cohérence signale le transfert (TransferMonitor). */
export const UNUSUAL_RATE_GAP = 0.05;

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
  /**
   * Prix de vente de ce transfert, tel que saisi ; vide = prix du jour (décision du patron, 30 octobre 2026 : l'agent
   * peut vendre plus cher ou moins cher, sans limite). Absent dans « Modifier montant ou taux », qui a le sien.
   */
  saleRate?: string;
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

/** Prix de vente saisi par l'agent ; null s'il est vide (prix du jour) ou mal saisi. */
export function chosenSaleRate(form: Pick<CreateForm, 'saleRate'>): number | null {
  if (!form.saleRate?.trim()) return null;
  const parsed = parseDecimalInput(form.saleRate, SALE_RATE_DECIMALS);
  return parsed.ok ? parsed.value : null;
}

/** Prix appliqué au transfert : celui saisi par l'agent, sinon celui du jour ; null s'il est mal saisi. */
export function appliedSaleRate(form: Pick<CreateForm, 'saleRate'>, dayRate: number | null): number | null {
  return form.saleRate?.trim() ? chosenSaleRate(form) : dayRate;
}

/** Même règle que le serveur : écart relatif avec le prix du jour strictement supérieur à 5 %. */
export function isUnusualRate(applied: number, dayRate: number): boolean {
  return Math.abs(applied - dayRate) > dayRate * UNUSUAL_RATE_GAP;
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
  if (form.saleRate?.trim()) {
    const rate = parseDecimalInput(form.saleRate, SALE_RATE_DECIMALS);
    if (!rate.ok) errors.saleRate = `rates.validation.${rate.error}`;
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
 * Répartition : chaque ligne a un compte (le même peut revenir, décision du patron du 27 octobre 2026) et un
 * montant valable ; sans crédit, le total
 * réparti égale le montant à payer. Avec crédit (2e livraison), le reste devient la part à crédit.
 */
export function validatePaymentStep(form: CreateForm, amountToPay: number, credit: boolean): FieldErrors {
  const errors: FieldErrors = {};
  form.payments.forEach((line, index) => {
    if (!line.accountId) errors[`payments.${index}.accountId`] = 'transfers.create.validation.accountRequired';
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

/**
 * Formulaire rempli à partir d'une opération rejetée de la file hors ligne (« Corriger ») : le montant de référence
 * en FCFA, la date de la saisie, la répartition dans la devise de l'agence d'envoi, les textes. Le crédit (tiers) est
 * repris à part par la page.
 */
export function formFromRequest(request: CreateTransferRequest, sourceAgencyId: string, sourceCurrency: Currency): CreateForm {
  return {
    sourceAgencyId,
    amount: String(request.amountXof),
    amountUnit: 'XOF',
    operationAt: request.operationAt ? localInputFromIso(request.operationAt) : '',
    saleRate: request.saleRate != null ? toInputText(request.saleRate) : '',
    payments: request.payments.map((line) => ({
      accountId: line.treasuryAccountId,
      amount: toInputText(line.amount),
      unit: defaultUnit(sourceCurrency),
    })),
    senderInfo: request.senderInfo,
    beneficiaryInfo: request.beneficiaryInfo,
    note: request.note ?? '',
  };
}
