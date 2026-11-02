import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { PartySummary } from '@/features/parties';
import { agentAccount, cashierAccount, nkc } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { CashMovementList } from '../types';

// Transfert de solde entre tiers (décisions du patron, 2 novembre 2026) : Superviseur et Agent, même agence, sans
// motif, plafond du tiers débité. Données fictives uniquement.

function party(id: string, name: string, balance: number, creditLimit: number | null): PartySummary {
  return {
    id,
    name,
    type: 'Client',
    accountCode: '41',
    phone: null,
    nni: null,
    address: null,
    note: null,
    inBalanceSheet: true,
    isArchived: false,
    homeAgencyId: null,
    balances: [{ agencyId: nkc.id, currency: 'MRO', balance, creditLimit, openingBalance: 0 }],
  };
}

const depositor = party('p-a', 'Déposant Fictif', -1_000_000, 0);
const receiver = party('p-b', 'Receveur Fictif', 0, null);

const emptyList: CashMovementList = {
  items: [],
  page: 1,
  pageSize: 50,
  totalCount: 0,
  allCount: 0,
  incoming: { amount: 0, count: 0 },
  outgoing: { amount: 0, count: 0 },
  net: 0,
  internalTransfers: { amount: 0, count: 0 },
};

function transferServer(post?: () => Response) {
  const calls: { body: unknown; key: string | null }[] = [];
  server.use(
    http.get(apiUrl('/cash-movements'), () => HttpResponse.json(emptyList)),
    http.get(apiUrl('/treasury-accounts'), () => HttpResponse.json([])),
    http.get(apiUrl('/balance-adjustments'), () => HttpResponse.json([])),
    http.get(apiUrl('/parties'), () =>
      HttpResponse.json({ items: [depositor, receiver], count: 2, totalCount: 2, totals: null }),
    ),
    http.post(apiUrl('/party-transfers'), async ({ request }) => {
      calls.push({ body: await request.json(), key: request.headers.get('Idempotency-Key') });
      return post ? post() : HttpResponse.json({ id: 't-1', amount: 400_000, currency: 'MRO', replayed: false }, { status: 201 });
    }),
  );
  return { calls };
}

async function choose(user: Awaited<ReturnType<typeof renderApp>>['user'], dialog: HTMLElement, label: string, name: string) {
  // Chaque sélecteur a sa propre liste de résultats, reliée à son champ de recherche.
  const input = within(dialog).getByLabelText(label);
  await user.type(input, name.slice(0, 4));
  const list = document.getElementById(input.getAttribute('aria-controls') ?? '')!;
  await user.click(await within(list).findByRole('button', { name: new RegExp(name) }));
}

describe('Caisse — transfert de solde entre tiers', () => {
  beforeEach(() => signedInAs(agentAccount));

  it('l’agent transfère un solde : soldes avant / après en mots, note facultative, clé d’idempotence', async () => {
    const { calls } = transferServer();
    const { user } = renderApp('/cash');
    await user.click(await screen.findByRole('button', { name: 'Transfert de solde' }));

    const dialog = await screen.findByRole('dialog');
    await choose(user, dialog, 'Du tiers (débité)', 'Déposant Fictif');
    expect(within(dialog).getByText('On lui doit 1 000 000 MRO')).toBeInTheDocument();
    await choose(user, dialog, 'Vers le tiers (crédité)', 'Receveur Fictif');
    await user.type(within(dialog).getByLabelText('Montant'), '400000');

    expect(within(dialog).getByText('Après : Déposant Fictif — On lui doit 600 000 MRO')).toBeInTheDocument();
    expect(within(dialog).getByText('Après : Receveur Fictif — On lui doit 400 000 MRO')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer le transfert de solde' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(calls).toHaveLength(1);
    expect(calls[0]!.body).toEqual({
      agencyId: nkc.id,
      fromPartyId: 'p-a',
      toPartyId: 'p-b',
      amount: 400_000,
      note: null,
      operationAt: null,
    });
    expect(calls[0]!.key).toBeTruthy();
  });

  it('prévient du plafond dépassé, et explique le refus du serveur', async () => {
    const { calls } = transferServer(() =>
      HttpResponse.json({ code: 'Cash.PartyTransfer.LimitExceeded' }, { status: 409 }),
    );
    const { user } = renderApp('/cash');
    await user.click(await screen.findByRole('button', { name: 'Transfert de solde' }));

    const dialog = await screen.findByRole('dialog');
    await choose(user, dialog, 'Du tiers (débité)', 'Déposant Fictif');
    await choose(user, dialog, 'Vers le tiers (crédité)', 'Receveur Fictif');
    await user.type(within(dialog).getByLabelText('Montant'), '1500000');
    expect(
      within(dialog).getByText('Déposant Fictif dépasserait son plafond de 0 MRO : le transfert de solde sera refusé.'),
    ).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer le transfert de solde' }));

    expect(
      await within(dialog).findByText('Le plafond de crédit du tiers débité serait dépassé : transfert de solde refusé.'),
    ).toBeInTheDocument();
    expect(calls).toHaveLength(1);
  });

  it('refuse deux fois le même tiers sans rien envoyer', async () => {
    const { calls } = transferServer();
    const { user } = renderApp('/cash');
    await user.click(await screen.findByRole('button', { name: 'Transfert de solde' }));

    const dialog = await screen.findByRole('dialog');
    await choose(user, dialog, 'Du tiers (débité)', 'Déposant Fictif');
    await choose(user, dialog, 'Vers le tiers (crédité)', 'Déposant Fictif');
    await user.type(within(dialog).getByLabelText('Montant'), '1000');
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer le transfert de solde' }));

    expect(within(dialog).getByText('Choisissez deux tiers différents.')).toBeInTheDocument();
    expect(calls).toEqual([]);
  });

  it('absent sans la permission (caissier)', async () => {
    signedInAs(cashierAccount);
    transferServer();
    renderApp('/cash');
    expect(await screen.findByRole('link', { name: 'Nouveau mouvement' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Transfert de solde' })).not.toBeInTheDocument();
  });
});
