import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { agentAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { MySession } from '../sessions';

// Mes appareils : chaque compte voit et ferme ses propres sessions. Données fictives uniquement.

const sessions: MySession[] = [
  { id: 's-other', startedAt: '2026-10-23T08:00:00Z', lastUsedAt: '2026-10-23T18:00:00Z', expiresAt: '2026-10-24T06:00:00Z', device: 'Téléphone perdu', ipAddress: '198.51.100.7', current: false },
  { id: 's-here', startedAt: '2026-10-24T08:00:00Z', lastUsedAt: '2026-10-24T11:00:00Z', expiresAt: '2026-10-24T20:00:00Z', device: 'Ordinateur fictif', ipAddress: '192.0.2.10', current: true },
];

describe('Mes appareils', () => {
  beforeEach(() => signedInAs(agentAccount));

  it('montre cet appareil en premier, sans bouton « Fermer », et ferme une autre session', async () => {
    let list = [...sessions];
    const revoked: string[] = [];
    server.use(
      http.get(apiUrl('/auth/sessions'), () => HttpResponse.json(list)),
      http.post(apiUrl('/auth/sessions/:id/revoke'), ({ params }) => {
        revoked.push(params.id as string);
        list = list.filter((s) => s.id !== params.id);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { user } = renderApp('/me/sessions');

    const items = within(await screen.findByRole('list', { name: 'Mes appareils' })).getAllByRole('listitem');
    expect(items[0]).toHaveTextContent('Ordinateur fictif · cet appareil');
    expect(within(items[0]!).getByText('Pour cet appareil, utilisez « Se déconnecter ».')).toBeInTheDocument();
    expect(within(items[0]!).queryByRole('button')).not.toBeInTheDocument();

    await user.click(within(items[1]!).getByRole('button', { name: 'Fermer la session' }));
    await waitFor(() => expect(revoked).toEqual(['s-other']));
    await waitFor(() => expect(screen.queryByText('Téléphone perdu')).not.toBeInTheDocument());
  });
});
