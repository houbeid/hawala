import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { agentAccount, tokens } from '@/test/fixtures';
import { apiUrl, server } from '@/test/server';
import { Can } from './Can';
import { useSession } from './hooks';
import { SessionManager } from './session';
import { SessionProvider } from './SessionProvider';

function Status() {
  return <p>état : {useSession().status}</p>;
}

describe('SessionProvider et <Can>', () => {
  it('reprend la session au montage puis affiche seulement les actions permises', async () => {
    server.use(
      http.post(apiUrl('/auth/refresh'), () => HttpResponse.json(tokens('t1'))),
      http.get(apiUrl('/auth/me'), () => HttpResponse.json(agentAccount)),
    );

    render(
      <SessionProvider session={new SessionManager()}>
        <Status />
        <Can permission="transfers.create">
          <button type="button">Nouveau transfert</button>
        </Can>
        <Can permission="transfers.cancel">
          <button type="button">Annuler</button>
        </Can>
      </SessionProvider>,
    );

    expect(await screen.findByText('état : authenticated')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Nouveau transfert' })).toBeInTheDocument();
    // Absent, pas grisé.
    expect(screen.queryByRole('button', { name: 'Annuler' })).not.toBeInTheDocument();
  });

  it("n'affiche aucune action protégée sans session", async () => {
    server.use(http.post(apiUrl('/auth/refresh'), () => HttpResponse.json({ code: 'Identity.RefreshToken.Invalid' }, { status: 401 })));

    render(
      <SessionProvider session={new SessionManager()}>
        <Status />
        <Can permission="transfers.create">
          <button type="button">Nouveau transfert</button>
        </Can>
      </SessionProvider>,
    );

    expect(await screen.findByText('état : anonymous')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
