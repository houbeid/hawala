import { z } from 'zod';
import { parseDecimalInput } from '@/shared/format/decimalInput';

// Saisie des deux taux : positifs, 2 décimales au plus, refusés (jamais arrondis) au-delà.
// Le prix de vente inférieur au prix d'achat n'est PAS une erreur : avertissement seulement (décision du 22 octobre 2026).

export const RATE_DECIMALS = 2;

const rateText = z.string().superRefine((value, ctx) => {
  const parsed = parseDecimalInput(value, RATE_DECIMALS);
  if (!parsed.ok) ctx.addIssue({ code: 'custom', message: `rates.validation.${parsed.error}` });
});

export const rateFormSchema = z.object({ purchaseRate: rateText, saleRate: rateText });

export type RateForm = z.infer<typeof rateFormSchema>;

/** Valeur lue si le texte est valable, sinon null (pour l'écart de commission affiché en direct). */
export function rateValue(text: string): number | null {
  const parsed = parseDecimalInput(text, RATE_DECIMALS);
  return parsed.ok ? parsed.value : null;
}
