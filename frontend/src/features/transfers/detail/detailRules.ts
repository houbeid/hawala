import type { CurrentAccount } from '@/shared/auth/types';
import type { Transfer, TransferEvent, TransferFieldChange } from '../types';

// Actions de la fiche (maquette 05, règles validées des 20 et 21 octobre 2026). Une action sans droit est
// ABSENTE, jamais grisée ; le serveur revérifie tout (permission, agence, statut, version).

export interface DetailActions {
  /** Expéditeur, bénéficiaire, note : agence d'envoi ou Superviseur (transfers.create). */
  editTexts: boolean;
  /** Superviseur (transfers.cancel), non livré seulement. */
  cancel: boolean;
  /** Agence d'envoi ou Superviseur (transfers.create) ; ne débloque pas. */
  newCode: boolean;
  /** Superviseur (transfers.manage_code), code bloqué après 5 essais faux. */
  unlock: boolean;
  /** Montant, prix de vente, date, répartition, crédit : Superviseur (transfers.edit_rate), non livré. */
  editAmounts: boolean;
  /** Livrer avec le code : agence de destination ou Superviseur (transfers.deliver), code non bloqué. */
  deliver: boolean;
  /** Livrer sans code (pièce d'identité + motif) : Superviseur (transfers.manage_code), même code bloqué. */
  deliverWithoutCode: boolean;
}

export function availableActions(transfer: Transfer, account: CurrentAccount): DetailActions {
  const can = (permission: string) => account.permissions.includes(permission);
  const open = transfer.status === 'NotDelivered';
  const sendingSide = account.role === 'Supervisor' || account.agency?.id === transfer.sourceAgencyId;
  return {
    editTexts: open && can('transfers.create') && sendingSide,
    cancel: open && can('transfers.cancel'),
    newCode: open && can('transfers.create') && sendingSide,
    unlock: open && transfer.isCodeLocked && can('transfers.manage_code'),
    editAmounts: open && can('transfers.edit_rate'),
    deliver:
      open &&
      !transfer.isCodeLocked &&
      can('transfers.deliver') &&
      (account.role === 'Supervisor' || account.agency?.id === transfer.destinationAgencyId),
    deliverWithoutCode: open && can('transfers.manage_code'),
  };
}

/** « Créé par » : l'auteur de la ligne « Ajouté » de l'historique (l'API de la fiche ne porte pas de nom). */
export function creatorName(history: TransferEvent[] | undefined): string | null {
  return history?.find((event) => event.type === 'Created')?.authorName ?? null;
}

/** Numéro de l'essai d'un code faux : la valeur « après » du compteur (« essai 2 sur 5 »). */
export function wrongCodeAttempt(event: TransferEvent): number | null {
  const change = event.changes.find((c) => c.field === 'FailedCodeAttempts');
  const value = change?.after ? Number(change.after) : NaN;
  return Number.isInteger(value) ? value : null;
}

export type ChangeValue =
  | { kind: 'text'; text: string }
  | { kind: 'amount'; value: number; currency: 'MRO' | 'XOF' }
  | { kind: 'rate'; value: number }
  | { kind: 'date'; iso: string }
  | { kind: 'status'; status: string }
  | { kind: 'payments'; lines: { accountId: string; amount: number }[] }
  | { kind: 'changed' }
  | { kind: 'empty' };

/**
 * Lecture d'une valeur « avant / après » de l'historique, selon le champ. Le serveur écrit les nombres en
 * texte invariant, les dates en ISO et la répartition en « compte:montant;… ». Un identifiant (tiers du
 * crédit) n'est jamais affiché tel quel : seulement « modifié ».
 */
export function readChangeValue(change: TransferFieldChange, side: 'before' | 'after', transfer: Transfer): ChangeValue {
  const raw = change[side];
  if (raw === null || raw === '') return { kind: 'empty' };
  switch (change.field) {
    case 'AmountXof':
      return { kind: 'amount', value: Number(raw), currency: 'XOF' };
    case 'AmountMro':
      return { kind: 'amount', value: Number(raw), currency: 'MRO' };
    case 'CreditAmount':
      return { kind: 'amount', value: Number(raw), currency: transfer.sourceCurrency };
    case 'SaleRate':
      return { kind: 'rate', value: Number(raw) };
    case 'OperationAt':
      return { kind: 'date', iso: raw };
    case 'Status':
      return { kind: 'status', status: raw };
    case 'Payments':
      return {
        kind: 'payments',
        lines: raw
          .split(';')
          .filter(Boolean)
          .map((part) => {
            const [accountId = '', amount = '0'] = part.split(':');
            return { accountId, amount: Number(amount) };
          }),
      };
    case 'CreditPartyId':
      return { kind: 'changed' };
    default:
      return { kind: 'text', text: raw };
  }
}

export const MAX_CODE_ATTEMPTS = 5;

/**
 * Codes faux déjà comptés, lus dans l'historique (la fiche ne les donne pas) : chaque « Code faux » porte le
 * compteur après l'essai ; un déblocage le remet à zéro ; un nouveau code ne le change pas (règle validée).
 */
export function failedCodeAttempts(history: TransferEvent[] | undefined): number {
  let attempts = 0;
  for (const event of history ?? []) {
    if (event.type === 'WrongCode') attempts = wrongCodeAttempt(event) ?? attempts + 1;
    if (event.type === 'CodeUnlocked') attempts = 0;
  }
  return attempts;
}
