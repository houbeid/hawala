import { isApiError } from '@/shared/api/errors';
import { createHttpClient, type AccessTokenSource, type HttpClient } from '@/shared/api/http';
import { isFresh, PROFILE_KEY, type KeyValueStore, type OfflineProfile } from '@/shared/offline/offlineStore';
import * as authApi from './authApi';
import type { AuthResponse, ChangePasswordRequest, CurrentAccount, LoginRequest } from './types';

// Session du compte connecté, sans React (testable seule). Règles décidées le 22 octobre 2026 :
// - le jeton d'accès vit en MÉMOIRE seulement (jamais localStorage : un script injecté ne peut pas le lire
//   après coup) ; au rechargement de la page, on repasse par /auth/refresh grâce au cookie HttpOnly ;
// - un seul rafraîchissement à la fois : les requêtes en 401 partagent la même promesse, et un verrou
//   Web Locks sérialise aussi entre onglets (le backend accepte deux rafraîchissements simultanés,
//   limite connue : on évite de la déclencher).
// - lecture hors ligne (24 octobre 2026) : avec un stockage `offline`, le profil du compte et l'heure du dernier
//   contact avec le serveur sont gardés (jamais le jeton) ; rouverte sans réseau moins de 12 h après ce contact,
//   l'app passe en « offline » (lecture seule) et reprend la session dès que le serveur répond.

export type SignOutReason = 'expired' | 'signedOut' | 'sessionClosed';

export type SessionState =
  | { status: 'loading' }
  | { status: 'anonymous'; reason: SignOutReason | null }
  /** Serveur injoignable au démarrage : la session existe peut-être encore, l'interface propose de réessayer. */
  | { status: 'unreachable' }
  /** Mot de passe temporaire : seul le changement de mot de passe est permis (GET /auth/me répond 403). */
  | { status: 'mustChangePassword' }
  | { status: 'authenticated'; account: CurrentAccount }
  /**
   * Rouverte sans réseau, moins de 12 h après le dernier contact avec le serveur : lecture seule des données gardées
   * du dernier compte connecté (aucun jeton). `since` : heure de ce dernier contact (ISO).
   */
  | { status: 'offline'; account: CurrentAccount; since: string };

/** Le compte connecté (ou gardé hors ligne), ou null. */
export function accountOf(state: SessionState): CurrentAccount | null {
  return state.status === 'authenticated' || state.status === 'offline' ? state.account : null;
}

type Listener = () => void;

/** Marge avant l'expiration du jeton pour le rafraîchir sans attendre un 401. */
const REFRESH_AHEAD_MS = 60_000;
const MIN_REFRESH_DELAY_MS = 5_000;
const REFRESH_LOCK = 'hawala.auth.refresh';

export interface SessionManagerOptions {
  baseUrl?: string;
  /** Remplacé dans les tests (MSW intercepte le fetch global, ce n'est donc pas nécessaire en général). */
  fetch?: typeof fetch;
  /** Stockage du téléphone pour la lecture hors ligne ; absent : rien n'est gardé (en ligne seulement). */
  offline?: KeyValueStore;
}

export class SessionManager implements AccessTokenSource {
  /** Client avec jeton et rejeu après rafraîchissement : celui que toute l'application utilise. */
  readonly http: HttpClient;
  private readonly anonymous: HttpClient;

  private state: SessionState = { status: 'loading' };
  private readonly listeners = new Set<Listener>();
  private accessToken: string | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;
  private refreshing: Promise<boolean> | null = null;
  private reloadingAccount: Promise<void> | null = null;
  private started: Promise<void> | null = null;
  private reconnecting: Promise<void> | null = null;
  private readonly offline: KeyValueStore | undefined;

  constructor(options: SessionManagerOptions = {}) {
    this.offline = options.offline;
    this.anonymous = createHttpClient({ baseUrl: options.baseUrl, fetch: options.fetch });
    this.http = createHttpClient({ baseUrl: options.baseUrl, fetch: options.fetch, auth: this });
  }

  // --- État observable (useSyncExternalStore) ---

  getState = (): SessionState => this.state;

  subscribe = (listener: Listener): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  private setState(next: SessionState): void {
    this.state = next;
    for (const listener of this.listeners) listener();
  }

  // --- AccessTokenSource (utilisé par le client HTTP) ---

  getAccessToken(): string | null {
    return this.accessToken;
  }

  /** Appelé par le client HTTP sur un 401 : rafraîchit, puis relit le compte (permissions peut-être changées). */
  async refreshAccessToken(): Promise<boolean> {
    if (!this.accessToken) return false;
    const refreshed = await this.refreshSerialized();
    // Un 401 sur un jeton encore valide = SecurityVersion changée (rôle, permissions…) : on relit /auth/me.
    if (refreshed && this.state.status === 'authenticated') void this.reloadAccount();
    return refreshed;
  }

  // --- Cycle de vie ---

  /** Au chargement de l'application : reprend la session grâce au cookie, s'il y en a une. */
  start(): Promise<void> {
    this.started ??= this.resume();
    return this.started;
  }

  /** Après « Réessayer » sur l'écran « serveur injoignable ». */
  retry(): Promise<void> {
    this.started = null;
    this.setState({ status: 'loading' });
    return this.start();
  }

  private async resume(): Promise<void> {
    try {
      const tokens = await this.withRefreshLock(() => authApi.refresh(this.anonymous));
      await this.enter(tokens);
    } catch (error) {
      if (isApiError(error) && error.isNetworkError) {
        await this.enterOffline();
      } else {
        this.end(null);
      }
    }
  }

