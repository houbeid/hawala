import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import '@/i18n';
import { server } from './server';

// Les écrans chargés à la demande (lazyRouteComponent) peuvent dépasser 1 s quand toute la suite tourne
// en parallèle ; le Budget et le Bilan chargent en plus Recharts (courbes) : on laisse 5 s aux findBy / waitFor.
configure({ asyncUtilTimeout: 5000 });

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));

afterEach(() => {
  cleanup();
  server.resetHandlers();
  window.localStorage.clear();
});

afterAll(() => server.close());
