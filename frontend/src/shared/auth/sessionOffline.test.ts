import { http, HttpResponse } from 'msw';
import { agentAccount, tokens } from '@/test/fixtures';
import { apiUrl, server } from '@/test/server';
import { createMemoryStore, PROFILE_KEY, QUERIES_KEY } from '@/shared/offline/offlineStore';
import { SessionManager } from './session';

// Lecture hors ligne (décisions du 24 octobre 2026) : profil du compte et heure du dernier contact gardés (jamais le
// jeton) ; app rouverte sans réseau moins de 12 h après ce contact = « offline » ; au-delà, tout est effacé ; retour du
// réseau = session reprise, ou tout effacé si le serveur la refuse ; déconnexion = tout effacé.

const HOUR = 60 * 60 * 1000;
const ago = (ms: number) => new Date(Date.now() - ms).toISOString();

function online() {
  server.use(
    http.post(apiUrl('/auth/refresh'), () => HttpResponse.json(tokens('t1'))),
    http.get(apiUrl('/auth/me'), () => HttpResponse.json(agentAccount)),
  );
}

const unreachable = () => server.use(http.post(apiUrl('/auth/refresh'), () => HttpResponse.error()));

describe('SessionManager — lecture hors ligne', () => {
  it('garde le profil et l’heure du dernier contact, jamais le jeton', async () => {
    online();
    const store = createMemoryStore();
    const session = new SessionManager({ offline: store });

    await session.start();

    const saved = store.data.get(PROFILE_KEY) as { account: unknown; lastContactAt: string };
    expect(saved.account).toEqual(agentAccount);
    expect(Date.now() - Date.parse(saved.lastContactAt)).toBeLessThan(5_000);
    expect(JSON.stringify([...store.data.values()])).not.toContain('t1');
  });

  it('rouverte sans réseau moins de 12 h après : lecture seule du dernier compte', async () => {
    unreachable();
    const since = ago(3 * HOUR);
    const session = new SessionManager({ offline: createMemoryStore({ [PROFILE_KEY]: { account: agentAccount, lastContactAt: since } }) });

    await session.start();

    expect(session.getState()).toEqual({ status: 'offline', account: agentAccount, since });
    expect(session.getAccessToken()).toBeNull();
  });

  it('au-delà de 12 h : tout est effacé et le serveur est injoignable', async () => {
    unreachable();
    const store = createMemoryStore({ [PROFILE_KEY]: { account: agentAccount, lastContactAt: ago(13 * HOUR) }, [QUERIES_KEY]: { x: 1 } });
    const session = new SessionManager({ offline: store });

    await session.start();

    expect(session.getState()).toEqual({ status: 'unreachable' });
    expect(store.data.size).toBe(0);
  });

  it('sans stockage : comme avant, serveur injoignable', async () => {
    unreachable();
    const session = new SessionManager();
    await session.start();
    expect(session.getState()).toEqual({ status: 'unreachable' });
  });

  it('retour du réseau : la session reprend', async () => {
    unreachable();
    const session = new SessionManager({ offline: createMemoryStore({ [PROFILE_KEY]: { account: agentAccount, lastContactAt: ago(HOUR) } }) });
    await session.start();

    online();
    await session.reconnect();

    expect(session.getState()).toEqual({ status: 'authenticated', account: agentAccount });
    expect(session.getAccessToken()).toBe('t1');
  });

  it('toujours injoignable : reste hors ligne', async () => {
    unreachable();
    const session = new SessionManager({ offline: createMemoryStore({ [PROFILE_KEY]: { account: agentAccount, lastContactAt: ago(HOUR) } }) });
    await session.start();

    await session.reconnect();

    expect(session.getState().status).toBe('offline');
  });

  it('session refusée au retour du réseau (compte bloqué) : tout est effacé, retour à la connexion', async () => {
    unreachable();
    const store = createMemoryStore({ [PROFILE_KEY]: { account: agentAccount, lastContactAt: ago(HOUR) }, [QUERIES_KEY]: { x: 1 } });
    const session = new SessionManager({ offline: store });
    await session.start();

    server.use(http.post(apiUrl('/auth/refresh'), () => HttpResponse.json({ status: 401, code: 'Identity.RefreshToken.Invalid' }, { status: 401 })));
    await session.reconnect();

    expect(session.getState()).toEqual({ status: 'anonymous', reason: 'expired' });
    expect(store.data.size).toBe(0);
  });

  it('la déconnexion efface tout ce qui est gardé', async () => {
    online();
    server.use(http.post(apiUrl('/auth/logout'), () => new HttpResponse(null, { status: 204 })));
    const store = createMemoryStore();
    const session = new SessionManager({ offline: store });
    await session.start();
    await store.set(QUERIES_KEY, { x: 1 });

    await session.logout();

    expect(store.data.size).toBe(0);
  });
});
