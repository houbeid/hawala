import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { PartySummary } from '@/features/parties';
import type { TreasuryAccount } from '@/features/treasury/types';
import { agentAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { stubDownloads } from '@/test/downloads';
import { exportServer } from '@/test/exportServer';
import { apiUrl, server } from '@/test/server';
import type { CashMovement, CashMovementList } from '../types';

// Écran Caisse, livraison 1 : liste et création des cinq opérations. Données fictives uniquement.

const accounts: TreasuryAccount[] = [
  { id: 'cash', name: 'Espèces', type: 'Cash', agencyId: nkc.id, currency: 'MRO', isActive: true, balance: 100_000 },
  { id: 'bank', name: 'Wallet fictif', type: 'Wallet', agencyId: nkc.id, currency: 'MRO', isActive: true, balance: 0 },
];

const client: PartySummary = {
  id: 'p-1',
  name: 'Boutique Fictive',
  type: 'Client',
  accountCode: '41',
  phone: null,
  nni: null,
  address: null,
  note: null,
  inBalanceSheet: true,
  isArchived: false,
  balances: null,
};

function movement(overrides: Partial<CashMovement>): CashMovement {
  return {
    id: 'm',
    agencyId: nkc.id,
    type: 'Receipt',
    incoming: true,
    isInternal: false,
    treasuryAccountId: 'cash',
    treasuryAccountName: 'Espèces',
    destinationTreasuryAccountId: null,
    destinationTreasuryAccountName: null,
    partyId: 'p-1',
    partyName: 'Boutique Fictive',
    amount: 300_000,
    currency: 'MRO',
    note: null,
    operationAt: '2026-10-24T14:40:00Z',
    createdAt: '2026-10-24T14:40:00Z',
    createdBy: 'u',
    status: 'Active',
    cancellationReason: null,
    ...overrides,
  };
}

const list: CashMovementList = {
  items: [
    movement({ id: 'm1' }),
    movement({ id: 'm2', type: 'Payment', incoming: false, amount: 150_000, partyName: 'Fournisseur Fictif' }),
    movement({
      id: 'm3',
      type: 'InternalTransfer',
      incoming: false,
      isInternal: true,
      partyId: null,
      partyName: null,
      destinationTreasuryAccountId: 'bank',
      destinationTreasuryAccountName: 'Wallet fictif',
      amount: 200_000,
    }),
    movement({ id: 'm4', status: 'Cancelled', cancellationReason: 'Montant saisi en double' }),
  ],
  page: 1,
  pageSize: 50,
  totalCount: 4,
  allCount: 12,
  incoming: { amount: 300_000, count: 1 },
  outgoing: { amount: 150_000, count: 1 },
  net: 150_000,
  internalTransfers: { amount: 200_000, count: 1 },
};

/** Le serveur de la caisse : liste, comptes, tiers et création (corps et clé d'idempotence notés). */
function cashServer(options: { createFails?: 'network' | string } = {}) {
  const queries: URLSearchParams[] = [];
  const posts: { body: unknown; key: string | null }[] = [];
  let failures = options.createFails ? 1 : 0;
  server.use(
    http.get(apiUrl('/cash-movements'), ({ request }) => {
      queries.push(new URL(request.url).searchParams);
      return HttpResponse.json(list);
    }),
    http.get(apiUrl('/treasury-accounts'), () => HttpResponse.json(accounts)),
    http.get(apiUrl('/balance-adjustments'), () => HttpResponse.json([])),
    http.get(apiUrl('/parties'), () => HttpResponse.json({ items: [client], count: 1, totalCount: 1, totals: null })),
    http.post(apiUrl('/cash-movements'), async ({ request }) => {
      posts.push({ body: await request.json(), key: request.headers.get('Idempotency-Key') });
      if (failures > 0) {
        failures -= 1;
        if (options.createFails === 'network') return HttpResponse.error();
        return HttpResponse.json({ code: options.createFails }, { status: 400 });
      }
      return HttpResponse.json({ id: 'new', type: 'Receipt', amount: 1, currency: 'MRO', replayed: false }, { status: 201 });
    }),
  );
  return { queries, posts };
}

describe('Caisse — liste', () => {
  beforeEach(() => signedInAs(agentAccount));

  it('montre les totaux, chaque mouvement avec son sens, et les annulés barrés avec leur motif', async () => {
    const { queries } = cashServer();
    renderApp('/cash');

    expect(await screen.findByText('Entrées')).toBeInTheDocument();
    expect(screen.getByText('Net').nextElementSibling).toHaveTextContent('+ 150 000 MRO');
    expect(screen.getByText('Virement interne : 1 · 200 000 MRO (sans effet sur le net)')).toBeInTheDocument();
    expect(screen.getByText('4 / 12 mouvements')).toBeInTheDocument();
    const rows = screen.getAllByRole('listitem');
    expect(within(rows[0]!).getByText('Encaissement')).toBeInTheDocument();
    expect(within(rows[0]!).getByText(/\+ 300 000 MRO/)).toBeInTheDocument();
    expect(within(rows[1]!).getByText(/− 150 000 MRO/)).toBeInTheDocument();
    expect(within(rows[2]!).getByText('Espèces → Wallet fictif')).toBeInTheDocument();
    expect(within(rows[2]!).queryByText(/[+−] 200 000/)).not.toBeInTheDocument();
    expect(within(rows[3]!).getByText('Annulé')).toBeInTheDocument();
    expect(within(rows[3]!).getByText('Motif : Montant saisi en double')).toBeInTheDocument();
    expect(queries[0]?.get('agencyId')).toBe(nkc.id);
    expect(queries[0]?.has('from')).toBe(false);
  });

  it('envoie les filtres au serveur et les garde dans l’adresse', async () => {
    const { queries } = cashServer();
    const { user, router } = renderApp('/cash');
    await screen.findByText('Entrées');

    await user.selectOptions(screen.getByLabelText('Type'), 'Payment');
    await user.selectOptions(screen.getByLabelText('Compte'), 'cash');
    await user.type(screen.getByRole('searchbox', { name: 'Rechercher dans les notes' }), 'loyer');

    await waitFor(() => expect(queries.at(-1)?.get('search')).toBe('loyer'));
    expect(queries.at(-1)?.get('type')).toBe('Payment');
    expect(queries.at(-1)?.get('treasuryAccountId')).toBe('cash');
    expect(router.state.location.search).toMatchObject({ type: 'Payment', account: 'cash', search: 'loyer' });
  });
});

describe('Caisse — Superviseur sur les deux agences', () => {
  it('fait choisir la caisse et envoie toujours une agence', async () => {
    signedInAs(supervisorAccount);
    const { queries } = cashServer();
    const { user } = renderApp('/cash');

    await screen.findByText('Entrées');
    expect(queries[0]?.get('agencyId')).toBe(nkc.id);
    await user.click(screen.getByRole('button', { name: 'DKR' }));
    await waitFor(() => expect(queries.at(-1)?.get('agencyId')).toBe(dkr.id));
    expect(screen.getByRole('button', { name: 'DKR' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('exporte la caisse affichée en Excel, avec ses filtres', async () => {
    signedInAs(supervisorAccount);
    cashServer();
    const exports = exportServer('/cash-movements/export');
    const downloads = stubDownloads();
    const { user } = renderApp('/cash?from=2026-10-01&to=2026-10-24&type=Receipt');

    await user.click(await screen.findByRole('button', { name: 'Exporter' }));

    expect(await screen.findByText('Fichier Excel téléchargé.')).toBeInTheDocument();
    expect(exports).toEqual([{ agencyId: nkc.id, from: '2026-10-01', to: '2026-10-24', type: 'Receipt', lang: 'fr' }]);
    expect(downloads.files).toEqual(['Caisse-20261001-20261024.xlsx']);
    downloads.restore();
  });
});

describe('Caisse — nouveau mouvement', () => {
  beforeEach(() => signedInAs(agentAccount));

  async function open(path = '/cash/new') {
    const view = renderApp(path);
    await screen.findAllByRole('option', { name: /Espèces/ });
    return view;
  }

  it('encaissement : compte, tiers et montant ; envoyé avec une clé d’idempotence, puis retour à la caisse', async () => {
    const { posts } = cashServer();
    const { user, router } = await open();

    await user.selectOptions(screen.getByLabelText('Compte de trésorerie'), 'cash');
    await user.type(screen.getByRole('searchbox'), 'Bout');
    await user.click(await screen.findByRole('button', { name: /Boutique Fictive/ }));
    await user.type(screen.getByLabelText('Montant'), '300000');
    await user.click(screen.getByRole('button', { name: 'Enregistrer' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/cash'));
    expect(posts).toHaveLength(1);
    expect(posts[0]?.key).toBeTruthy();
    expect(posts[0]?.body).toEqual({
      agencyId: null,
      type: 'Receipt',
      treasuryAccountId: 'cash',
      partyId: 'p-1',
      amount: 300_000,
      unit: null,
      note: null,
      operationAt: null,
      destinationTreasuryAccountId: null,
    });
  });

  it('refuse un encaissement sans tiers avant tout envoi', async () => {
    const { posts } = cashServer();
    const { user } = await open();
    await user.selectOptions(screen.getByLabelText('Compte de trésorerie'), 'cash');
    await user.type(screen.getByLabelText('Montant'), '1000');
    await user.click(screen.getByRole('button', { name: 'Enregistrer' }));

    expect(await screen.findByText('Le tiers est obligatoire pour un encaissement ou un décaissement.')).toBeInTheDocument();
    expect(posts).toEqual([]);
  });

  it('retrait du patron en MRU : converti, sans tiers, avec l’avertissement de découvert', async () => {
    const { posts } = cashServer();
    const { user } = await open('/cash/new?type=OwnerWithdrawal');
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Compte de trésorerie'), 'cash');
    await user.click(screen.getByRole('button', { name: 'MRU' }));
    await user.type(screen.getByLabelText('Montant'), '12500');
    expect(screen.getByText('= 125 000 MRO')).toBeInTheDocument();
    expect(screen.getByText('Le compte « Espèces » passera en négatif : − 25 000 MRO. Vous pouvez continuer.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Enregistrer' }));

    await waitFor(() => expect(posts).toHaveLength(1));
    expect(posts[0]?.body).toMatchObject({ type: 'OwnerWithdrawal', partyId: null, amount: 125_000, unit: null });
  });

  it('virement interne : vers un autre compte de l’agence', async () => {
    const { posts } = cashServer();
    const { user } = await open('/cash/new?type=InternalTransfer');

    await user.selectOptions(screen.getByLabelText('Depuis le compte'), 'cash');
    const to = screen.getByLabelText('Vers le compte');
    expect(within(to).queryByRole('option', { name: 'Espèces' })).not.toBeInTheDocument();
    await user.selectOptions(to, 'bank');
    await user.type(screen.getByLabelText('Montant'), '50000');
    await user.click(screen.getByRole('button', { name: 'Enregistrer' }));

    await waitFor(() => expect(posts).toHaveLength(1));
    expect(posts[0]?.body).toMatchObject({ type: 'InternalTransfer', treasuryAccountId: 'cash', destinationTreasuryAccountId: 'bank' });
  });

  it('après une coupure, renvoie avec la même clé ; un refus du serveur est traduit', async () => {
    const { posts } = cashServer({ createFails: 'network' });
    const { user, router } = await open('/cash/new?type=OwnerContribution');
    await user.selectOptions(screen.getByLabelText('Compte de trésorerie'), 'cash');
    await user.type(screen.getByLabelText('Montant'), '1000');

    await user.click(screen.getByRole('button', { name: 'Enregistrer' }));
    await screen.findByRole('alert');
    await user.click(screen.getByRole('button', { name: 'Enregistrer' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/cash'));
    expect(posts).toHaveLength(2);
    expect(posts[1]?.key).toBe(posts[0]?.key);
  });

  it('traduit un refus métier du serveur', async () => {
    cashServer({ createFails: 'Cash.Movement.AccountNotAllowed' });
    const { user } = await open('/cash/new?type=OwnerContribution');
    await user.selectOptions(screen.getByLabelText('Compte de trésorerie'), 'cash');
    await user.type(screen.getByLabelText('Montant'), '1000');
    await user.click(screen.getByRole('button', { name: 'Enregistrer' }));

    expect(await screen.findByText('Compte inconnu, désactivé ou d’une autre agence.')).toBeInTheDocument();
  });
});
