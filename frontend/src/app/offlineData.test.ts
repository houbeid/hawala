import { QueryClient } from '@tanstack/react-query';
import { createMemoryStore, QUERIES_KEY } from '@/shared/offline/offlineStore';
import { startOfflineCache } from './offlineData';

// Cache gardé sur le téléphone : seulement les données de la lecture hors ligne (taux, trésorerie, tiers, transferts,
// agences), rechargé seulement pour le même compte et s'il a moins de 12 h, jamais écrit hors d'une session ouverte.

const HOUR = 60 * 60 * 1000;

describe('startOfflineCache', () => {
  beforeEach(() => vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }));
  afterEach(() => vi.useRealTimers());

  it('garde les transferts, les taux et les tiers, jamais la caisse, le budget, le bilan ni les comptes', async () => {
    const client = new QueryClient();
    const store = createMemoryStore();
    const stop = startOfflineCache(client, store, 'a1', () => true);

    client.setQueryData(['transfers', 'list', {}], { items: ['T'] });
    client.setQueryData(['rates', 'day', '2026-10-24'], { saleRate: 3650 });
    client.setQueryData(['parties', 'list', {}], { items: [] });
    client.setQueryData(['cash', 'list', {}], { items: ['C'] });
    client.setQueryData(['balance', 'overview'], { caisses: [] });
    client.setQueryData(['users', 'list', {}], { items: [] });
    await vi.advanceTimersByTimeAsync(2_000);

    const saved = store.data.get(QUERIES_KEY) as { accountId: string; state: { queries: { queryKey: unknown[] }[] } };
    expect(saved.accountId).toBe('a1');
    expect(saved.state.queries.map((q) => q.queryKey[0]).sort()).toEqual(['parties', 'rates', 'transfers']);
    stop();
  });

  it('n’écrit rien quand la session n’est plus ouverte (déconnexion, lecture hors ligne)', async () => {
    const client = new QueryClient();
    const store = createMemoryStore();
    const stop = startOfflineCache(client, store, 'a1', () => false);

    client.setQueryData(['transfers', 'list', {}], { items: ['T'] });
    await vi.advanceTimersByTimeAsync(2_000);

    expect(store.data.has(QUERIES_KEY)).toBe(false);
    stop();
  });

  it('recharge le cache du même compte s’il a moins de 12 h, jamais celui d’un autre compte ni un trop vieux', async () => {
    const source = new QueryClient();
    const store = createMemoryStore();
    const stopSource = startOfflineCache(source, store, 'a1', () => true);
    source.setQueryData(['transfers', 'list', {}], { items: ['T'] });
    await vi.advanceTimersByTimeAsync(2_000);
    stopSource();
    const saved = store.data.get(QUERIES_KEY) as { savedAt: string };

    const sameReader = new QueryClient();
    const stopSame = startOfflineCache(sameReader, store, 'a1', () => false);
    await vi.waitFor(() => expect(sameReader.getQueryData(['transfers', 'list', {}])).toEqual({ items: ['T'] }));
    stopSame();

    const other = new QueryClient();
    const stopOther = startOfflineCache(other, store, 'a2', () => false);
    await Promise.resolve();
    await Promise.resolve();
    expect(other.getQueryData(['transfers', 'list', {}])).toBeUndefined();
    stopOther();

    await store.set(QUERIES_KEY, { ...(store.data.get(QUERIES_KEY) as object), savedAt: new Date(Date.parse(saved.savedAt) - 13 * HOUR).toISOString() });
    const late = new QueryClient();
    const stopLate = startOfflineCache(late, store, 'a1', () => false);
    await Promise.resolve();
    await Promise.resolve();
    expect(late.getQueryData(['transfers', 'list', {}])).toBeUndefined();
    stopLate();
  });
});
