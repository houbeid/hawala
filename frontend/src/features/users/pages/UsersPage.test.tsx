import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { agentAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { UserSummary } from '../types';

// Comptes, livraison 1 : liste, création (mot de passe temporaire une seule fois), fiche et actions.
// Données fictives uniquement (emails en @example.com).

const nkcAgency = { id: nkc.id, code: 'NKC', name: 'Nouakchott', color: '#1E8E3E' };

function user(overrides: Partial<UserSummary>): UserSummary {
  return {
    id: 'u',
    fullName: 'Compte Fictif',
    email: 'compte@example.com',
    phone: null,
    role: 'Agent',
    agency: nkcAgency,
    status: 'Active',
    mustChangePassword: false,
    lockedUntil: null,
    lastLoginAt: '2026-10-24T08:15:00Z',
    ...overrides,
  };
}

const agent = user({ id: 'u-agent', fullName: 'Agent Fictif', email: 'agent.fictif@example.com', phone: '+22220000001', mustChangePassword: true });
const blocked = user({ id: 'u-blocked', fullName: 'Consultation Fictive', role: 'ReadOnly', status: 'Blocked', agency: { ...nkcAgency, id: dkr.id, code: 'DKR' } });
const locked = user({ id: 'u-locked', fullName: 'Caissier Fictif', role: 'Cashier', lockedUntil: '2999-01-01T10:00:00Z' });
const self = user({ id: supervisorAccount.id, fullName: 'Superviseur Test', role: 'Supervisor', agency: null });

function usersServer(options: { failWith?: string } = {}) {
  const users = [agent, blocked, locked, self].map((u) => ({ ...u }));
  const queries: URLSearchParams[] = [];
  const calls: { path: string; body: unknown }[] = [];
  const record = async (path: string, request: Request) => {
    const text = await request.text();
    calls.push({ path, body: text ? JSON.parse(text) : null });
  };
  const fail = () => HttpResponse.json({ code: options.failWith }, { status: 409 });
  server.use(
    http.get(apiUrl('/users'), ({ request }) => {
      queries.push(new URL(request.url).searchParams);
      return HttpResponse.json(users);
    }),
    http.get(apiUrl('/users/:id'), ({ params }) => {
      const found = users.find((u) => u.id === params.id);
      return found ? HttpResponse.json(found) : HttpResponse.json({ code: 'Identity.Account.NotFound' }, { status: 404 });
    }),
    http.get(apiUrl('/users/:id/permissions'), ({ params }) => HttpResponse.json({ userId: params.id, role: 'Agent', permissions: [] })),
    http.post(apiUrl('/users'), async ({ request }) => {
      await record('create', request);
      return HttpResponse.json({ id: 'u-new', temporaryPassword: 'abc-def-gh2' }, { status: 201 });
    }),
    http.put(apiUrl('/users/:id'), async ({ params, request }) => {
      await record(`profile:${params.id as string}`, request);
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(apiUrl('/users/:id/:action'), async ({ params, request }) => {
      await record(`${params.action as string}:${params.id as string}`, request);
      if (options.failWith) return fail();
      const target = users.find((u) => u.id === params.id)!;
      if (params.action === 'block') target.status = 'Blocked';
      if (params.action === 'unblock' || params.action === 'reset-password') return HttpResponse.json({ temporaryPassword: 'new-tmp-pw3' });
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return { queries, calls };
}

describe('Comptes — liste', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('écrit l’état de chaque compte en mots, avec son rôle, son email et son agence', async () => {
    usersServer();
    renderApp('/users');

    const agentRow = (await screen.findByText('Agent Fictif')).closest('a') as HTMLElement;
    expect(within(agentRow).getByText('Agent')).toBeInTheDocument();
    expect(within(agentRow).getByText(/agent\.fictif@example\.com · \+22220000001/)).toBeInTheDocument();
    expect(within(agentRow).getByText('Mot de passe temporaire')).toBeInTheDocument();
    expect(within(agentRow).getByText('NKC')).toBeInTheDocument();
    expect(within(screen.getByText('Consultation Fictive').closest('a') as HTMLElement).getByText('Bloqué')).toBeInTheDocument();
    expect(within(screen.getByText('Caissier Fictif').closest('a') as HTMLElement).getByText(/Verrouillé jusqu’à/)).toBeInTheDocument();
  });

  it('envoie les filtres au serveur', async () => {
    const { queries } = usersServer();
    const { user: u } = renderApp('/users');
    await screen.findByText('Agent Fictif');

    await u.selectOptions(screen.getByLabelText('Rôle'), 'Cashier');
    await u.selectOptions(screen.getByLabelText('Statut'), 'Blocked');
    await u.selectOptions(screen.getByLabelText('Agence'), dkr.id);
    await u.type(screen.getByRole('searchbox', { name: 'Rechercher un compte' }), 'fictif');

    await waitFor(() => expect(queries.at(-1)?.get('search')).toBe('fictif'));
    expect(queries.at(-1)?.get('role')).toBe('Cashier');
    expect(queries.at(-1)?.get('status')).toBe('Blocked');
    expect(queries.at(-1)?.get('agencyId')).toBe(dkr.id);
  });

  it('crée un compte et affiche son mot de passe temporaire une seule fois', async () => {
    const { calls } = usersServer();
    const { user: u } = renderApp('/users');
    await u.click(await screen.findByRole('button', { name: 'Nouveau compte' }));

    const dialog = await screen.findByRole('dialog', { name: 'Nouveau compte' });
    await u.type(within(dialog).getByLabelText('Nom complet'), 'Caissier Neuf');
    await u.type(within(dialog).getByLabelText('Email (sert à se connecter)'), 'caissier.neuf@example.com');
    await u.selectOptions(within(dialog).getByLabelText('Rôle'), 'Cashier');
    await u.selectOptions(within(dialog).getByLabelText('Agence'), dkr.id);
    await u.click(within(dialog).getByRole('button', { name: 'Créer le compte' }));

    const done = await screen.findByRole('dialog', { name: 'Compte créé' });
    expect(within(done).getByTestId('temporary-password')).toHaveTextContent('abc-def-gh2');
    expect(within(done).getByText(/Il ne sera plus jamais affiché/)).toBeInTheDocument();
    expect(calls).toEqual([
      { path: 'create', body: { fullName: 'Caissier Neuf', email: 'caissier.neuf@example.com', phone: null, role: 'Cashier', agencyId: dkr.id } },
    ]);
    await u.click(within(done).getByRole('button', { name: 'J’ai transmis le mot de passe' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('un Superviseur se crée sans agence', async () => {
    const { calls } = usersServer();
    const { user: u } = renderApp('/users');
    await u.click(await screen.findByRole('button', { name: 'Nouveau compte' }));
    const dialog = await screen.findByRole('dialog');
    await u.type(within(dialog).getByLabelText('Nom complet'), 'Superviseur Neuf');
    await u.type(within(dialog).getByLabelText('Email (sert à se connecter)'), 'sup.neuf@example.com');
    await u.selectOptions(within(dialog).getByLabelText('Rôle'), 'Supervisor');
    expect(within(dialog).queryByLabelText('Agence')).not.toBeInTheDocument();
    await u.click(within(dialog).getByRole('button', { name: 'Créer le compte' }));
    await waitFor(() => expect(calls).toHaveLength(1));
    expect(calls[0]?.body).toMatchObject({ role: 'Supervisor', agencyId: null });
  });
});

describe('Comptes — gestionnaire non Superviseur', () => {
  it('ne propose ni le rôle Superviseur ni une autre agence', async () => {
    signedInAs({ ...agentAccount, permissions: [...agentAccount.permissions, 'users.manage'] });
    usersServer();
    const { user: u } = renderApp('/users');
    expect(screen.queryByLabelText('Agence')).not.toBeInTheDocument();
    await u.click(await screen.findByRole('button', { name: 'Nouveau compte' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).queryByRole('option', { name: 'Superviseur' })).not.toBeInTheDocument();
    expect(within(dialog).getByLabelText('Agence')).toBeDisabled();
    expect(within(dialog).getByLabelText('Agence')).toHaveValue(nkc.id);
  });
});

describe('Comptes — fiche et actions', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('réinitialise le mot de passe : le nouveau s’affiche une fois sur la fiche', async () => {
    const { calls } = usersServer();
    const { user: u } = renderApp('/users/u-agent');
    await u.click(await screen.findByRole('button', { name: 'Nouveau mot de passe temporaire' }));
    await u.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Réinitialiser' }));

    expect(await screen.findByTestId('temporary-password')).toHaveTextContent('new-tmp-pw3');
    expect(calls.map((c) => c.path)).toEqual(['reset-password:u-agent']);
    await u.click(screen.getByRole('button', { name: 'J’ai transmis le mot de passe' }));
    expect(screen.queryByTestId('temporary-password')).not.toBeInTheDocument();
  });

  it('bloque après confirmation ; le compte devient « Débloquer »', async () => {
    const { calls } = usersServer();
    const { user: u } = renderApp('/users/u-agent');
    await u.click(await screen.findByRole('button', { name: 'Bloquer le compte' }));
    const dialog = await screen.findByRole('dialog', { name: 'Bloquer « Agent Fictif » ?' });
    await u.click(within(dialog).getByRole('button', { name: 'Bloquer le compte' }));
    await waitFor(() => expect(calls.map((c) => c.path)).toEqual(['block:u-agent']));
    expect(await screen.findByRole('button', { name: 'Débloquer le compte' })).toBeInTheDocument();
  });

  it('montre le refus du serveur dans la fenêtre', async () => {
    usersServer({ failWith: 'Identity.Account.LastActiveSupervisor' });
    const { user: u } = renderApp('/users/u-agent');
    await u.click(await screen.findByRole('button', { name: 'Bloquer le compte' }));
    const dialog = await screen.findByRole('dialog');
    await u.click(within(dialog).getByRole('button', { name: 'Bloquer le compte' }));
    expect(await within(dialog).findByText(/c’est le dernier Superviseur actif/)).toBeInTheDocument();
  });

  it('modifie le profil et change le rôle avec l’agence', async () => {
    const { calls } = usersServer();
    const { user: u } = renderApp('/users/u-agent');

    await u.click(await screen.findByRole('button', { name: 'Modifier le nom, l’email, le téléphone' }));
    let dialog = await screen.findByRole('dialog');
    await u.clear(within(dialog).getByLabelText(/^Téléphone/));
    await u.click(within(dialog).getByRole('button', { name: 'Enregistrer le profil' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    await u.click(screen.getByRole('button', { name: 'Changer le rôle ou l’agence' }));
    dialog = await screen.findByRole('dialog');
    await u.selectOptions(within(dialog).getByLabelText('Rôle'), 'Cashier');
    await u.selectOptions(within(dialog).getByLabelText('Agence'), dkr.id);
    await u.click(within(dialog).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() => expect(calls).toHaveLength(2));

    expect(calls).toEqual([
      { path: 'profile:u-agent', body: { fullName: 'Agent Fictif', email: 'agent.fictif@example.com', phone: null } },
      { path: 'change-role:u-agent', body: { role: 'Cashier', agencyId: dkr.id } },
    ]);
  });

  it('sur son propre compte : ni bloquer, ni réinitialiser, ni changer de rôle', async () => {
    usersServer();
    renderApp(`/users/${supervisorAccount.id}`);
    expect(await screen.findByRole('button', { name: 'Modifier le nom, l’email, le téléphone' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Bloquer le compte' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nouveau mot de passe temporaire' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Changer le rôle ou l’agence' })).not.toBeInTheDocument();
  });

  it('dit qu’un compte hors de portée est introuvable', async () => {
    usersServer();
    renderApp('/users/u-absent');
    expect(await screen.findByText('Compte introuvable.')).toBeInTheDocument();
  });
});
