import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { addDays, todayIso } from '@/shared/format/dates';
import { agentAccount, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { AuditResponse } from '../types';

// Journal d'audit : phrases construites par l'interface à partir du code de l'action. Données fictives uniquement.

const response: AuditResponse = {
  page: 1,
  totalCount: 3,
  items: [
    {
      id: 'a1',
      occurredAt: '2026-10-24T09:15:00Z',
      action: 'Identity.Account.PermissionsChanged',
      entityType: 'UserAccount',
      entityId: 'u1',
      entityName: 'Agent Fictif',
      actor: { id: 's', name: 'Superviseur Fictif' },
      agencyId: nkc.id,
      reason: null,
      changes: [
        { field: 'transfers.cancel', before: 'not_granted', after: 'granted' },
        { field: 'cash.budget.save', before: 'granted', after: 'not_granted' },
      ],
      ipAddress: '192.0.2.10',
      device: 'Navigateur fictif',
    },
    {
      id: 'a2',
      occurredAt: '2026-10-24T08:00:00Z',
      action: 'Parties.Party.Updated',
      entityType: 'Party',
      entityId: 'p1',
      entityName: 'Boutique Fictive',
      actor: { id: 's', name: 'Superviseur Fictif' },
      agencyId: null,
      reason: null,
      changes: [
        { field: 'phone', before: 'set', after: 'changed' },
        { field: 'inBalanceSheet', before: 'true', after: 'false' },
      ],
      ipAddress: null,
      device: null,
    },
    {
      id: 'a3',
      occurredAt: '2026-10-24T07:00:00Z',
      action: 'Transfers.Transfer.Cancelled',
      entityType: 'Transfer',
      entityId: 't1',
      entityName: null,
      actor: { id: 's', name: 'Superviseur Fictif' },
      agencyId: nkc.id,
      reason: 'Erreur de saisie',
      changes: [],
      ipAddress: null,
      device: null,
    },
  ],
};

function auditServer() {
  const queries: URLSearchParams[] = [];
  server.use(
    http.get(apiUrl('/audit'), ({ request }) => {
      queries.push(new URL(request.url).searchParams);
      return HttpResponse.json(response);
    }),
  );
  return { queries };
}

describe('Journal d’audit', () => {
  beforeEach(() => signedInAs({ ...supervisorAccount }));

  it('chaque action se lit comme une phrase ; une permission accordée ou retirée aussi', async () => {
    auditServer();
    renderApp('/audit');

    expect(await screen.findByText('Superviseur Fictif a changé les permissions de Agent Fictif')).toBeInTheDocument();
    expect(screen.getByText('a accordé « Annuler un transfert »')).toBeInTheDocument();
    expect(screen.getByText('a retiré « Enregistrer le budget de la caisse »')).toBeInTheDocument();
    expect(screen.getByText('3 actions')).toBeInTheDocument();
  });

  it('les valeurs sensibles restent masquées ; motif et appareil sont lisibles', async () => {
    auditServer();
    const { user } = renderApp('/audit');
    const line = (await screen.findByText('Superviseur Fictif a modifié la fiche du tiers Boutique Fictive')).closest('li') as HTMLElement;
    const phone = within(line).getByText('Téléphone :').parentElement!;
    expect(phone).toHaveTextContent('renseigné');
    expect(phone).toHaveTextContent('changé');
    expect(within(line).getByText('Dans le bilan :').parentElement).toHaveTextContent(/oui.*non/);

    const cancel = screen.getByText('Superviseur Fictif a annulé un transfert').closest('li') as HTMLElement;
    expect(within(cancel).getByText('Motif : Erreur de saisie')).toBeInTheDocument();

    await user.click(screen.getByText('Appareil'));
    expect(screen.getByText('192.0.2.10 · Navigateur fictif')).toBeVisible();
  });

  it('envoie la période et la famille d’actions', async () => {
    const { queries } = auditServer();
    const { user } = renderApp('/audit');
    await screen.findByText('3 actions');

    await user.click(screen.getByRole('button', { name: '7 derniers jours' }));
    await waitFor(() => expect(queries.at(-1)?.get('from')).toBe(addDays(todayIso(), -6)));
    await user.selectOptions(screen.getByLabelText('Actions'), 'Identity.Account');
    await waitFor(() => expect(queries.at(-1)?.get('action')).toBe('Identity.Account'));
    expect(queries[0]?.has('agencyId')).toBe(false);
  });

  it('sans audit.view : écran refusé', async () => {
    signedInAs(agentAccount);
    renderApp('/audit');
    expect(await screen.findByText("Votre compte n'a pas accès à cet écran.")).toBeInTheDocument();
  });
});
