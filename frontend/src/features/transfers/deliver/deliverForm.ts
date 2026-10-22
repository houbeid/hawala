import type { TreasuryAccount } from '@/features/treasury/types';
import { parseMoneyInput, sumAmounts } from '@/shared/format/moneyInput';
import { paymentValues, type FieldErrors, type PaymentLine } from '../create/createForm';

// Livraison (maquette 06, règles validées du 20 octobre 2026) : la somme remise au bénéficiaire est répartie sur
// les caisses ACTIVES de l'agence de destination (MRO / MRU à Nouakchott, FCFA entier à Dakar) ; le total remis
// égale le montant à livrer. Une caisse insuffisante n'empêche pas la livraison : avertissement seulement.

export const CODE_LENGTH = 6;
export const ID_DOCUMENT_MAX = 200;
export const REASON_MAX = 500;

export interface DeliveryForm {
  /** 6 chiffres, donnés par le bénéficiaire (livraison avec code). */
  code: string;
  /** Livraison sans code (Superviseur) : type et numéro de la pièce d'identité du bénéficiaire. */
  idDocument: string;
  reason: string;
  note: string;
  payments: PaymentLine[];
}

export function validateCode(code: string): string | null {
  return new RegExp(`^\\d{${CODE_LENGTH}}$`).test(code.trim()) ? null : 'transfers.deliver.validation.code';
}

export function validateWithoutCode(form: Pick<DeliveryForm, 'idDocument' | 'reason'>): FieldErrors {
  const errors: FieldErrors = {};
  if (!form.idDocument.trim()) errors.idDocument = 'transfers.deliver.validation.idDocument';
  if (!form.reason.trim()) errors.reason = 'transfers.detail.reasonRequired';
  return errors;
}

/** La remise : un compte par ligne (une seule fois), un montant valable, total = montant à livrer. */
export function validateRemise(lines: PaymentLine[], amountToDeliver: number): FieldErrors {
  const errors: FieldErrors = {};
  const seen = new Set<string>();
  lines.forEach((line, index) => {
    if (!line.accountId) errors[`payments.${index}.accountId`] = 'transfers.create.validation.accountRequired';
    else if (seen.has(line.accountId)) errors[`payments.${index}.accountId`] = 'transfers.create.validation.accountDuplicated';
    seen.add(line.accountId);
    const parsed = parseMoneyInput(line.amount, line.unit);
    if (!parsed.ok) errors[`payments.${index}.amount`] = `transfers.create.validation.payment.${parsed.error}`;
  });
  if (Object.keys(errors).length > 0) return errors;
  if (lines.length === 0) errors.payments = 'transfers.deliver.validation.remiseRequired';
  else if (remisedTotal(lines) !== amountToDeliver) errors.payments = 'transfers.deliver.validation.totalMismatch';
  return errors;
}

export function remisedTotal(lines: PaymentLine[]): number {
  return sumAmounts(paymentValues(lines).filter((value): value is number => value !== null));
}

export interface OverdraftWarning {
  accountId: string;
  name: string;
  /** Solde après la livraison, négatif. */
  after: number;
}

/**
 * Caisses qui passeraient en négatif, d'après les soldes connus (cash.budget.read ; sans ce droit, pas de
 * prévision : le serveur le dira après la livraison). Avertissement seulement, jamais de blocage.
 */
export function overdraftWarnings(lines: PaymentLine[], accounts: TreasuryAccount[]): OverdraftWarning[] {
  const values = paymentValues(lines);
  const warnings: OverdraftWarning[] = [];
  lines.forEach((line, index) => {
    const account = accounts.find((a) => a.id === line.accountId);
    const value = values[index];
    if (!account || account.balance === null || value === null || value === undefined) return;
    const after = Math.round((account.balance - value) * 100) / 100;
    if (after < 0) warnings.push({ accountId: account.id, name: account.name, after });
  });
  return warnings;
}
