import { isApiError } from '@/shared/api/errors';
import { createHttpClient, type AccessTokenSource, type HttpClient } from '@/shared/api/http';
import * as authApi from './authApi';
import type { AuthResponse, ChangePasswordRequest, CurrentAccount, LoginRequest } from './types';

// Session du compte connecté, sans React (testable seule). Règles décidées le 22 octobre 2026 :
// - le jeton d'accès vit en MÉMOIRE seulement (jamais localStorage : un script injecté ne peut pas le lire
//   après coup) ; au rechargement de la page, on repasse par /auth/refresh grâce au cookie HttpOnly ;
// - un seul rafraîchissement à la fois : les requêtes en 401 partagent la même promesse, et un verrou
//   Web Locks sérialise aussi entre onglets (le backend accepte deux rafraîchissements simultanés,
//   limite connue : on évite de la déclencher).

export type SignOutReason = 'expired' | 'signedOut' | 'sessionClosed';

export type SessionState =
  | { status: 'loading' }
  | { status: 'anonymous'; reason: SignOutReason | null }
  /** Serveur injoignable au démarrage : la session existe peut-être encore, l'interface propose de réessayer. */
  | { status: 'unreachable' }
  /** Mot de passe temporaire : seul le changement de mot de passe est permis (GET /auth/me répond 403). */
  | { status: 'mustChangePassword' }
  | { status: 'authenticated'; account: CurrentAccount };

type Listener = () => void;

/** Marge avant l'expiration du jeton pour le rafraîchir sans attendre un 401. */
const REFRESH_AHEAD_MS = 60_000;
const MIN_REFRESH_DELAY_MS = 5_000;
const REFRESH_LOCK = 'hawala.auth.refresh';

export interface SessionManagerOptions {
  baseUrl?: string;
  /** Remplacé dans les tests (MSW intercepte le fetch global, ce n'est donc pas nécessaire en général). */
  fetch?: typeof fetch;
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

  constructor(options: SessionManagerOptions = {}) {
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
        this.setState({ status: 'unreachable' });
      } else {
        this.clearToken();
        this.setState({ status: 'anonymous', reason: null });
      }
    }
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
        if (this.accessToken) this.setState({ status: 'authenticated', account });
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
    this.setState({ status: 'authenticated', account });
  }

  private refreshSerialized(): Promise<boolean> {
    this.refreshing ??= (async () => {
      try {
        const tokens = await this.withRefreshLock(() => authApi.refresh(this.anonymous));
        this.storeToken(tokens);
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
    this.setState({ status: 'anonymous', reason });
  }
}
