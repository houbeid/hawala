import { setupServer } from 'msw/node';

// Serveur MSW partagé : il intercepte les vraies requêtes fetch au niveau réseau.
// Chaque test déclare ses réponses avec server.use(...) ; une requête non prévue fait échouer le test.
export const server = setupServer();

/** URL absolue d'une route de l'API, telle que le client la construit dans jsdom. */
export function apiUrl(path: string): string {
  return new URL(`/api/v1${path}`, globalThis.location.href).toString();
}
