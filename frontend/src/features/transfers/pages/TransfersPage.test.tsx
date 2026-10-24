import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { agentAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { stubDownloads } from '@/test/downloads';
import { apiUrl, server } from '@/test/server';
import type { Transfer, TransferList, TransferSummary } from '../types';

function transfer(overrides: Partial<Transfer> = {}): Transfer {
  return {
    id: '01900000-0000-7000-8000-00000000t001',
    reference: 'TR-NKC-20261022-000043',
    status: 'NotDelivered',
    direction: 'Sent',
    sourceAgencyId: nkc.id,
    sourceCurrency: 'MRO',
    destinationAgencyId: dkr.id,
    destinationCurrency: 'XOF',
    amountXof: 1250000,
    amountMro: 912500,
    saleRate: 3650,
    purchaseRate: null,
    profitMro: null,
    senderInfo: 'Expéditeur Test',
    beneficiaryInfo: 'Bénéficiaire Test',
    note: null,
    operationAt: '2026-10-22T14:32:00Z',
    createdAt: '2026-10-22T14:32:05Z',
    payments: [],
    credit: null,
    deliveryOperationAt: null,
    isCodeLocked: false,
    deliveredWithoutCode: false,
    cancellationOperationAt: null,
    cancellationReason: null,
    version: 1,
    ...overrides,
  };
}

function page(items: Transfer[], overrides: Partial<TransferList> = {}): TransferList {
  return { items, page: 1, pageSize: 50, totalCount: items.length, allCount: 392, ...overrides };
}

const agentSummary: TransferSummary = {
  agencyId: nkc.id,
  currency: 'MRO',
  otherAgencyId: dkr.id,
  otherCurrency: 'XOF',
  from: '2026-10-22',
  to: '2026-10-22',
  incoming: { amount: 2850000, count: 12 },
  outgoing: { amount: 2190000, count: 9 },
  sentByOther: null,
  deliveredByOther: null,
  notDelivered: { amount: 640000, count: 7 },
  profitMro: null,
  saleRate: 3650,
  purchaseRate: null,
};

/** Liste qui enregistre les paramètres reçus (pour vérifier ce que la page envoie au serveur). */
function listServer(result: TransferList | ((url: URL) => TransferList)) {
  const calls: URLSearchParams[] = [];
  server.use(
    http.get(apiUrl('/transfers'), ({ request }) => {
      const url = new URL(request.url);
      calls.push(url.searchParams);
      return HttpResponse.json(typeof result === 'function' ? result(url) : result);
    }),
  );
  return calls;
}

describe('Liste des transferts — Agent', () => {
  beforeEach(() => {
    signedInAs(agentAccount);
    server.use(http.get(apiUrl('/transfers/summary'), () => HttpResponse.json(agentSummary)));
  });

  it('montre les 4 cartes de l’agent, sans bénéfice ni chiffres de l’autre agence', async () => {
    listServer(page([transfer()]));
    renderApp('/transfers');

    const cards = await screen.findByRole('region', { name: 'Chiffres de la période' });
    expect(within(cards).getByText('Total acheté')).toBeInTheDocument();
    expect(within(cards).getByText('2 850 000 MRO')).toBeInTheDocument();
    expect(within(cards).getByText('Total vendu')).toBeInTheDocument();
    expect(within(cards).getByText('Non livrés')).toBeInTheDocument();
    expect(within(cards).getByText('7 transferts')).toBeInTheDocument();
    expect(within(cards).getByText('Prix de vente')).toBeInTheDocument();
    expect(within(cards).queryByText(/Bénéfice/)).not.toBeInTheDocument();
    expect(within(cards).queryByText(/Montant livré/)).not.toBeInTheDocument();
  });

  it('affiche une carte par transfert : montant à livrer, sens, statut, parties, compteur', async () => {
    listServer(page([transfer()], { totalCount: 24, allCount: 392 }));
    renderApp('/transfers');

    const card = await screen.findByRole('link', { name: /TR-NKC-20261022-000043/ });
    expect(within(card).getByText('1 250 000 FCFA')).toBeInTheDocument();
    expect(within(card).getByText('Non livré')).toBeInTheDocument();
    expect(within(card).getByText('Nouakchott → Dakar')).toBeInTheDocument();
    expect(within(card).getByText('Envoyé')).toBeInTheDocument();
    expect(within(card).getByText('14:32')).toBeInTheDocument();
    expect(within(card).getByText('Bénéficiaire Test')).toBeInTheDocument();
    expect(card).toHaveAttribute('href', '/transfers/01900000-0000-7000-8000-00000000t001');
    expect(screen.getByText('24 / 392 transferts')).toBeInTheDocument();
  });

  it('montre en MRO le montant à livrer d’un transfert reçu de Dakar', async () => {
    listServer(
      page([
        transfer({
          id: 't2',
          reference: 'TR-DKR-20261022-000018',
          direction: 'Received',
          sourceAgencyId: dkr.id,
          sourceCurrency: 'XOF',
          destinationAgencyId: nkc.id,
          destinationCurrency: 'MRO',
          amountXof: 400000,
          amountMro: 292000,
        }),
      ]),
    );
    renderApp('/transfers');

    const card = await screen.findByRole('link', { name: /TR-DKR-20261022-000018/ });
    expect(within(card).getByText('292 000 MRO')).toBeInTheDocument();
    expect(within(card).getByText('Reçu')).toBeInTheDocument();
  });

  it('filtre par statut : l’adresse change, le serveur reçoit le filtre, une puce permet de le retirer', async () => {
    const calls = listServer(page([transfer()]));
    const { user, router } = renderApp('/transfers');
    await screen.findByRole('link', { name: /TR-NKC/ });

    await user.click(screen.getByRole('button', { name: 'Filtres' }));
    await user.selectOptions(screen.getByLabelText('Statut'), 'Non livré');

    await waitFor(() => expect(router.state.location.search).toEqual({ status: 'NotDelivered' }));
    await waitFor(() => expect(calls.at(-1)?.get('status')).toBe('NotDelivered'));
    expect(screen.getByRole('button', { name: 'Filtres · 1' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Non livré.*retirer ce filtre/ }));
    await waitFor(() => expect(router.state.location.search).toEqual({}));
  });

  it('envoie la recherche après une pause de frappe', async () => {
    const calls = listServer(page([transfer()]));
    const { user, router } = renderApp('/transfers');
    await screen.findByRole('link', { name: /TR-NKC/ });

    await user.type(screen.getByRole('searchbox', { name: 'Recherche' }), 'Test');

    await waitFor(() => expect(router.state.location.search).toEqual({ search: 'Test' }));
    expect(calls.at(-1)?.get('search')).toBe('Test');
  });

  it('dit clairement quand rien ne correspond', async () => {
    listServer(page([], { totalCount: 0 }));
    renderApp('/transfers?status=Cancelled');

    expect(await screen.findByText('Aucun transfert')).toBeInTheDocument();
  });

  it('sans la permission export, pas de bouton « Exporter »', async () => {
    listServer(page([transfer()]));
    renderApp('/transfers');

    await screen.findByRole('link', { name: /TR-NKC/ });
    expect(screen.queryByRole('button', { name: 'Exporter' })).not.toBeInTheDocument();
  });

  it('pagine par 50', async () => {
    const calls = listServer((url) => page([transfer({ reference: `TR-PAGE-${url.searchParams.get('page') ?? '1'}` })], { totalCount: 120, page: Number(url.searchParams.get('page') ?? 1) }));
    const { user } = renderApp('/transfers');

    expect(await screen.findByText('Page 1 sur 3')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Suivants' }));

    expect(await screen.findByText('Page 2 sur 3')).toBeInTheDocument();
    expect(calls.at(-1)?.get('page')).toBe('2');
  });
});

describe('Liste des transferts — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('exporte en Excel avec les filtres de l’écran et la langue de l’interface', async () => {
    listServer(page([transfer({ direction: null })]));
    const exports: URLSearchParams[] = [];
    server.use(
      http.get(apiUrl('/transfers/export'), ({ request }) => {
        exports.push(new URL(request.url).searchParams);
        return new HttpResponse(new Uint8Array([0x50, 0x4b]), {
          headers: { 'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
        });
      }),
    );
    const downloads = stubDownloads();
    const { user } = renderApp('/transfers?from=2026-10-01&to=2026-10-22&status=Delivered&page=2');

    await user.click(await screen.findByRole('button', { name: 'Exporter' }));

    expect(await screen.findByText('Fichier Excel téléchargé.')).toBeInTheDocument();
    expect(Object.fromEntries(exports[0]!)).toEqual({ from: '2026-10-01', to: '2026-10-22', status: 'Delivered', lang: 'fr' });
    expect(downloads.files).toEqual(['Transferts-20261001-20261022.xlsx']);
    downloads.restore();
  });

  it('explique un export trop gros', async () => {
    listServer(page([transfer({ direction: null })]));
    server.use(
      http.get(apiUrl('/transfers/export'), () =>
        HttpResponse.json({ status: 400, code: 'Api.Export.TooLarge', maxRows: 10000 }, { status: 400 }),
      ),
    );
    const { user } = renderApp('/transfers');

    await user.click(await screen.findByRole('button', { name: 'Exporter' }));
    expect(await screen.findByText('Plus de 10000 lignes : réduisez la période ou ajoutez un filtre.')).toBeInTheDocument();
  });

  it('sur « les deux agences » : tous les transferts, cartes après le choix d’une agence, sans filtre de sens', async () => {
    const calls = listServer(page([transfer({ direction: null })]));
    let summaryAgency: string | null = null;
    server.use(
      http.get(apiUrl('/transfers/summary'), ({ request }) => {
        summaryAgency = new URL(request.url).searchParams.get('agencyId');
        return HttpResponse.json({
          ...agentSummary,
          sentByOther: { amount: 3900000, count: 10 },
          deliveredByOther: { amount: 2750000, count: 8 },
          profitMro: 48300.4,
          purchaseRate: 3600,
        });
      }),
    );
    const { user } = renderApp('/transfers');

    await screen.findByRole('link', { name: /TR-NKC/ });
    expect(calls.at(-1)?.get('agencyId')).toBeNull();
    expect(screen.queryByRole('region', { name: 'Chiffres de la période' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Filtres' }));
    expect(screen.queryByLabelText('Sens')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'NKC' }));

    const cards = await screen.findByRole('region', { name: 'Chiffres de la période' });
    expect(summaryAgency).toBe(nkc.id);
    expect(within(cards).getByText('Entrées · NKC')).toBeInTheDocument();
    expect(within(cards).getByText('Montant livré · DKR')).toBeInTheDocument();
    expect(within(cards).getByText('2 750 000 FCFA')).toBeInTheDocument();
    expect(within(cards).getByText('+ 48 300 MRO')).toBeInTheDocument();
    expect(screen.getByLabelText('Sens')).toBeInTheDocument();
  });
});
