import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { agentAccount, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { TreasuryAccount, TreasuryAccountStatement } from '../types';

// Fiche d'un compte de trésorerie : relevé lu au grand livre et actions (renommer, désactiver, réactiver).
// Données fictives uniquement.

function account(overrides: Partial<TreasuryAccount>): TreasuryAccount {
  return { id: 'acc', name: 'Compte', type: 'Cash', agencyId: nkc.id, currency: 'MRO', isActive: true, balance: 0, ...overrides };
}

const accounts: TreasuryAccount[] = [
  account({ id: 'nkc-cash', name: 'Caisse principale', balance: 70_000 }),
  account({ id: 'nkc-empty', name: 'Banque vide', type: 'Bank', balance: 0 }),
  account({ id: 'nkc-old', name: 'Ancien wallet', type: 'Wallet', isActive: false, balance: 0 }),
];

function statementOf(a: TreasuryAccount): TreasuryAccountStatement {
  return {
    accountId: a.id,
    accountName: a.name,
    type: a.type,
    agencyId: a.agencyId,
    currency: a.currency,
    isActive: a.isActive,
    from: '2026-10-01',
    to: '2026-10-24',
    openingBalance: 0,
    lines: [
      {
        operationDate: '2026-10-04',
        recordedAt: '2026-10-04T10:00:00Z',
        entryType: 'Transfers.Created',
        label: 'Transfert TR-NKC-20261004-000012',
        reason: null,
        isReversal: false,
        debit: 100_000,
        credit: 0,
        balanceAfter: 100_000,
      },
      {
        operationDate: '2026-10-12',
        recordedAt: '2026-10-12T10:00:00Z',
        entryType: 'Tests.Unknown',
        label: 'Écriture de test',
        reason: 'Motif fictif',
        isReversal: true,
        debit: 0,
        credit: 30_000,
        balanceAfter: 70_000,
      },
    ],
    closingBalance: 70_000,
  };
}

/** Le serveur d'un compte : fiche, relevé, et les actions qui modifient la fiche. */
function accountServer(options: { hideBalances?: boolean } = {}) {
  const store = accounts.map((a) => ({ ...a }));
  const statementQueries: URLSearchParams[] = [];
  const calls: { path: string; body: unknown }[] = [];
  const find = (id: unknown) => store.find((a) => a.id === id);
  server.use(
    http.get(apiUrl('/treasury-accounts'), () => HttpResponse.json(store)),
    http.get(apiUrl('/treasury-accounts/:id'), ({ params }) => {
      const found = find(params.id);
      if (!found) return HttpResponse.json({ code: 'Treasury.Account.NotFound' }, { status: 404 });
      return HttpResponse.json(options.hideBalances ? { ...found, balance: null } : found);
    }),
    http.get(apiUrl('/treasury-accounts/:id/statement'), ({ params, request }) => {
      statementQueries.push(new URL(request.url).searchParams);
      return HttpResponse.json(statementOf(find(params.id)!));
    }),
    http.put(apiUrl('/treasury-accounts/:id'), async ({ params, request }) => {
      const body = (await request.json()) as { name: string };
      calls.push({ path: `rename:${params.id as string}`, body });
      find(params.id)!.name = body.name;
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(apiUrl('/treasury-accounts/:id/:action'), ({ params }) => {
      calls.push({ path: `${params.action as string}:${params.id as string}`, body: null });
      find(params.id)!.isActive = params.action === 'reactivate';
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return { statementQueries, calls };
}

describe('Fiche d’un compte de trésorerie — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('s’ouvre depuis la liste et montre le relevé, chaque ligne titrée par son type', async () => {
    const { statementQueries } = accountServer();
    const { user } = renderApp('/treasury');
    await user.click(await screen.findByRole('link', { name: /Caisse principale/ }));

    expect(await screen.findByRole('heading', { name: 'Caisse principale' })).toBeInTheDocument();
    const statement = await screen.findByRole('region', { name: 'Relevé' });
    expect(within(statement).getByText('Paiement d’un transfert')).toBeInTheDocument();
    expect(within(statement).getByText('Transfert TR-NKC-20261004-000012')).toBeInTheDocument();
    expect(within(statement).getByText('+ 100 000 MRO')).toBeInTheDocument();
    // Type inconnu : le libellé du serveur sert de titre ; contre-passation et motif écrits.
    expect(within(statement).getByText('Écriture de test')).toBeInTheDocument();
    expect(within(statement).getByText('Contre-passation')).toBeInTheDocument();
    expect(within(statement).getByText('Motif fictif')).toBeInTheDocument();
    expect(within(statement).getByText('− 30 000 MRO')).toBeInTheDocument();
    expect(within(statement).getByText('Solde de fin · 24/10').parentElement).toHaveTextContent('70 000 MRO');
    expect(statementQueries[0]?.has('from')).toBe(false);

    // Un champ date ne se tape pas caractère par caractère dans jsdom : on pose la valeur choisie.
    fireEvent.change(screen.getByLabelText('Du'), { target: { value: '2026-09-01' } });
    await waitFor(() => expect(statementQueries.at(-1)?.get('from')).toBe('2026-09-01'));
  });

  it('renomme le compte depuis sa fiche', async () => {
    const { calls } = accountServer();
    const { user } = renderApp('/treasury/nkc-cash');
    await user.click(await screen.findByRole('button', { name: 'Renommer, désactiver ou réactiver' }));

    const dialog = await screen.findByRole('dialog');
    const field = within(dialog).getByLabelText('Nom du compte');
    await user.clear(field);
    await user.type(field, 'Caisse du guichet');
    await user.click(within(dialog).getByRole('button', { name: 'Renommer' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(calls).toEqual([{ path: 'rename:nkc-cash', body: { name: 'Caisse du guichet' } }]);
    expect(await screen.findByRole('heading', { name: 'Caisse du guichet' })).toBeInTheDocument();
  });

  it('n’offre pas de désactiver un compte dont le solde n’est pas nul', async () => {
    const { calls } = accountServer();
    const { user } = renderApp('/treasury/nkc-cash');
    await user.click(await screen.findByRole('button', { name: 'Renommer, désactiver ou réactiver' }));

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).queryByRole('button', { name: 'Désactiver ce compte' })).not.toBeInTheDocument();
    expect(within(dialog).getByText(/son solde doit être à zéro/)).toBeInTheDocument();
    expect(calls).toEqual([]);
  });

  it('désactive un compte à solde nul après confirmation', async () => {
    const { calls } = accountServer();
    const { user } = renderApp('/treasury/nkc-empty');
    await user.click(await screen.findByRole('button', { name: 'Renommer, désactiver ou réactiver' }));

    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Désactiver ce compte' }));
    expect(within(dialog).getByText('Désactiver « Banque vide » ?')).toBeInTheDocument();
    expect(calls).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Désactiver ce compte' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(calls).toEqual([{ path: 'deactivate:nkc-empty', body: null }]);
    expect(await screen.findByText('Désactivé')).toBeInTheDocument();
  });

  it('réactive un compte désactivé', async () => {
    const { calls } = accountServer();
    const { user } = renderApp('/treasury/nkc-old');
    await user.click(await screen.findByRole('button', { name: 'Renommer, désactiver ou réactiver' }));

    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Réactiver ce compte' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(calls).toEqual([{ path: 'reactivate:nkc-old', body: null }]);
    expect(await screen.findByText('Actif')).toBeInTheDocument();
  });
});

describe('Fiche d’un compte de trésorerie — autres comptes', () => {
  it('un agent lit le relevé, sans action de gestion', async () => {
    signedInAs(agentAccount);
    accountServer();
    renderApp('/treasury/nkc-cash');

    expect(await screen.findByRole('region', { name: 'Relevé' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Renommer, désactiver ou réactiver' })).not.toBeInTheDocument();
  });

  it('sans cash.budget.read : ni solde ni relevé, et aucune requête de relevé', async () => {
    signedInAs({ ...agentAccount, permissions: agentAccount.permissions.filter((p) => p !== 'cash.budget.read') });
    const { statementQueries } = accountServer({ hideBalances: true });
    renderApp('/treasury/nkc-cash');

    expect(await screen.findByText(/ne voit pas les soldes ni les mouvements/)).toBeInTheDocument();
    expect(screen.queryByText('Solde')).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Relevé' })).not.toBeInTheDocument();
    expect(statementQueries).toEqual([]);
  });

  it('dit qu’un compte hors de portée est introuvable', async () => {
    signedInAs(agentAccount);
    accountServer();
    renderApp('/treasury/dkr-absent');
    expect(await screen.findByText('Compte de trésorerie introuvable.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Retour aux comptes de trésorerie' })).toBeInTheDocument();
  });
});
