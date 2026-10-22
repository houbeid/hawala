import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import '@/i18n';
import { server } from './server';

// Les écrans chargés à la demande (lazyRouteComponent) peuvent dépasser 1 s quand toute la suite tourne
// en parallèle : on laisse 3 s aux findBy / waitFor avant d'échouer.
configure({ asyncUtilTimeout: 3000 });

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));

afterEach(() => {
  cleanup();
  server.resetHandlers();
  window.localStorage.clear();
});

afterAll(() => server.close());
