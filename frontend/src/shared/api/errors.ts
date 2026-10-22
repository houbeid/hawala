// Erreurs renvoyées par l'API au format ProblemDetails (RFC 9457), voir backend
// Hawala.Api/Errors/ErrorResults.cs : { type, title, status, detail, code, ...champs ajoutés }.
// Le frontend s'appuie sur « code » (stable, ex. « Transfers.Credit.LimitExceeded »), jamais sur « detail »
// (texte français du serveur, non traduit).

/** Champs standard d'un ProblemDetails : tout le reste est rangé dans `details`. */
const STANDARD_FIELDS = new Set(['type', 'title', 'status', 'detail', 'instance', 'code', 'traceId']);

/** Codes produits par le client lui-même (pas par le serveur). */
export const ClientErrorCodes = {
  /** Réseau coupé ou serveur injoignable : aucune réponse HTTP. */
  Network: 'Client.Network',
  /** Réponse sans code exploitable (401 vide d'un jeton refusé, 500 du serveur…). */
  Unknown: 'Client.Unknown',
} as const;

export const RATE_LIMITED_CODE = 'Api.RateLimited';

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  /** Champs ajoutés par le serveur (remaining, currency, remainingAttempts…). */
  readonly details: Readonly<Record<string, unknown>>;
  /** Pour un 429 : secondes à attendre, lues dans l'en-tête Retry-After. */
  readonly retryAfterSeconds: number | null;

  constructor(init: {
    status: number;
    code: string;
    message?: string;
    details?: Record<string, unknown>;
    retryAfterSeconds?: number | null;
  }) {
    super(init.message ?? init.code);
    this.name = 'ApiError';
    this.status = init.status;
    this.code = init.code;
    this.details = init.details ?? {};
    this.retryAfterSeconds = init.retryAfterSeconds ?? null;
  }

  get isNetworkError(): boolean {
    return this.code === ClientErrorCodes.Network;
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

function readRetryAfter(response: Response): number | null {
  const header = response.headers.get('Retry-After');
  if (!header) return null;
  const seconds = Number(header);
  if (Number.isFinite(seconds) && seconds >= 0) return Math.ceil(seconds);
  const date = Date.parse(header);
  return Number.isNaN(date) ? null : Math.max(0, Math.ceil((date - Date.now()) / 1000));
}

/** Construit une ApiError à partir d'une réponse en échec (corps ProblemDetails ou vide). */
export async function apiErrorFromResponse(response: Response): Promise<ApiError> {
  let body: unknown = null;
  const text = await response.text().catch(() => '');
  if (text) {
    try {
      body = JSON.parse(text);
    } catch {
      body = null;
    }
  }

  const problem = typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : {};
  const code = typeof problem.code === 'string' && problem.code ? problem.code : ClientErrorCodes.Unknown;
  const details: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(problem)) {
    if (!STANDARD_FIELDS.has(key)) details[key] = value;
  }

  return new ApiError({
    status: response.status,
    code,
    message: typeof problem.detail === 'string' ? problem.detail : `HTTP ${response.status}`,
    details,
    retryAfterSeconds: response.status === 429 ? readRetryAfter(response) : null,
  });
}

export function networkError(cause: unknown): ApiError {
  const error = new ApiError({ status: 0, code: ClientErrorCodes.Network, message: 'Réseau indisponible' });
  (error as { cause?: unknown }).cause = cause;
  return error;
}
