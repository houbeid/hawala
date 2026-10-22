import type { Currency } from '@/features/organization/types';
import type { Transfer } from './types';

/**
 * Chiffre principal d'un transfert (maquette 03, comme l'app actuelle « المبلغ المراد تسليمه ») :
 * le montant à remettre au bénéficiaire, dans la devise de l'agence qui livre.
 */
export function amountToDeliver(transfer: Transfer): { amount: number; currency: Currency } {
  return transfer.destinationCurrency === 'XOF'
    ? { amount: transfer.amountXof, currency: 'XOF' }
    : { amount: transfer.amountMro, currency: 'MRO' };
}

/** L'autre montant (payé par le client à l'agence d'envoi), en secondaire. */
export function amountPaid(transfer: Transfer): { amount: number; currency: Currency } {
  return transfer.sourceCurrency === 'XOF'
    ? { amount: transfer.amountXof, currency: 'XOF' }
    : { amount: transfer.amountMro, currency: 'MRO' };
}

/**
 * Heure d'opération (« 14:32 »), précédée du jour (« 21/10 14:32 ») quand la période couvre plusieurs jours.
 * Lue en UTC, comme les jours du serveur (Nouakchott et Dakar sont en UTC+0).
 */
export function formatOperationTime(iso: string, withDay: boolean): string {
  const date = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  const time = `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}`;
  return withDay ? `${pad(date.getUTCDate())}/${pad(date.getUTCMonth() + 1)} ${time}` : time;
}
