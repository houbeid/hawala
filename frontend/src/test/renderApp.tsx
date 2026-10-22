import { createMemoryHistory } from '@tanstack/react-router';
import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { App } from '@/app/App';
import { createAppRouter } from '@/app/router';
import { SessionManager } from '@/shared/auth/session';
import type { CurrentAccount } from '@/shared/auth/types';
import { dkr, nkc, tokens } from './fixtures';
import { apiUrl, server } from './server';

/** Toute l'application, à une adresse donnée (historique en mémoire). */
export function renderApp(path = '/') {
  const session = new SessionManager();
  const router = createAppRouter(createMemoryHistory({ initialEntries: [path] }));
  const user = userEvent.setup();
  render(<App session={session} router={router} />);
  return { session, router, user };
}

/** Session déjà ouverte (cookie valable) pour ce compte, et les deux agences. */
export function signedInAs(account: CurrentAccount) {
  server.use(
    http.post(apiUrl('/auth/refresh'), () => HttpResponse.json(tokens('t1'))),
    http.get(apiUrl('/auth/me'), () => HttpResponse.json(account)),
    http.get(apiUrl('/agencies'), () => HttpResponse.json([nkc, dkr])),
  );
}
