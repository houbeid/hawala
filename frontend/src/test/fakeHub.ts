import type { RealtimeConnection, RealtimeConnectionFactory, RealtimeStatus } from '@/shared/realtime/connection';

// Faux hub SignalR pour les tests (méthode : « client SignalR injecté par contexte, faux hub ») : l'application s'y
// connecte comme au vrai, et le test émet les messages du serveur (emit) ou change l'état de la connexion (status).
// Jamais de requête réseau : le vrai client SignalR n'est pas utilisé dans jsdom.

export interface FakeHub {
  factory: RealtimeConnectionFactory;
  /** Envoie un message du serveur à toutes les connexions ouvertes. */
  emit(message: string, payload?: unknown): void;
  /** Change l'état de la connexion (reconnexion, perte). */
  status(status: RealtimeStatus): void;
  /** Jetons passés aux connexions ouvertes (pour vérifier que le jeton en mémoire est utilisé). */
  tokens(): (string | null)[];
  /** Nombre de connexions démarrées et pas encore arrêtées. */
  openCount(): number;
  /** Fait échouer les prochains démarrages. */
  failStarts(count: number): void;
}

export function createFakeHub(): FakeHub {
  const open = new Set<{ handlers: Map<string, ((payload: unknown) => void)[]>; onStatus: ((s: RealtimeStatus) => void)[]; token: () => string | null }>();
  let failures = 0;
  return {
    factory: (accessToken) => {
      const entry = { handlers: new Map<string, ((payload: unknown) => void)[]>(), onStatus: [] as ((s: RealtimeStatus) => void)[], token: accessToken };
      const connection: RealtimeConnection = {
        start: () => {
          if (failures > 0) {
            failures -= 1;
            return Promise.reject(new Error('Hub injoignable (test).'));
          }
          open.add(entry);
          return Promise.resolve();
        },
        stop: () => {
          open.delete(entry);
          return Promise.resolve();
        },
        on: (message, handler) => entry.handlers.set(message, [...(entry.handlers.get(message) ?? []), handler]),
        onStatus: (handler) => entry.onStatus.push(handler),
      };
      return connection;
    },
    emit: (message, payload) => {
      for (const entry of open) for (const handler of entry.handlers.get(message) ?? []) handler(payload);
    },
    status: (status) => {
      for (const entry of open) for (const handler of entry.onStatus) handler(status);
    },
    tokens: () => [...open].map((e) => e.token()),
    openCount: () => open.size,
    failStarts: (count) => {
      failures = count;
    },
  };
}
