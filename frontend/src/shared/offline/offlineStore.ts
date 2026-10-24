// Stockage du téléphone pour la lecture hors ligne (décisions du 24 octobre 2026) : le profil du dernier compte
// connecté, l'heure de son dernier contact avec le serveur et les données préchargées. JAMAIS de jeton ni de mot de
// passe (le jeton reste en mémoire). Tout est effacé à la déconnexion, quand le serveur refuse la session, et au-delà
// de 12 h sans contact avec le serveur (durée de la session).

/** Au-delà, les données gardées ne s'affichent plus : il faut le réseau pour se reconnecter. */
export const OFFLINE_MAX_AGE_MS = 12 * 60 * 60 * 1000;

export interface KeyValueStore {
  get<T>(key: string): Promise<T | undefined>;
  set(key: string, value: unknown): Promise<void>;
  /** Efface tout (déconnexion, session refusée, données trop vieilles). */
  clear(): Promise<void>;
}

const DATABASE = 'hawala-offline';
/** Base à part pour la file des créations : jamais effacée par une déconnexion (l'argent encaissé ne se perd pas). */
export const QUEUE_DATABASE = 'hawala-queue';
const STORE = 'kv';

function request<T>(req: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error ?? new Error('IndexedDB'));
  });
}

/**
 * IndexedDB du navigateur. Indisponible (navigation privée, vieux navigateur) : rien n'est gardé et l'app marche
 * comme avant, en ligne seulement.
 */
export function createIndexedDbStore(name: string = DATABASE): KeyValueStore {
  let database: Promise<IDBDatabase | null> | null = null;
  const open = () => {
    database ??= new Promise((resolve) => {
      if (typeof indexedDB === 'undefined') return resolve(null);
      const req = indexedDB.open(name, 1);
      req.onupgradeneeded = () => req.result.createObjectStore(STORE);
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => resolve(null);
    });
    return database;
  };
  const run = async <T>(mode: IDBTransactionMode, work: (store: IDBObjectStore) => IDBRequest<T>): Promise<T | undefined> => {
    const db = await open();
    if (!db) return undefined;
    try {
      return await request(work(db.transaction(STORE, mode).objectStore(STORE)));
    } catch {
      return undefined;
    }
  };
  return {
    get: <T>(key: string) => run('readonly', (s) => s.get(key) as IDBRequest<T>),
    set: async (key, value) => {
      await run('readwrite', (s) => s.put(value, key));
    },
    clear: async () => {
      await run('readwrite', (s) => s.clear());
    },
  };
}

/** Stockage en mémoire : tests, ou repli. */
export function createMemoryStore(initial: Record<string, unknown> = {}): KeyValueStore & { data: Map<string, unknown> } {
  const data = new Map<string, unknown>(Object.entries(initial));
  return {
    data,
    get: async <T>(key: string) => data.get(key) as T | undefined,
    set: async (key, value) => {
      data.set(key, structuredClone(value));
    },
    clear: async () => {
      data.clear();
    },
  };
}

/** Le profil du dernier compte connecté et l'heure de son dernier contact avec le serveur (ISO). */
export interface OfflineProfile<TAccount> {
  account: TAccount;
  lastContactAt: string;
}

export const PROFILE_KEY = 'profile';
export const QUERIES_KEY = 'queries';

/** Vrai tant que le dernier contact avec le serveur date de moins de 12 h (et n'est pas dans le futur). */
export function isFresh(lastContactAt: string, now: number = Date.now()): boolean {
  const at = Date.parse(lastContactAt);
  return !Number.isNaN(at) && now - at < OFFLINE_MAX_AGE_MS && at <= now + 60_000;
}
