import { apiErrorFromResponse, networkError } from './errors';

// Seul endroit qui appelle fetch : en-têtes, JSON, erreurs et rejeu après rafraîchissement du jeton.
// Les fonctionnalités n'appellent jamais fetch directement, elles passent par un HttpClient.

export const API_BASE = '/api/v1';

/** Ce dont le client a besoin de la session, sans en dépendre (la session est dans shared/auth). */
export interface AccessTokenSource {
  getAccessToken(): string | null;
  /**
   * Obtient un nouveau jeton (rafraîchissement sérialisé par la session).
   * Renvoie false si la session est perdue : la requête n'est alors pas rejouée.
   */
  refreshAccessToken(): Promise<boolean>;
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  /** Corps envoyé en JSON. */
  body?: unknown;
  /** Paramètres d'URL ; null et undefined sont omis. */
  query?: Record<string, string | number | boolean | null | undefined>;
  headers?: Record<string, string>;
  signal?: AbortSignal;
}

export interface HttpClient {
  request<T>(path: string, options?: RequestOptions): Promise<T>;
  get<T>(path: string, options?: Omit<RequestOptions, 'method' | 'body'>): Promise<T>;
  post<T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>): Promise<T>;
  put<T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>): Promise<T>;
  /** Réponse binaire (PDF, Excel) : mêmes en-têtes, erreurs et rejeu que les autres appels. */
  blob(path: string, options?: RequestOptions): Promise<Blob>;
}

export interface HttpClientOptions {
  /** Absent : client anonyme (connexion, rafraîchissement), sans en-tête Authorization ni rejeu. */
  auth?: AccessTokenSource;
  baseUrl?: string;
  fetch?: typeof fetch;
}

export function buildUrl(baseUrl: string, path: string, query?: RequestOptions['query']): string {
  const url = `${baseUrl}${path.startsWith('/') ? path : `/${path}`}`;
  if (!query) return url;
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== null && value !== undefined) params.append(key, String(value));
  }
  const search = params.toString();
  return search ? `${url}?${search}` : url;
}

export function createHttpClient(options: HttpClientOptions = {}): HttpClient {
  const baseUrl = options.baseUrl ?? API_BASE;
  // Lu à chaque appel (pas capturé) : les tests remplacent fetch par celui de MSW.
  const doFetch = (input: string, init: RequestInit) => (options.fetch ?? globalThis.fetch)(input, init);

  async function send(path: string, request: RequestOptions, token: string | null): Promise<Response> {
    const headers: Record<string, string> = { Accept: 'application/json', ...request.headers };
    if (request.body !== undefined) headers['Content-Type'] = 'application/json';
    if (token) headers.Authorization = `Bearer ${token}`;

    // URL absolue résolue sur la page : identique dans le navigateur, et nécessaire au fetch de Node (tests).
    const url = new URL(buildUrl(baseUrl, path, request.query), globalThis.location.href).toString();
    try {
      return await doFetch(url, {
        method: request.method ?? 'GET',
        headers,
        body: request.body === undefined ? undefined : JSON.stringify(request.body),
        // Même origine : le cookie hawala_refresh part vers /api/v1/auth, jamais ailleurs (chemin du cookie).
        credentials: 'same-origin',
        signal: request.signal,
      });
    } catch (cause) {
      if (request.signal?.aborted) throw cause;
      throw networkError(cause);
    }
  }

  async function fetchOk(path: string, req: RequestOptions): Promise<Response> {
    const auth = options.auth;
    let response = await send(path, req, auth?.getAccessToken() ?? null);

    // Jeton expiré ou session revalidée : un seul rafraîchissement, puis un seul rejeu.
    if (response.status === 401 && auth) {
      const refreshed = await auth.refreshAccessToken();
      if (refreshed) response = await send(path, req, auth.getAccessToken());
    }

    if (!response.ok) throw await apiErrorFromResponse(response);
    return response;
  }

  async function request<T>(path: string, req: RequestOptions = {}): Promise<T> {
    const response = await fetchOk(path, req);
    if (response.status === 204) return undefined as T;

    const text = await response.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }

  return {
    request,
    get: (path, opts) => request(path, { ...opts, method: 'GET' }),
    post: (path, body, opts) => request(path, { ...opts, method: 'POST', body }),
    put: (path, body, opts) => request(path, { ...opts, method: 'PUT', body }),
    blob: async (path, opts = {}) => (await fetchOk(path, { ...opts, headers: { Accept: '*/*', ...opts.headers } })).blob(),
  };
}
