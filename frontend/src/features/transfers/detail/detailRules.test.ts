import { agentAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import type { Transfer, TransferEvent } from '../types';
import { availableActions, creatorName, failedCodeAttempts, readChangeValue, wrongCodeAttempt } from './detailRules';

const transfer = {
  id: 't1',
  status: 'NotDelivered',
  sourceAgencyId: nkc.id,
  sourceCurrency: 'MRO',
  destinationAgencyId: dkr.id,
  destinationCurrency: 'XOF',
  isCodeLocked: false,
} as Transfer;

const dakarAgent = { ...agentAccount, agency: { id: dkr.id, code: 'DKR', name: 'Dakar', color: '#1565C0' } };

describe('actions de la fiche selon le compte', () => {
  it("l'agence d'envoi modifie les textes et demande un nouveau code, sans annuler", () => {
    expect(availableActions(transfer, agentAccount)).toEqual({
      editTexts: true,
      cancel: false,
      newCode: true,
      unlock: false,
      editAmounts: false,
      deliver: false,
      deliverWithoutCode: false,
    });
  });

  it("l'agence de destination livre, sans aucune action de l'agence d'envoi", () => {
    expect(availableActions(transfer, dakarAgent)).toEqual({
      editTexts: false,
      cancel: false,
      newCode: false,
      unlock: false,
      editAmounts: false,
      deliver: true,
      deliverWithoutCode: false,
    });
    expect(availableActions({ ...transfer, isCodeLocked: true }, dakarAgent).deliver).toBe(false);
  });

  it('le Superviseur a tout, et débloque seulement un code bloqué', () => {
    expect(availableActions(transfer, supervisorAccount)).toEqual({
      editTexts: true,
      cancel: true,
      newCode: true,
      unlock: false,
      editAmounts: true,
      deliver: true,
      deliverWithoutCode: true,
    });
    expect(availableActions({ ...transfer, isCodeLocked: true }, supervisorAccount)).toMatchObject({
      unlock: true,
      deliver: false,
      deliverWithoutCode: true,
    });
  });

  it('un transfert livré ou annulé est verrouillé', () => {
    for (const status of ['Delivered', 'Cancelled'] as const) {
      expect(availableActions({ ...transfer, status }, supervisorAccount)).toEqual({
        editTexts: false,
        cancel: false,
        newCode: false,
        unlock: false,
        editAmounts: false,
        deliver: false,
        deliverWithoutCode: false,
      });
    }
  });
});

describe("lecture de l'historique", () => {
  const event = (type: TransferEvent['type'], changes: TransferEvent['changes'] = []): TransferEvent => ({
    type,
    at: '2026-10-22T14:32:00Z',
    authorId: 'a1',
    authorName: 'Agent Test',
    reason: null,
    changes,
  });

  it("donne l'auteur de la création et le numéro d'un code faux", () => {
    expect(creatorName([event('Created'), event('Edited')])).toBe('Agent Test');
    expect(wrongCodeAttempt(event('WrongCode', [{ field: 'FailedCodeAttempts', before: '1', after: '2' }]))).toBe(2);
  });

  it('lit les montants, la répartition et masque l’identifiant du tiers', () => {
    expect(readChangeValue({ field: 'AmountMro', before: '912500', after: '946400' }, 'after', transfer)).toEqual({
      kind: 'amount',
      value: 946400,
      currency: 'MRO',
    });
    expect(readChangeValue({ field: 'Payments', before: 'a:500000;b:412500', after: null }, 'before', transfer)).toEqual({
      kind: 'payments',
      lines: [
        { accountId: 'a', amount: 500000 },
        { accountId: 'b', amount: 412500 },
      ],
    });
    expect(readChangeValue({ field: 'CreditPartyId', before: 'x', after: 'y' }, 'after', transfer)).toEqual({ kind: 'changed' });
    expect(readChangeValue({ field: 'Note', before: null, after: 'Frais' }, 'before', transfer)).toEqual({ kind: 'empty' });
  });
});

describe('codes faux déjà comptés', () => {
  const ev = (type: TransferEvent['type'], after?: string): TransferEvent => ({
    type,
    at: '2026-10-22T15:00:00Z',
    authorId: 'a',
    authorName: 'A',
    reason: null,
    changes: after ? [{ field: 'FailedCodeAttempts', before: null, after }] : [],
  });

  it('suit le compteur, remis à zéro par un déblocage, inchangé par un nouveau code', () => {
    expect(failedCodeAttempts([ev('Created'), ev('WrongCode', '1'), ev('WrongCode', '2')])).toBe(2);
    expect(failedCodeAttempts([ev('WrongCode', '5'), ev('CodeUnlocked'), ev('WrongCode', '1')])).toBe(1);
    expect(failedCodeAttempts([ev('WrongCode', '2'), ev('CodeReplaced')])).toBe(2);
    expect(failedCodeAttempts(undefined)).toBe(0);
  });
});
