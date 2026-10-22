import { http, HttpResponse } from 'msw';
import { agentAccount, tokens } from '@/test/fixtures';
import { apiUrl, server } from '@/test/server';
import { ApiError } from '@/shared/api/errors';
import { SessionManager } from './session';

const unauthorized = (code: string) => HttpResponse.json({ status: 401, code }, { status: 401 });

/** /auth/me qui n'accepte que le jeton donné (comme le vrai serveur après un changement de SecurityVersion). */
function meAccepting(token: string, account = agentAccount) {
  return http.get(apiUrl('/auth/me'), ({ request }) =>
    request.headers.get('Authorization') === `Bearer ${token}` ? HttpResponse.json(account) : new HttpResponse(null, { status: 401 }),
  );
}

describe('SessionManager — reprise au chargement', () => {
  it('reprend la session grâce au cookie, puis lit le compte et ses permissions', async () => {
    server.use(http.post(apiUrl('/auth/refresh'), () => HttpResponse.json(tokens('t1'))), meAccepting('t1'));
    const session = new SessionManager();

    await session.start();

    expect(session.getState()).toEqual({ status: 'authenticated', account: agentAccount });
    expect(session.getAccessToken()).toBe('t1');
  });

  it("passe à « non connecté » sans cookie valable", async () => {
    server.use(http.post(apiUrl('/auth/refresh'), () => unauthorized('Identity.RefreshToken.Invalid')));
    const session = new SessionManager();

    await session.start();

    expect(session.getState()).toEqual({ status: 'anonymous', reason: null });
    expect(session.getAccessToken()).toBeNull();
  });

  it('signale un serveur injoignable, puis réessaie', async () => {
    server.use(http.post(apiUrl('/auth/refresh'), () => HttpResponse.error()));
    const session = new SessionManager();

    await session.start();
    expect(session.getState()).toEqual({ status: 'unreachable' });

    server.use(http.post(apiUrl('/auth/refresh'), () => HttpResponse.json(tokens('t1'))), meAccepting('t1'));
    await session.retry();
    expect(session.getState().status).toBe('authenticated');
  });

  it('ne lance la reprise qu’une fois, même appelée deux fois', async () => {
    let refreshes = 0;
    server.use(
      http.post(apiUrl('/auth/refresh'), () => {
        refreshes += 1;
        return HttpResponse.json(tokens('t1'));
      }),
      meAccepting('t1'),
    );
    const session = new SessionManager();

    await Promise.all([session.start(), session.start()]);

    expect(refreshes).toBe(1);
  });
});

describe('SessionManager — connexion', () => {
  it('se connecte et lit le compte', async () => {
    let body: unknown = null;
    server.use(
      http.post(apiUrl('/auth/login'), async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(tokens('t1'));
      }),
      meAccepting('t1'),
    );
    const session = new SessionManager();

    await session.login({ login: 'agent.nkc@example.com', password: 'motdepasse1' });

    expect(body).toEqual({ login: 'agent.nkc@example.com', password: 'motdepasse1' });
    expect(session.getState()).toEqual({ status: 'authenticated', account: agentAccount });
  });

  it("laisse remonter un refus au formulaire, sans tenter de rafraîchissement", async () => {
    let refreshes = 0;
    server.use(
      http.post(apiUrl('/auth/login'), () => unauthorized('Identity.Auth.InvalidCredentials')),
      http.post(apiUrl('/auth/refresh'), () => {
        refreshes += 1;
        return HttpResponse.json(tokens('t1'));
      }),
    );
    const session = new SessionManager();

    const error = (await session.login({ login: 'x@example.com', password: 'faux' }).catch((e: unknown) => e)) as ApiError;

    expect(error).toBeInstanceOf(ApiError);
    expect(error.code).toBe('Identity.Auth.InvalidCredentials');
    expect(refreshes).toBe(0);
  });

  it('avec un mot de passe temporaire : seul le changement est permis, puis la session s’ouvre', async () => {
    let meCalls = 0;
    let changeAuth: string | null = null;
    server.use(
      http.post(apiUrl('/auth/login'), () => HttpResponse.json(tokens('temp', { mustChangePassword: true }))),
      http.get(apiUrl('/auth/me'), ({ request }) => {
        meCalls += 1;
        return request.headers.get('Authorization') === 'Bearer t2'
          ? HttpResponse.json(agentAccount)
          : new HttpResponse(null, { status: 403 });
      }),
      http.post(apiUrl('/auth/change-password'), ({ request }) => {
        changeAuth = request.headers.get('Authorization');
        return HttpResponse.json(tokens('t2'));
      }),
    );
    const session = new SessionManager();

    await session.login({ login: 'agent.nkc@example.com', password: 'abc-def-gh2' });
    expect(session.getState()).toEqual({ status: 'mustChangePassword' });
    expect(meCalls).toBe(0);

    await session.changePassword({ currentPassword: 'abc-def-gh2', newPassword: 'nouveau123' });
    expect(changeAuth).toBe('Bearer temp');
    expect(session.getState().status).toBe('authenticated');
  });
});

