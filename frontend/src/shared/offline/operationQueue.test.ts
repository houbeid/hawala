import { ApiError, ClientErrorCodes } from '../api/errors';
import type { HttpClient } from '../api/http';
import { createMemoryStore } from './offlineStore';
import { OperationQueue } from './operationQueue';

// File des créations hors ligne : envoi dans l'ordre, avec la clé de chaque opération ; réseau coupé = arrêt sans rien
// perdre ; refus métier = « rejetée » et la suivante part ; résultat gardé seulement s'il doit être montré ; la file
// survit à la fermeture de l'app et n'envoie que les opérations du compte connecté.

const http = {} as HttpClient;
const networkError = () => new ApiError({ status: 0, code: ClientErrorCodes.Network });

function queueWith(send: (request: unknown, key: string) => Promise<unknown>, store = createMemoryStore()) {
  const sent: string[] = [];
  const queue = new OperationQueue(
    store,
    {
      transfer: { send: (_http, request, key) => send(request, key), keepResult: () => true },
      cash: { send: (_http, request, key) => send(request, key) },
    },
    (kind) => sent.push(kind),
  );
  return { queue, store, sent };
}

const op = (id: string, kind = 'cash', accountId = 'a1') => ({ id, accountId, kind, request: { n: id }, label: id });

describe('OperationQueue', () => {
  it('envoie dans l’ordre de saisie, avec la clé de chaque opération', async () => {
    const calls: string[] = [];
    const { queue, sent } = queueWith(async (_request, key) => {
      calls.push(key);
      return {};
    });
    await queue.enqueue(op('k1'));
    await queue.enqueue(op('k2'));

    await queue.flush(http, 'a1');

    expect(calls).toEqual(['k1', 'k2']);
    expect(queue.forAccount('a1')).toEqual([]);
    expect(sent).toEqual(['cash', 'cash']);
  });

  it('réseau coupé : s’arrête, rien n’est perdu, l’ordre est gardé', async () => {
    const { queue } = queueWith(async () => {
      throw networkError();
    });
    await queue.enqueue(op('k1'));
    await queue.enqueue(op('k2'));

    await queue.flush(http, 'a1');

    expect(queue.forAccount('a1').map((o) => [o.id, o.status])).toEqual([
      ['k1', 'pending'],
      ['k2', 'pending'],
    ]);
  });

  it('refus du serveur : l’opération est rejetée avec son code, la suivante part', async () => {
    const { queue } = queueWith(async (request) => {
      if ((request as { n: string }).n === 'k1') {
        throw new ApiError({ status: 409, code: 'Transfers.Limits.PerTransfer', details: { remaining: 10, currency: 'MRO' } });
      }
      return {};
    });
    await queue.enqueue(op('k1'));
    await queue.enqueue(op('k2'));

    await queue.flush(http, 'a1');

    const [first, ...others] = queue.forAccount('a1');
    expect(first).toMatchObject({ id: 'k1', status: 'rejected', error: { status: 409, code: 'Transfers.Limits.PerTransfer', details: { remaining: 10 } } });
    expect(others).toEqual([]);
  });

  it('serveur en panne (5xx) : s’arrête et réessaiera plus tard', async () => {
    const { queue } = queueWith(async () => {
      throw new ApiError({ status: 503, code: 'Client.Unknown' });
    });
    await queue.enqueue(op('k1'));

    await queue.flush(http, 'a1');

    expect(queue.forAccount('a1')[0]?.status).toBe('pending');
  });

  it('un transfert envoyé garde sa réponse (code de retrait) jusqu’à ce qu’il soit retiré', async () => {
    const { queue } = queueWith(async () => ({ reference: 'TR-1', withdrawalCode: '123456' }));
    await queue.enqueue(op('k1', 'transfer'));

    await queue.flush(http, 'a1');

    expect(queue.forAccount('a1')[0]).toMatchObject({ status: 'sent', result: { withdrawalCode: '123456' } });
    await queue.remove('k1');
    expect(queue.forAccount('a1')).toEqual([]);
  });

  it('n’envoie que les opérations du compte connecté', async () => {
    const calls: string[] = [];
    const { queue } = queueWith(async (_request, key) => {
      calls.push(key);
      return {};
    });
    await queue.enqueue(op('mine'));
    await queue.enqueue(op('other', 'cash', 'a2'));

    await queue.flush(http, 'a1');

    expect(calls).toEqual(['mine']);
    expect(queue.forAccount('a2')).toHaveLength(1);
  });

  it('survit à la fermeture de l’app ; une même clé n’est jamais ajoutée deux fois', async () => {
    const store = createMemoryStore();
    const first = queueWith(async () => ({}), store).queue;
    await first.enqueue(op('k1'));
    await first.enqueue(op('k1'));

    const reopened = queueWith(async () => ({}), store).queue;
    await reopened.load();

    expect(reopened.forAccount('a1').map((o) => o.id)).toEqual(['k1']);
  });
});
