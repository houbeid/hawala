import { act, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { Transfer, TransferList, TransferSummary } from '@/features/transfers/types';
import { agentAccount, dkr, nkc } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';

// Temps réel (hub /hubs/live) : un faux hub injecté par le contexte émet les messages du serveur comme le vrai.
// On prouve : connexion seulement connecté et avec le jeton en mémoire ; relecture des données sur TransferChanged et
// RatesChanged (le message ne porte aucun montant) ; déconnexion en direct sur SignedOut / SessionClosed ; bandeau
// quand la connexion est coupée. Données fictives uniquement.

function transfer(reference: string): Transfer {
  return {
    id: `id-${reference}`,
    reference,
    status: 'NotDelivered',
    direction: 'Received',
    sourceAgencyId: dkr.id,
    sourceCurrency: 'XOF',
    destinationAgencyId: nkc.id,
    destinationCurrency: 'MRO',
    amountXof: 1_000_000,
    amountMro: 730_000,
    saleRate: 3650,
    purchaseRate: null,
    profitMro: null,
    senderInfo: 'Expéditeur Fictif',
    beneficiaryInfo: 'Bénéficiaire Fictif',
    note: null,
    operationAt: '2026-10-24T10:00:00Z',
    createdAt: '2026-10-24T10:00:00Z',
    payments: [],
    credit: null,
    deliveryOperationAt: null,
    isCodeLocked: false,
    deliveredWithoutCode: false,
    cancellationOperationAt: null,
    cancellationReason: null,
    version: 1,
  };
}

const summary: TransferSummary = {
  agencyId: nkc.id,
  currency: 'MRO',
  otherAgencyId: dkr.id,
  otherCurrency: 'XOF',
  from: '2026-10-24',
  to: '2026-10-24',
  incoming: { amount: 0, count: 0 },
  outgoing: { amount: 0, count: 0 },
  sentByOther: null,
  deliveredByOther: null,
  notDelivered: { amount: 0, count: 0 },
  profitMro: null,
  saleRate: 3650,
  purchaseRate: null,
};

/** Liste des transferts qui grandit quand l'autre agence en crée un : elle ne se voit qu'après relecture. */
function transfersServer() {
  const items = [transfer('TR-DKR-20261024-000001')];
  let reads = 0;
  server.use(
    http.get(apiUrl('/transfers'), () => {
      reads += 1;
      const body: TransferList = { items: [...items], page: 1, pageSize: 50, totalCount: items.length, allCount: items.length };
      return HttpResponse.json(body);
    }),
    http.get(apiUrl('/transfers/summary'), () => HttpResponse.json(summary)),
  );
  return {
    reads: () => reads,
    add: (reference: string) => items.unshift(transfer(reference)),
  };
}

describe('Temps réel', () => {
  beforeEach(() => signedInAs(agentAccount));

  it('se connecte une fois la session ouverte, avec le jeton gardé en mémoire', async () => {
    transfersServer();
    const { hub } = renderApp('/transfers');
    await screen.findByText('TR-DKR-20261024-000001');
    await waitFor(() => expect(hub.openCount()).toBe(1));
    expect(hub.tokens()).toEqual(['t1']);
  });

  it('un transfert créé par l’autre agence apparaît sans rafraîchir (la liste est relue)', async () => {
    const server = transfersServer();
    const { hub } = renderApp('/transfers');
    await screen.findByText('TR-DKR-20261024-000001');
    await waitFor(() => expect(hub.openCount()).toBe(1));
    const before = server.reads();

    server.add('TR-DKR-20261024-000002');
    act(() =>
      hub.emit('TransferChanged', {
        transferId: 'id-TR-DKR-20261024-000002',
        reference: 'TR-DKR-20261024-000002',
        event: 'Created',
        version: 1,
        status: 'NotDelivered',
        sourceAgencyId: dkr.id,
        destinationAgencyId: nkc.id,
      }),
    );

    expect(await screen.findByText('TR-DKR-20261024-000002')).toBeInTheDocument();
    expect(server.reads()).toBeGreaterThan(before);
  });

  it('un taux du jour changé est relu sur l’écran des taux', async () => {
    let reads = 0;
    server.use(
      http.get(apiUrl('/rates/:date'), ({ params }) => {
        reads += 1;
        return HttpResponse.json({ date: params.date, saleRate: reads === 1 ? 3650 : 3660, purchaseRate: null, updatedAt: '2026-10-24T08:00:00Z' });
      }),
      http.get(apiUrl('/rates'), () => HttpResponse.json([])),
    );
    const { hub } = renderApp('/rates');
    expect(await screen.findByText('5 000 FCFA = 3 650 MRO')).toBeInTheDocument();
    await waitFor(() => expect(hub.openCount()).toBe(1));

    act(() => hub.emit('RatesChanged', { date: '2026-10-24' }));
    expect(await screen.findByText('5 000 FCFA = 3 660 MRO')).toBeInTheDocument();
  });

  it('compte bloqué : retour à la connexion avec le motif, connexion temps réel fermée', async () => {
    transfersServer();
    const { hub, router } = renderApp('/transfers');
    await screen.findByText('TR-DKR-20261024-000001');
    await waitFor(() => expect(hub.openCount()).toBe(1));

    act(() => hub.emit('SignedOut', { accountId: agentAccount.id }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'));
    expect(await screen.findByText('Votre session a été fermée par le Superviseur. Reconnectez-vous ou contactez-le.')).toBeInTheDocument();
    await waitFor(() => expect(hub.openCount()).toBe(0));
  });

  it('session fermée depuis un autre appareil : retour à la connexion avec son motif', async () => {
    transfersServer();
    const { hub, router } = renderApp('/transfers');
    await screen.findByText('TR-DKR-20261024-000001');
    await waitFor(() => expect(hub.openCount()).toBe(1));

    act(() => hub.emit('SessionClosed', { sessionId: 's1' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'));
    expect(await screen.findByText('Cette session a été fermée depuis un autre appareil.')).toBeInTheDocument();
  });

  it('bandeau discret pendant une reconnexion, retiré une fois la connexion rétablie', async () => {
    transfersServer();
    const { hub } = renderApp('/transfers');
    await screen.findByText('TR-DKR-20261024-000001');
    await waitFor(() => expect(hub.openCount()).toBe(1));

    act(() => hub.status('reconnecting'));
    expect(await screen.findByText(/Mises à jour en direct interrompues/)).toBeInTheDocument();
    act(() => hub.status('connected'));
    await waitFor(() => expect(screen.queryByText(/Mises à jour en direct interrompues/)).not.toBeInTheDocument());
  });

  it('hub injoignable au départ : l’application marche, avec le bandeau « coupées »', async () => {
    transfersServer();
    const { hub } = renderApp('/transfers');
    hub.failStarts(1);
    expect(await screen.findByText('TR-DKR-20261024-000001')).toBeInTheDocument();
    expect(await screen.findByText(/Mises à jour en direct coupées/)).toBeInTheDocument();
    expect(hub.openCount()).toBe(0);
  });
});
