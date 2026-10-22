import { parseDecimalInput, toInputText } from '@/shared/format/decimalInput';
import { defaultUnit, transferMroPreview } from '@/shared/format/moneyInput';
import { amountToPay, amountXofValue, operationAtIso, paymentValues, type CreateForm } from '../create/createForm';
import type { EditTransferAmountsRequest, Transfer } from '../types';

// Modification d'argent d'un transfert non livré (Superviseur, règles validées du 21 octobre 2026) : montant,
// prix de vente, date, répartition et crédit ; motif obligatoire ; version lue sur la fiche. Le prix d'achat et
// les agences ne changent jamais. Le formulaire reprend celui de la création (même saisie, même répartition).

export const RATE_DECIMALS = 2;

/** Valeurs de départ : l'état actuel du transfert, montant saisi dans la devise de l'agence d'envoi. */
export function initialForm(transfer: Transfer): CreateForm {
  const unit = transfer.sourceCurrency;
  return {
    sourceAgencyId: transfer.sourceAgencyId,
    amount: toInputText(unit === 'MRO' ? transfer.amountMro : transfer.amountXof),
    amountUnit: unit,
    operationAt: '',
    payments: (transfer.payments ?? []).map((payment) => ({
      accountId: payment.treasuryAccountId,
      amount: toInputText(payment.amount),
      unit: defaultUnit(payment.currency),
    })),
    // Textes non modifiés ici (action « Modifier les textes ») : gardés pour réutiliser le type du formulaire.
    senderInfo: transfer.senderInfo,
    beneficiaryInfo: transfer.beneficiaryInfo,
    note: transfer.note ?? '',
  };
}

/** Prix de vente saisi (2 décimales au plus, refus sinon) ; null s'il n'est pas valable. */
export function saleRateValue(text: string): number | null {
  const parsed = parseDecimalInput(text, RATE_DECIMALS);
  return parsed.ok ? parsed.value : null;
}

export interface Computed {
  amountXof: number | null;
  amountMro: number | null;
  /** Montant que la répartition (et le crédit) doivent couvrir, devise de l'agence d'envoi. */
  toPay: number | null;
}

/** Nouveaux montants, calculés comme le serveur (FCFA de référence, MRO arrondi une fois au nouveau prix). */
export function compute(form: Pick<CreateForm, 'amount' | 'amountUnit'>, saleRate: number | null, transfer: Transfer): Computed {
  const amountXof = amountXofValue(form, saleRate);
  const amountMro = amountXof !== null && saleRate !== null ? transferMroPreview(amountXof, saleRate) : null;
  const toPay = amountXof !== null && amountMro !== null ? amountToPay(transfer.sourceCurrency, amountXof, amountMro) : null;
  return { amountXof, amountMro, toPay };
}

/**
 * Requête : montant, prix et date envoyés seulement s'ils changent (null = inchangé côté serveur) ; répartition
 * et crédit toujours complets.
 */
export function buildRequest(
  transfer: Transfer,
  form: CreateForm,
  values: { amountXof: number; saleRate: number; creditPartyId: string | null; reason: string },
): EditTransferAmountsRequest {
  return {
    version: transfer.version,
    amountXof: values.amountXof === transfer.amountXof ? null : values.amountXof,
    saleRate: values.saleRate === transfer.saleRate ? null : values.saleRate,
    operationAt: operationAtIso(form),
    payments: form.payments.map((line, index) => ({ treasuryAccountId: line.accountId, amount: paymentValues(form.payments)[index] ?? 0 })),
    creditPartyId: values.creditPartyId,
    reason: values.reason,
  };
}
