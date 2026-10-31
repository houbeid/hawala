import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import '@/i18n';
import { server } from './server';

// Les écrans chargés à la demande (lazyRouteComponent) peuvent dépasser 1 s quand toute la suite tourne
// en parallèle ; le Budget et le Bilan chargent en plus Recharts (courbes), et dépassaient parfois 5 s sous charge :
// on laisse 10 s aux findBy / waitFor (une attente s'arrête dès que l'élément paraît ; le test garde ses 15 s).
configure({ asyncUtilTimeout: 10_000 });

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));

afterEach(() => {
  cleanup();
  server.resetHandlers();
  window.localStorage.clear();
});

afterAll(() => server.close());
