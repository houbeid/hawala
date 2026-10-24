import { http, HttpResponse } from 'msw';
import { apiUrl, server } from './server';

/** Export Excel d'une liste : garde les paramètres reçus et renvoie un petit fichier. */
export function exportServer(path: string) {
  const queries: Record<string, string>[] = [];
  server.use(
    http.get(apiUrl(path), ({ request }) => {
      queries.push(Object.fromEntries(new URL(request.url).searchParams));
      return new HttpResponse(new Uint8Array([0x50, 0x4b]), {
        headers: { 'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
      });
    }),
  );
  return queries;
}