  /** Sans réseau au démarrage : le profil gardé s'il a moins de 12 h, sinon tout est effacé. */
  private async enterOffline(): Promise<void> {
    const saved = await this.offline?.get<OfflineProfile<CurrentAccount>>(PROFILE_KEY);
    if (saved && isFresh(saved.lastContactAt)) {
      this.setState({ status: 'offline', account: saved.account, since: saved.lastContactAt });
      return;
    }
    if (saved) await this.offline?.clear();
    this.setState({ status: 'unreachable' });
  }

  /**
   * En lecture hors ligne : nouvel essai de reprise de session (retour du réseau, ou à intervalles). Serveur
   * toujours injoignable : on reste hors ligne ; session refusée (compte bloqué, session fermée, expirée) : tout est
   * effacé et l'app revient à la connexion.
   */
  reconnect(): Promise<void> {
    if (this.state.status !== 'offline') return Promise.resolve();
    this.reconnecting ??= (async () => {
      try {
        const tokens = await this.withRefreshLock(() => authApi.refresh(this.anonymous));
        await this.enter(tokens);
      } catch (error) {
        if (!(isApiError(error) && error.isNetworkError)) this.end('expired');
      } finally {
        this.reconnecting = null;
      }
    })();
    return this.reconnecting;
  }

  /** Les erreurs (identifiants faux, compte verrouillé ou bloqué, 429) remontent au formulaire. */
  async login(request: LoginRequest): Promise<void> {
    const tokens = await authApi.login(this.anonymous, request);
    await this.enter(tokens);
  }

  async changePassword(request: ChangePasswordRequest): Promise<void> {
    const tokens = await authApi.changePassword(this.http, request);
    await this.enter(tokens);
  }

  async logout(): Promise<void> {
    try {
      await authApi.logout(this.anonymous);
    } catch {
      // Déconnexion locale quand même : le refresh token expirera seul côté serveur.
    }
    this.end(null);
  }

  /** Fin de session décidée ailleurs : SignalR « SignedOut » / « SessionClosed », ou session perdue. */
  signOut(reason: SignOutReason): void {
    this.end(reason);
  }

  /** Relit /auth/me (nom, agence, permissions). Une seule lecture à la fois. */
  reloadAccount(): Promise<void> {
    this.reloadingAccount ??= (async () => {
      try {
        const account = await authApi.getCurrentAccount(this.http);
        if (this.accessToken) this.authenticate(account);
      } catch (error) {
        if (isApiError(error) && error.status === 401) this.end('expired');
        // Autres erreurs (réseau…) : on garde le compte déjà connu.
      } finally {
        this.reloadingAccount = null;
      }
    })();
    return this.reloadingAccount;
  }

  // --- Interne ---

  private async enter(tokens: AuthResponse): Promise<void> {
    this.storeToken(tokens);
    if (tokens.mustChangePassword) {
      this.setState({ status: 'mustChangePassword' });
      return;
    }
    const account = await authApi.getCurrentAccount(this.http);
    this.authenticate(account);
  }

  /** Compte lu au serveur : affiché, et gardé pour la lecture hors ligne avec l'heure de ce contact. */
  private authenticate(account: CurrentAccount): void {
    this.setState({ status: 'authenticated', account });
    this.remember(account);
  }

  private remember(account: CurrentAccount): void {
    const profile: OfflineProfile<CurrentAccount> = { account, lastContactAt: new Date().toISOString() };
    void this.offline?.set(PROFILE_KEY, profile);
  }

  private refreshSerialized(): Promise<boolean> {
    this.refreshing ??= (async () => {
      try {
        const tokens = await this.withRefreshLock(() => authApi.refresh(this.anonymous));
        this.storeToken(tokens);
        // Le serveur a répondu : le délai de 12 h de la lecture hors ligne repart de maintenant.
        if (this.state.status === 'authenticated') this.remember(this.state.account);
        return true;
      } catch (error) {
        // Réseau coupé : la session n'est pas forcément perdue, la requête échoue simplement.
        if (isApiError(error) && error.isNetworkError) return false;
        this.end('expired');
        return false;
      } finally {
        this.refreshing = null;
      }
    })();
    return this.refreshing;
  }

  private withRefreshLock<T>(work: () => Promise<T>): Promise<T> {
    const locks = typeof navigator === 'undefined' ? undefined : navigator.locks;
    return locks ? locks.request(REFRESH_LOCK, work) : work();
  }

  private storeToken(tokens: AuthResponse): void {
    this.accessToken = tokens.accessToken;
    this.scheduleRefresh(tokens.accessTokenExpiresAt);
  }

  // L'horloge du téléphone peut être fausse : si le rafraîchissement anticipé tombe trop tard,
  // le 401 suivi d'un rafraîchissement prend le relais.
  private scheduleRefresh(expiresAt: string): void {
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
    const expiry = Date.parse(expiresAt);
    if (Number.isNaN(expiry)) return;
    const delay = Math.max(MIN_REFRESH_DELAY_MS, expiry - Date.now() - REFRESH_AHEAD_MS);
    this.refreshTimer = setTimeout(() => {
      this.refreshTimer = null;
      void this.refreshSerialized();
    }, delay);
  }

  private clearToken(): void {
    this.accessToken = null;
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
    this.refreshTimer = null;
  }

  private end(reason: SignOutReason | null): void {
    this.clearToken();
    // Plus rien du compte ne reste sur le téléphone (profil et données gardées).
    void this.offline?.clear();
    this.setState({ status: 'anonymous', reason });
  }
}
