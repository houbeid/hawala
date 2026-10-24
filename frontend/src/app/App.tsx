import { QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { useEffect, useMemo, useState } from 'react';
import { SessionProvider } from '@/shared/auth/SessionProvider';
import type { SessionManager } from '@/shared/auth/session';
import { createSignalRConnection, type RealtimeConnectionFactory } from '@/shared/realtime/connection';
import { RealtimeProvider } from '@/shared/realtime/RealtimeProvider';
import { createQueryClient } from './queryClient';
import { createRealtimeHandlers } from './realtimeHandlers';
import type { AppRouter } from './router';

interface Props {
  session: SessionManager;
  router: AppRouter;
  /** Connexion temps réel ; un faux hub dans les tests. Par défaut : SignalR vers /hubs/live. */
  realtime?: RealtimeConnectionFactory;
}

/** Racine de l'application : la session (reprise au montage), le cache des données, le temps réel, le routeur. */
export function App({ session, router, realtime = createSignalRConnection }: Props) {
  const [queryClient] = useState(createQueryClient);
  const handlers = useMemo(() => createRealtimeHandlers(queryClient), [queryClient]);

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
        <RealtimeProvider session={session} createConnection={realtime} handlers={handlers}>
          <RouterProvider router={router} />
        </RealtimeProvider>
      </QueryClientProvider>
    </SessionProvider>
  );
}
