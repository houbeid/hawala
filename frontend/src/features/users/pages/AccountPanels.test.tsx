import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { PartySummary } from '@/features/parties';
import { agentAccount, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { AccountPartyScope, SessionView, UserLimits, UserPermissions, UserSummary } from '../types';

// Comptes, livraison 2 : onglets Permissions, Tiers autorisés, Plafonds, Sessions. Données fictives uniquement.

const target: UserSummary = {
  id: 'u-agent',
  fullName: 'Agent Fictif',
  email: 'agent.fictif@example.com',
  phone: null,
  role: 'Agent',
  agency: { id: nkc.id, code: 'NKC', name: 'Nouakchott', color: '#1E8E3E' },
  status: 'Active',
  mustChangePassword: false,
  lockedUntil: null,
  lastLoginAt: null,
};

const permissions: UserPermissions = {
  userId: 'u-agent',
  role: 'Agent',
  permissions: [
    { permission: 'transfers.deliver', granted: true, source: 'Role' },
    { permission: 'transfers.cancel', granted: false, source: 'None' },
    { permission: 'transfers.credit', granted: true, source: 'Added' },
    { permission: 'cash.budget.save', granted: false, source: 'Removed' },
  ],
};

const scope: AccountPartyScope = {
  accountId: 'u-agent',
  limited: true,
  parties: [
    { id: 'p1', name: 'Boutique Fictive', isArchived: false },
    { id: 'p2', name: 'Fournisseur Fictif', isArchived: false },
  ],
};

const limits: UserLimits = { userId: 'u-agent', currency: 'MRO', perTransfer: 5_000_000, perDay: 20_000_000, creditPerDay: null };

const sessions: SessionView[] = [
  { id: 's1', startedAt: '2026-10-24T08:00:00Z', lastUsedAt: '2026-10-24T11:00:00Z', expiresAt: '2026-10-24T20:00:00Z', device: 'Téléphone fictif', ipAddress: '192.0.2.10', current: false },
];

const newParty: PartySummary = {
  id: 'p3',
  name: 'Client Fictif',
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

function panelsServer(user: UserSummary = target) {
  const calls: { path: string; body: unknown }[] = [];
  const record = async (path: string, request: Request) => {
    const text = await request.text();
    calls.push({ path, body: text ? JSON.parse(text) : null });
  };
  server.use(
    http.get(apiUrl('/users/:id'), () => HttpResponse.json(user)),
    http.get(apiUrl('/users/:id/permissions'), () => HttpResponse.json({ ...permissions, role: user.role })),
    http.put(apiUrl('/users/:id/permissions'), async ({ request }) => {
      await record('permissions', request);
      return HttpResponse.json(permissions);
    }),
    http.get(apiUrl('/users/:id/party-scope'), () => HttpResponse.json(scope)),
    http.put(apiUrl('/users/:id/party-scope'), async ({ request }) => {
      await record('party-scope', request);
      return HttpResponse.json(scope);
    }),
    http.get(apiUrl('/users/:id/limits'), () => HttpResponse.json(limits)),
    http.put(apiUrl('/users/:id/limits'), async ({ request }) => {
      await record('limits', request);
      return HttpResponse.json(limits);
    }),
    http.get(apiUrl('/users/:id/sessions'), () => HttpResponse.json(sessions)),
    http.post(apiUrl('/users/:id/sessions/:sessionId/revoke'), async ({ params, request }) => {
      await record(`revoke:${params.sessionId as string}`, request);
      return new HttpResponse(null, { status: 204 });
    }),
    http.get(apiUrl('/parties'), () => HttpResponse.json({ items: [newParty], count: 1, totalCount: 1, totals: null })),
  );
  return { calls };
}

async function openTab(user: ReturnType<typeof renderApp>['user'], name: string) {
  await user.click(await screen.findByRole('tab', { name }));
  return screen.getByRole('tabpanel');
}

describe('Fiche d’un compte — onglets (Superviseur)', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('Permissions : l’origine est écrite en mots ; la liste complète voulue est envoyée', async () => {
    const { calls } = panelsServer();
    const { user } = renderApp('/users/u-agent');
    const panel = await openTab(user, 'Permissions');

    expect(await within(panel).findByText('Modèle de rôle : Agent')).toBeInTheDocument();
    expect(within(panel).getByText('Livrer un transfert').nextElementSibling).toHaveTextContent('du rôle');
    expect(within(panel).getByText('Activer le crédit').nextElementSibling).toHaveTextContent('ajoutée');
    expect(within(panel).getByText('Enregistrer le budget de la caisse').nextElementSibling).toHaveTextContent('retirée');
    expect(within(panel).getByRole('button', { name: 'Enregistrer les permissions' })).toBeDisabled();

    await user.click(within(panel).getByRole('switch', { name: 'Annuler un transfert' }));
    expect(within(panel).getByText('Annuler un transfert').nextElementSibling).toHaveTextContent('modifiée · à enregistrer');
    await user.click(within(panel).getByRole('switch', { name: 'Activer le crédit' }));
    await user.click(within(panel).getByRole('button', { name: 'Enregistrer les permissions' }));

    await waitFor(() => expect(calls).toHaveLength(1));
    expect(calls[0]).toEqual({ path: 'permissions', body: { permissions: ['transfers.cancel', 'transfers.deliver'] } });
    expect(await within(panel).findByText('Permissions enregistrées : effet immédiat.')).toBeInTheDocument();
  });

  it('Tiers autorisés : retirer, ajouter, puis envoyer la liste', async () => {
    const { calls } = panelsServer();
    const { user } = renderApp('/users/u-agent');
    const panel = await openTab(user, 'Tiers autorisés');

    expect(await within(panel).findByText('Boutique Fictive')).toBeInTheDocument();
    await user.click(within(panel).getByRole('button', { name: 'Retirer Fournisseur Fictif' }));
    await user.type(within(panel).getByRole('searchbox'), 'Client');
    await user.click(await within(panel).findByRole('button', { name: /Client Fictif/ }));
    await user.click(within(panel).getByRole('button', { name: 'Enregistrer' }));

    await waitFor(() => expect(calls).toHaveLength(1));
    expect(calls[0]).toEqual({ path: 'party-scope', body: { limited: true, partyIds: ['p1', 'p3'] } });
  });

  it('Tiers autorisés : « Tous les tiers » vide la liste', async () => {
    const { calls } = panelsServer();
    const { user } = renderApp('/users/u-agent');
    const panel = await openTab(user, 'Tiers autorisés');
    await user.click(await within(panel).findByRole('radio', { name: 'Tous les tiers' }));
    await user.click(within(panel).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() => expect(calls[0]).toEqual({ path: 'party-scope', body: { limited: false, partyIds: [] } }));
  });

  it('Plafonds : vide = pas de limite ; trop de décimales refusé avant l’envoi', async () => {
    const { calls } = panelsServer();
    const { user } = renderApp('/users/u-agent');
    const panel = await openTab(user, 'Plafonds');

    const perDay = await within(panel).findByLabelText('Plafond par jour (transferts enregistrés)');
    expect(within(panel).getByLabelText('Plafond par transfert')).toHaveValue('5000000');
    await user.clear(perDay);
    const credit = within(panel).getByLabelText('Plafond de crédit par jour');
    await user.type(credit, '1,234');
    await user.click(within(panel).getByRole('button', { name: 'Enregistrer les plafonds' }));
    expect(within(panel).getByText('Deux décimales au plus en MRO.')).toBeInTheDocument();
    expect(calls).toEqual([]);

    await user.clear(credit);
    await user.type(credit, '500000');
    await user.click(within(panel).getByRole('button', { name: 'Enregistrer les plafonds' }));
    await waitFor(() => expect(calls).toHaveLength(1));
    expect(calls[0]).toEqual({ path: 'limits', body: { perTransfer: 5_000_000, perDay: null, creditPerDay: 500_000 } });
  });

  it('Sessions : liste les appareils et ferme une session', async () => {
    const { calls } = panelsServer();
    const { user } = renderApp('/users/u-agent');
    const panel = await openTab(user, 'Sessions');

    expect(await within(panel).findByText('Téléphone fictif')).toBeInTheDocument();
    expect(within(panel).getByText('192.0.2.10')).toBeInTheDocument();
    await user.click(within(panel).getByRole('button', { name: 'Fermer la session' }));
    await waitFor(() => expect(calls.map((c) => c.path)).toEqual(['revoke:s1']));
  });

  it('un Superviseur ciblé garde tout : ni interrupteur, ni plafond, ni liste de tiers', async () => {
    panelsServer({ ...target, id: 'u-sup', role: 'Supervisor', agency: null });
    const { user } = renderApp('/users/u-sup');
    let panel = await openTab(user, 'Permissions');
    expect(await within(panel).findByText(/Un Superviseur a toujours toutes les permissions/)).toBeInTheDocument();
    expect(within(panel).queryByRole('switch')).not.toBeInTheDocument();
    panel = await openTab(user, 'Plafonds');
    expect(within(panel).getByText('Un Superviseur n’a jamais de plafond.')).toBeInTheDocument();
    panel = await openTab(user, 'Tiers autorisés');
    expect(within(panel).getByText('Un Superviseur voit toujours tous les tiers.')).toBeInTheDocument();
  });
});

describe('Fiche d’un compte — gestionnaire non Superviseur', () => {
  it('lit permissions et plafonds sans les modifier ; pas d’onglet « Tiers autorisés »', async () => {
    signedInAs({ ...agentAccount, permissions: [...agentAccount.permissions, 'users.manage'] });
    panelsServer();
    const { user } = renderApp('/users/u-agent');

    let panel = await openTab(user, 'Permissions');
    expect(await within(panel).findByText('Livrer un transfert')).toBeInTheDocument();
    expect(within(panel).queryByRole('switch')).not.toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: 'Enregistrer les permissions' })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Tiers autorisés' })).not.toBeInTheDocument();

    panel = await openTab(user, 'Plafonds');
    expect(await within(panel).findByText('Plafond de crédit par jour')).toBeInTheDocument();
    expect(within(panel).getByText('pas de limite')).toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: 'Enregistrer les plafonds' })).not.toBeInTheDocument();
  });
});
