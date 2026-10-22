import { QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { SessionProvider } from '@/shared/auth/SessionProvider';
import type { SessionManager } from '@/shared/auth/session';
import { createQueryClient } from './queryClient';
import type { AppRouter } from './router';

/** Racine de l'application : la session (reprise au montage), le cache des données, le routeur et ses gardes. */
export function App({ session, router }: { session: SessionManager; router: AppRouter }) {
  const [queryClient] = useState(createQueryClient);

  // À la fin d'une session, aucune donnée du compte précédent ne doit rester en mémoire.
  useEffect(
    () =>
      session.subscribe(() => {
        if (session.getState().status === 'anonymous') queryClient.clear();
      }),
    [session, queryClient],
  );

  return (
    <SessionProvider session={session}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </SessionProvider>
  );
}
