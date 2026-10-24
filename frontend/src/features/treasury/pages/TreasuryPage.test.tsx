import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { agentAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { TreasuryAccount } from '../types';

// Données fictives uniquement.
function account(overrides: Partial<TreasuryAccount>): TreasuryAccount {
  return { id: 'acc', name: 'Compte', type: 'Cash', agencyId: nkc.id, currency: 'MRO', isActive: true, balance: 0, ...overrides };
}

const initial: TreasuryAccount[] = [
  account({ id: 'nkc-cash', name: 'Caisse principale', balance: 1_850_000 }),
  account({ id: 'nkc-wallet', name: 'Wallet fictif', type: 'Wallet', balance: 350_000 }),
  account({ id: 'nkc-old', name: 'Ancien wallet', type: 'Wallet', isActive: false, balance: 0 }),
  account({ id: 'nkc-empty', name: 'Banque vide', type: 'Bank', balance: 0 }),
  account({ id: 'dkr-cash', name: 'Caisse Dakar', agencyId: dkr.id, currency: 'XOF', balance: 4_400_000 }),
];

/** Le serveur de la trésorerie : liste filtrée comme le backend, et les actions qui modifient la liste. */
function treasuryServer(accounts: TreasuryAccount[], options: { hideBalances?: boolean } = {}) {
  const store = accounts.map((a) => ({ ...a }));
  const queries: URLSearchParams[] = [];
  const posts: { path: string; body: unknown }[] = [];
  server.use(
    http.get(apiUrl('/treasury-accounts'), ({ request }) => {
      const query = new URL(request.url).searchParams;
      queries.push(query);
      const agencyId = query.get('agencyId');
      const list = store
        .filter((a) => (!agencyId || a.agencyId === agencyId) && (query.get('includeInactive') === 'true' || a.isActive))
        .map((a) => (options.hideBalances ? { ...a, balance: null } : a));
      return HttpResponse.json(list);
    }),
    http.post(apiUrl('/treasury-accounts'), async ({ request }) => {
      const body = (await request.json()) as { name: string; type: TreasuryAccount['type']; agencyId: string };
      posts.push({ path: 'create', body });
      if (store.some((a) => a.agencyId === body.agencyId && a.name.toLowerCase() === body.name.toLowerCase())) {
        return HttpResponse.json({ code: 'Treasury.Account.NameTaken' }, { status: 409 });
      }
      const currency = body.agencyId === dkr.id ? 'XOF' : 'MRO';
      store.push(account({ id: `new-${store.length}`, ...body, currency }));
      return HttpResponse.json({ id: `new-${store.length}` }, { status: 201 });
    }),
    http.put(apiUrl('/treasury-accounts/:id'), async ({ params, request }) => {
      const body = (await request.json()) as { name: string };
      posts.push({ path: `rename:${params.id as string}`, body });
      store.find((a) => a.id === params.id)!.name = body.name;
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(apiUrl('/treasury-accounts/:id/:action'), ({ params }) => {
      posts.push({ path: `${params.action as string}:${params.id as string}`, body: null });
      store.find((a) => a.id === params.id)!.isActive = params.action === 'reactivate';
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return { queries, posts };
}

const card = (name: string) => screen.getByText(name).closest('a') as HTMLElement;

describe('Comptes de trésorerie — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('montre les deux agences, chacune avec son total des comptes actifs dans sa devise', async () => {
    const { queries } = treasuryServer(initial);
    renderApp('/treasury');

    const nouakchott = await screen.findByRole('region', { name: 'Nouakchott' });
    const dakar = screen.getByRole('region', { name: 'Dakar' });
    // Le compte désactivé n'entre pas dans le total : 1 850 000 + 350 000 + 0.
    expect(within(nouakchott).getByText('2 200 000 MRO')).toBeInTheDocument();
    expect(within(dakar).getAllByText('4 400 000 FCFA')).toHaveLength(2);
    expect(within(nouakchott).getByText('Caisses')).toBeInTheDocument();
    expect(within(nouakchott).getByText('Banques et wallets')).toBeInTheDocument();
    // Banques et wallets de Nouakchott en MRO, jamais en MRU.
    expect(within(nouakchott).getByText('350 000 MRO')).toBeInTheDocument();
    expect(within(card('Ancien wallet')).getByText('Désactivé')).toBeInTheDocument();
    expect(queries[0]?.get('includeInactive')).toBe('true');
    expect(queries[0]?.has('agencyId')).toBe(false);
  });

  it('crée un compte : nom, type, agence ; la liste est relue', async () => {
    const { posts } = treasuryServer(initial);
    const { user } = renderApp('/treasury');
    await user.click(await screen.findByRole('button', { name: 'Nouveau compte' }));

    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText('Nom (50 caractères au plus)'), '  Wave fictif ');
    await user.selectOptions(within(dialog).getByLabelText('Type'), 'Wallet');
    await user.selectOptions(within(dialog).getByLabelText('Agence'), dkr.id);
    await user.click(within(dialog).getByRole('button', { name: 'Créer le compte' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(posts).toEqual([{ path: 'create', body: { name: 'Wave fictif', type: 'Wallet', agencyId: dkr.id } }]);
    expect(await within(screen.getByRole('region', { name: 'Dakar' })).findByText('Wave fictif')).toBeInTheDocument();
  });

  it('exige un nom avant tout envoi et traduit un nom déjà pris', async () => {
    const { posts } = treasuryServer(initial);
    const { user } = renderApp('/treasury');
    await user.click(await screen.findByRole('button', { name: 'Nouveau compte' }));
    const dialog = await screen.findByRole('dialog');

    await user.click(within(dialog).getByRole('button', { name: 'Créer le compte' }));
    expect(within(dialog).getByText('Le nom est obligatoire.')).toBeInTheDocument();
    expect(posts).toEqual([]);

    await user.type(within(dialog).getByLabelText('Nom (50 caractères au plus)'), 'caisse principale');
    await user.click(within(dialog).getByRole('button', { name: 'Créer le compte' }));
    expect(await within(dialog).findByText('Un compte de cette agence porte déjà ce nom.')).toBeInTheDocument();
  });

});

describe('Comptes de trésorerie — Agent', () => {
  it('voit les comptes de son agence et leurs soldes, sans action de gestion', async () => {
    signedInAs(agentAccount);
    const { queries } = treasuryServer(initial);
    renderApp('/treasury');

    expect(await screen.findByText('Caisse principale')).toBeInTheDocument();
    expect(screen.getByText('2 200 000 MRO')).toBeInTheDocument();
    expect(screen.queryByText('Caisse Dakar')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nouveau compte' })).not.toBeInTheDocument();
    // Chaque carte ouvre la fiche du compte (relevé) ; aucune action de gestion.
    expect(screen.getByRole('link', { name: /Caisse principale/ })).toHaveAttribute('href', '/treasury/nkc-cash');
    expect(queries[0]?.get('agencyId')).toBe(nkc.id);
  });

  it('sans cash.budget.read : ni solde ni total', async () => {
    signedInAs({ ...agentAccount, permissions: agentAccount.permissions.filter((p) => p !== 'cash.budget.read') });
    treasuryServer(initial, { hideBalances: true });
    renderApp('/treasury');

    expect(await screen.findByText('Caisse principale')).toBeInTheDocument();
    expect(screen.queryByText('Solde')).not.toBeInTheDocument();
    expect(screen.queryByText('Total des comptes actifs')).not.toBeInTheDocument();
  });
});
