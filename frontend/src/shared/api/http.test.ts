import { http, HttpResponse } from 'msw';
import { apiUrl, server } from '@/test/server';
import { ApiError, ClientErrorCodes } from './errors';
import { buildUrl, createHttpClient, type AccessTokenSource } from './http';

function tokenSource(initial: string | null, next: string | null = null): AccessTokenSource & { refreshes: number } {
  let token = initial;
  return {
    refreshes: 0,
    getAccessToken: () => token,
    async refreshAccessToken() {
      this.refreshes += 1;
      token = next;
      return next !== null;
    },
  };
}

describe('buildUrl', () => {
  it('omet les paramètres vides et encode les autres', () => {
    expect(buildUrl('/api/v1', '/transfers', { status: 'NotDelivered', agencyId: null, search: 'a b', page: 2 })).toBe(
      '/api/v1/transfers?status=NotDelivered&search=a+b&page=2',
    );
  });
});

describe('createHttpClient', () => {
  it('envoie le jeton, le corps en JSON et lit la réponse', async () => {
    let seen: { auth: string | null; type: string | null; body: unknown } | null = null;
    server.use(
      http.post(apiUrl('/things'), async ({ request }) => {
        seen = {
          auth: request.headers.get('Authorization'),
          type: request.headers.get('Content-Type'),
          body: await request.json(),
        };
        return HttpResponse.json({ id: 'x1' });
      }),
    );

    const client = createHttpClient({ auth: tokenSource('jeton-1') });
    const result = await client.post<{ id: string }>('/things', { name: 'test' });

    expect(result).toEqual({ id: 'x1' });
    expect(seen).toEqual({ auth: 'Bearer jeton-1', type: 'application/json', body: { name: 'test' } });
  });

  it("n'envoie pas d'en-tête Authorization sans source de jeton", async () => {
    let auth: string | null = 'non lu';
    server.use(
      http.get(apiUrl('/public'), ({ request }) => {
        auth = request.headers.get('Authorization');
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await expect(createHttpClient().get('/public')).resolves.toBeUndefined();
    expect(auth).toBeNull();
  });

  it('rafraîchit une fois sur 401 puis rejoue la requête avec le nouveau jeton', async () => {
    const tokensSeen: (string | null)[] = [];
    server.use(
      http.get(apiUrl('/me-thing'), ({ request }) => {
        const auth = request.headers.get('Authorization');
        tokensSeen.push(auth);
        return auth === 'Bearer neuf' ? HttpResponse.json({ ok: true }) : new HttpResponse(null, { status: 401 });
      }),
    );
    const source = tokenSource('vieux', 'neuf');

    await expect(createHttpClient({ auth: source }).get('/me-thing')).resolves.toEqual({ ok: true });
    expect(source.refreshes).toBe(1);
    expect(tokensSeen).toEqual(['Bearer vieux', 'Bearer neuf']);
  });

  it('ne rejoue pas si le rafraîchissement échoue', async () => {
    let calls = 0;
    server.use(
      http.get(apiUrl('/me-thing'), () => {
        calls += 1;
        return new HttpResponse(null, { status: 401 });
      }),
    );

    const error = await createHttpClient({ auth: tokenSource('vieux', null) })
      .get('/me-thing')
      .catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(401);
    expect(calls).toBe(1);
  });

  it('transforme un ProblemDetails en ApiError avec son code et ses champs ajoutés', async () => {
    server.use(
      http.post(apiUrl('/transfers'), () =>
        HttpResponse.json(
          {
            type: 'https://tools.ietf.org/html/rfc9110#section-15.5.10',
            title: 'Transfers.Limits.PerDayExceeded',
            status: 409,
            detail: 'Plafond par jour dépassé.',
            code: 'Transfers.Limits.PerDayExceeded',
            remaining: 125000,
            currency: 'MRO',
            traceId: '00-abc',
          },
          { status: 409 },
        ),
      ),
    );

    const error = (await createHttpClient().post('/transfers', {}).catch((e: unknown) => e)) as ApiError;

    expect(error.status).toBe(409);
    expect(error.code).toBe('Transfers.Limits.PerDayExceeded');
    expect(error.details).toEqual({ remaining: 125000, currency: 'MRO' });
  });

  it('lit Retry-After sur un 429', async () => {
    server.use(
      http.post(apiUrl('/auth/login'), () =>
        HttpResponse.json(
          { title: 'Api.RateLimited', status: 429, code: 'Api.RateLimited' },
          { status: 429, headers: { 'Retry-After': '42' } },
        ),
      ),
    );

    const error = (await createHttpClient().post('/auth/login', {}).catch((e: unknown) => e)) as ApiError;

    expect(error.code).toBe('Api.RateLimited');
    expect(error.retryAfterSeconds).toBe(42);
  });

  it('donne un code client quand la réponse est vide ou le réseau coupé', async () => {
    server.use(
      http.get(apiUrl('/boom'), () => new HttpResponse(null, { status: 500 })),
      http.get(apiUrl('/offline'), () => HttpResponse.error()),
    );
    const client = createHttpClient();

    const boom = (await client.get('/boom').catch((e: unknown) => e)) as ApiError;
    const offline = (await client.get('/offline').catch((e: unknown) => e)) as ApiError;

    expect(boom.code).toBe(ClientErrorCodes.Unknown);
    expect(boom.status).toBe(500);
    expect(offline.code).toBe(ClientErrorCodes.Network);
    expect(offline.isNetworkError).toBe(true);
  });
});
