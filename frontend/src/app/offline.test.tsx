import { act, cleanup, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { Transfer, TransferList, TransferSummary } from '@/features/transfers/types';
import { todayIso } from '@/shared/format/dates';
import { createMemoryStore, PROFILE_KEY, QUERIES_KEY } from '@/shared/offline/offlineStore';
import { agentAccount, dkr, nkc } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';

// Lecture hors ligne, toute l'application (décisions du 24 octobre 2026) : en ligne, les données sont préchargées et
// gardées ; rouverte sans réseau, l'app les montre en lecture seule avec l'heure des données, sans aucune action
// serveur ; au retour du réseau, la session reprend seule. Données fictives uniquement.

const today = todayIso();

function transfer(reference: string): Transfer {
  return {
    id: `id-${reference}`,
    reference,
    status: 'NotDelivered',
    direction: 'Sent',
    sourceAgencyId: nkc.id,
    sourceCurrency: 'MRO',
    destinationAgencyId: dkr.id,
    destinationCurrency: 'XOF',
    amountXof: 1_000_000,
    amountMro: 730_000,
    saleRate: 3650,
    purchaseRate: null,
    profitMro: null,
    senderInfo: 'Expéditeur Fictif',
    beneficiaryInfo: 'Bénéficiaire Fictif',
    note: null,
    operationAt: `${today}T10:00:00Z`,
    createdAt: `${today}T10:00:00Z`,
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
  from: today,
  to: today,
  incoming: { amount: 730_000, count: 1 },
  outgoing: { amount: 0, count: 0 },
  sentByOther: null,
  deliveredByOther: null,
  notDelivered: { amount: 730_000, count: 1 },
  profitMro: null,
  saleRate: 3650,
  purchaseRate: null,
};

/** Serveur joignable : tout ce que l'app précharge. Garde les adresses demandées. */
function onlineServer() {
  const asked: string[] = [];
  const list: TransferList = { items: [transfer('TR-NKC-20261024-000077')], page: 1, pageSize: 50, totalCount: 1, allCount: 1 };
  server.use(
    http.get(apiUrl('/transfers'), ({ request }) => {
      asked.push(new URL(request.url).search);
      return HttpResponse.json(list);
    }),
    http.get(apiUrl('/transfers/summary'), () => HttpResponse.json(summary)),
    http.get(apiUrl('/rates/:date'), ({ params }) => HttpResponse.json({ date: params.date, saleRate: 3650, purchaseRate: null, updatedAt: `${today}T07:00:00Z` })),
    http.get(apiUrl('/treasury-accounts'), () => HttpResponse.json([])),
    http.get(apiUrl('/parties'), () => HttpResponse.json({ items: [], count: 0, totalCount: 0, totals: [] })),
  );
  return asked;
}

/** Plus aucun réseau : toute requête échoue. */
const noNetwork = () => server.use(http.all(apiUrl('*'), () => HttpResponse.error()));

describe('Lecture hors ligne', () => {
  it('précharge et garde les données, puis les montre en lecture seule quand l’app est rouverte sans réseau', async () => {
    signedInAs(agentAccount);
    const asked = onlineServer();
    const store = createMemoryStore();
    renderApp('/transfers', { offlineStore: store });

    expect(await screen.findByText('TR-NKC-20261024-000077')).toBeInTheDocument();
    // Préchargement des 7 derniers jours, puis sauvegarde (regroupée) dans le stockage du téléphone.
    await waitFor(() => expect(asked.some((q) => q.includes('from=') && q.includes('to='))).toBe(true));
    await waitFor(() => expect(JSON.stringify(store.data.get(QUERIES_KEY) ?? null)).toContain('TR-NKC-20261024-000077'), { timeout: 8_000 });
    expect(store.data.get(PROFILE_KEY)).toMatchObject({ account: { id: agentAccount.id } });

    cleanup();
    noNetwork();
    renderApp('/transfers', { offlineStore: store });

    expect(await screen.findByText(/Hors ligne : lecture seule des données gardées le/)).toBeInTheDocument();
    expect(await screen.findByText('TR-NKC-20261024-000077')).toBeInTheDocument();
  });

  it('au retour du réseau, la session reprend et le bandeau disparaît', async () => {
    noNetwork();
    const store = createMemoryStore({ [PROFILE_KEY]: { account: agentAccount, lastContactAt: new Date().toISOString() } });
    const { session } = renderApp('/transfers', { offlineStore: store });
    expect(await screen.findByText(/Hors ligne : lecture seule/)).toBeInTheDocument();

    server.resetHandlers();
    signedInAs(agentAccount);
    onlineServer();
    act(() => void window.dispatchEvent(new Event('online')));

    await waitFor(() => expect(session.getState().status).toBe('authenticated'));
    expect(screen.queryByText(/Hors ligne : lecture seule/)).not.toBeInTheDocument();
  });

  it('hors ligne, une action serveur est remplacée par son explication', async () => {
    noNetwork();
    const store = createMemoryStore({ [PROFILE_KEY]: { account: agentAccount, lastContactAt: new Date().toISOString() } });
    renderApp('/transfers/new', { offlineStore: store });

    expect(await screen.findByText(/Hors ligne : lecture seule/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Créer le transfert' })).not.toBeInTheDocument();
  });
});