describe('SessionManager — jeton expiré', () => {
  async function connected(): Promise<SessionManager> {
    server.use(http.post(apiUrl('/auth/login'), () => HttpResponse.json(tokens('t1'))), meAccepting('t1'));
    const session = new SessionManager();
    await session.login({ login: 'agent.nkc@example.com', password: 'motdepasse1' });
    return session;
  }

  it('trois requêtes refusées en même temps ne font qu’un seul rafraîchissement', async () => {
    const session = await connected();
    let refreshes = 0;
    server.use(
      http.post(apiUrl('/auth/refresh'), async () => {
        refreshes += 1;
        await new Promise((resolve) => setTimeout(resolve, 20));
        return HttpResponse.json(tokens('t2'));
      }),
      http.get(apiUrl('/data/:n'), ({ request, params }) =>
        request.headers.get('Authorization') === 'Bearer t2'
          ? HttpResponse.json({ n: params.n })
          : new HttpResponse(null, { status: 401 }),
      ),
      meAccepting('t2'),
    );

    const results = await Promise.all(['1', '2', '3'].map((n) => session.http.get<{ n: string }>(`/data/${n}`)));

    expect(results.map((r) => r.n)).toEqual(['1', '2', '3']);
    expect(refreshes).toBe(1);
    expect(session.getAccessToken()).toBe('t2');
  });

  it('relit le compte après un rafraîchissement causé par un 401 (permissions changées)', async () => {
    const session = await connected();
    const updated = { ...agentAccount, permissions: [...agentAccount.permissions, 'profit.view'] };
    server.use(
      http.post(apiUrl('/auth/refresh'), () => HttpResponse.json(tokens('t2'))),
      http.get(apiUrl('/data'), ({ request }) =>
        request.headers.get('Authorization') === 'Bearer t2' ? HttpResponse.json({}) : new HttpResponse(null, { status: 401 }),
      ),
      meAccepting('t2', updated),
    );

    await session.http.get('/data');
    await session.reloadAccount();

    const state = session.getState();
    expect(state.status === 'authenticated' && state.account.permissions).toContain('profit.view');
  });

  it('ferme la session si le rafraîchissement est refusé', async () => {
    const session = await connected();
    server.use(
      http.post(apiUrl('/auth/refresh'), () => unauthorized('Identity.RefreshToken.Revoked')),
      http.get(apiUrl('/data'), () => new HttpResponse(null, { status: 401 })),
    );

    await expect(session.http.get('/data')).rejects.toBeInstanceOf(ApiError);

    expect(session.getState()).toEqual({ status: 'anonymous', reason: 'expired' });
    expect(session.getAccessToken()).toBeNull();
  });

  it('rafraîchit avant l’expiration du jeton, sans attendre un 401', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    try {
      server.use(
        http.post(apiUrl('/auth/login'), () =>
          HttpResponse.json(tokens('t1', { accessTokenExpiresAt: new Date(Date.now() + 15 * 60_000).toISOString() })),
        ),
        meAccepting('t1'),
        http.post(apiUrl('/auth/refresh'), () => HttpResponse.json(tokens('t2'))),
      );
      const session = new SessionManager();
      await session.login({ login: 'agent.nkc@example.com', password: 'motdepasse1' });

      await vi.advanceTimersByTimeAsync(13 * 60_000);
      expect(session.getAccessToken()).toBe('t1');

      await vi.advanceTimersByTimeAsync(60_000 + 1);
      await vi.waitFor(() => expect(session.getAccessToken()).toBe('t2'));
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('SessionManager — déconnexion', () => {
  it('appelle /auth/logout et oublie le jeton, même si le serveur échoue', async () => {
    let loggedOut = false;
    server.use(
      http.post(apiUrl('/auth/login'), () => HttpResponse.json(tokens('t1'))),
      meAccepting('t1'),
      http.post(apiUrl('/auth/logout'), () => {
        loggedOut = true;
        return new HttpResponse(null, { status: 500 });
      }),
    );
    const session = new SessionManager();
    await session.login({ login: 'agent.nkc@example.com', password: 'motdepasse1' });

    await session.logout();

    expect(loggedOut).toBe(true);
    expect(session.getState()).toEqual({ status: 'anonymous', reason: null });
    expect(session.getAccessToken()).toBeNull();
  });

  it('se ferme sur ordre du serveur (SignalR) avec le motif', async () => {
    server.use(http.post(apiUrl('/auth/login'), () => HttpResponse.json(tokens('t1'))), meAccepting('t1'));
    const session = new SessionManager();
    await session.login({ login: 'agent.nkc@example.com', password: 'motdepasse1' });
    const seen: string[] = [];
    session.subscribe(() => seen.push(session.getState().status));

    session.signOut('signedOut');

    expect(session.getState()).toEqual({ status: 'anonymous', reason: 'signedOut' });
    expect(seen).toEqual(['anonymous']);
  });
});
