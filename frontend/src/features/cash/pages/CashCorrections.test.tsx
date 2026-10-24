import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { PartySummary } from '@/features/parties';
import type { TreasuryAccount } from '@/features/treasury/types';
import { agentAccount, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { BalanceAdjustment, CashMovement, CashMovementList } from '../types';

// Caisse, livraison 2 : corrections du Superviseur (cash.manage) : annuler un mouvement, ajustement de solde.
// Données fictives uniquement.

const accounts: TreasuryAccount[] = [{ id: 'cash', name: 'Espèces', type: 'Cash', agencyId: nkc.id, currency: 'MRO', isActive: true, balance: 100_000 }];

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

const active: CashMovement = {
  id: 'm1',
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
  operationAt: '2026-10-24T11:30:00Z',
  createdAt: '2026-10-24T11:30:00Z',
  createdBy: 'u',
  status: 'Active',
  cancellationReason: null,
};

const adjustment: BalanceAdjustment = {
  id: 'a1',
  agencyId: nkc.id,
  target: 'TreasuryAccount',
  targetId: 'cash',
  targetName: 'Espèces',
  amount: -2_500,
  currency: 'MRO',
  reason: 'Écart de comptage',
  operationAt: '2026-10-24T09:00:00Z',
  createdBy: 'u',
  createdAt: '2026-10-24T09:00:00Z',
};

function listOf(items: CashMovement[]): CashMovementList {
  return {
    items,
    page: 1,
    pageSize: 50,
    totalCount: items.length,
    allCount: items.length,
    incoming: { amount: 300_000, count: 1 },
    outgoing: { amount: 0, count: 0 },
    net: 300_000,
    internalTransfers: { amount: 0, count: 0 },
  };
}

function correctionsServer() {
  let movement = { ...active };
  const calls: { path: string; body: unknown }[] = [];
  server.use(
    http.get(apiUrl('/cash-movements'), () => HttpResponse.json(listOf([movement]))),
    http.get(apiUrl('/treasury-accounts'), () => HttpResponse.json(accounts)),
    http.get(apiUrl('/parties'), () => HttpResponse.json({ items: [client], count: 1, totalCount: 1, totals: null })),
    http.get(apiUrl('/balance-adjustments'), () => HttpResponse.json([adjustment])),
    http.post(apiUrl('/cash-movements/:id/cancel'), async ({ params, request }) => {
      const body = (await request.json()) as { reason: string };
      calls.push({ path: `cancel:${params.id as string}`, body });
      movement = { ...movement, status: 'Cancelled', cancellationReason: body.reason };
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(apiUrl('/balance-adjustments'), async ({ request }) => {
      calls.push({ path: 'adjust', body: await request.json() });
      return HttpResponse.json({ id: 'a2' }, { status: 201 });
    }),
  );
  return { calls };
}

describe('Caisse — corrections du Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('annule un mouvement avec un motif obligatoire ; il reste barré avec son motif', async () => {
    const { calls } = correctionsServer();
    const { user } = renderApp(`/cash?agencyId=${nkc.id}`);
    await user.click(await screen.findByRole('button', { name: 'Annuler : Encaissement · Boutique Fictive' }));

    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Annuler le mouvement' }));
    expect(within(dialog).getByText('Le motif est obligatoire.')).toBeInTheDocument();
    expect(calls).toEqual([]);

    await user.type(within(dialog).getByLabelText('Motif (obligatoire)'), 'Montant saisi en double');
    await user.click(within(dialog).getByRole('button', { name: 'Annuler le mouvement' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(calls).toEqual([{ path: 'cancel:m1', body: { reason: 'Montant saisi en double' } }]);
    expect(await screen.findByText('Annulé')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Annuler :/ })).not.toBeInTheDocument();
  });

  it('montre les ajustements de la période, à part des mouvements', async () => {
    correctionsServer();
    renderApp(`/cash?agencyId=${nkc.id}`);
    const section = await screen.findByRole('region', { name: 'Ajustements de solde de la période' });
    expect(within(section).getByText('Espèces')).toBeInTheDocument();
    expect(within(section).getByText('− 2 500 MRO')).toBeInTheDocument();
    expect(within(section).getByText('Motif : Écart de comptage')).toBeInTheDocument();
  });

  it('ajuste le solde d’un compte : sens en mots, nouveau solde affiché, différence signée envoyée', async () => {
    const { calls } = correctionsServer();
    const { user } = renderApp(`/cash?agencyId=${nkc.id}`);
    await user.click(await screen.findByRole('button', { name: 'Ajustement de solde' }));

    const dialog = await screen.findByRole('dialog');
    await user.selectOptions(within(dialog).getByLabelText('Compte à corriger'), 'cash');
    await user.click(within(dialog).getByRole('radio', { name: 'Baisser le solde (moins d’argent sur le compte)' }));
    await user.type(within(dialog).getByLabelText('Montant de la correction'), '2500');
    expect(within(dialog).getByText('Nouveau solde de « Espèces » : 97 500 MRO.')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer l’ajustement' }));
    expect(within(dialog).getByText('Le motif est obligatoire.')).toBeInTheDocument();

    await user.type(within(dialog).getByLabelText('Motif (obligatoire)'), 'Écart de comptage');
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer l’ajustement' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(calls).toEqual([
      {
        path: 'adjust',
        body: { agencyId: nkc.id, target: 'TreasuryAccount', targetId: 'cash', amount: -2_500, reason: 'Écart de comptage', operationAt: null },
      },
    ]);
  });

  it('ajuste le solde d’un tiers : choisi dans la liste des tiers', async () => {
    const { calls } = correctionsServer();
    const { user } = renderApp(`/cash?agencyId=${nkc.id}`);
    await user.click(await screen.findByRole('button', { name: 'Ajustement de solde' }));

    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('radio', { name: 'Tiers' }));
    await user.type(within(dialog).getByRole('searchbox'), 'Bout');
    await user.click(await within(dialog).findByRole('button', { name: /Boutique Fictive/ }));
    await user.click(within(dialog).getByRole('radio', { name: 'Augmenter ce qu’il nous doit' }));
    await user.type(within(dialog).getByLabelText('Montant de la correction'), '10000');
    await user.type(within(dialog).getByLabelText('Motif (obligatoire)'), 'Oubli de saisie');
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer l’ajustement' }));

    await waitFor(() => expect(calls).toHaveLength(1));
    expect(calls[0]?.body).toMatchObject({ target: 'Party', targetId: 'p-1', amount: 10_000 });
  });
});

describe('Caisse — sans cash.manage', () => {
  it('ni « Annuler » ni « Ajustement de solde »', async () => {
    signedInAs(agentAccount);
    correctionsServer();
    renderApp('/cash');
    expect(await screen.findByText('Boutique Fictive')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Annuler :/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Ajustement de solde' })).not.toBeInTheDocument();
  });
});
