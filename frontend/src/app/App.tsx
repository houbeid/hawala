import { QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { useEffect, useMemo, useState } from 'react';
import { SessionProvider } from '@/shared/auth/SessionProvider';
import { accountOf, type SessionManager } from '@/shared/auth/session';
import { createMemoryStore, type KeyValueStore } from '@/shared/offline/offlineStore';
import type { OperationQueue } from '@/shared/offline/operationQueue';
import { OperationQueueContext } from '@/shared/offline/queueContext';
import { createSignalRConnection, type RealtimeConnectionFactory } from '@/shared/realtime/connection';
import { createServiceWorkerUpdates, type AppUpdates } from '@/shared/pwa/appUpdates';
import { UpdateNotice } from '@/shared/pwa/UpdateNotice';
import { RealtimeProvider } from '@/shared/realtime/RealtimeProvider';
import { listAgencies } from '@/features/organization/api';
import { prefetchOfflineData, startOfflineCache } from './offlineData';
import { createOperationQueue } from './offlineQueue';
import { createQueryClient } from './queryClient';
import { createRealtimeHandlers } from './realtimeHandlers';
import type { AppRouter } from './router';

interface Props {
  session: SessionManager;
  router: AppRouter;
  /** Connexion temps réel ; un faux hub dans les tests. Par défaut : SignalR vers /hubs/live. */
  realtime?: RealtimeConnectionFactory;
  /** Mises à jour de l'app installée ; un faux service worker dans les tests. */
  updates?: AppUpdates;
  /** Stockage du téléphone pour la lecture hors ligne (le même que celui de la session) ; absent : rien n'est gardé. */
  offlineStore?: KeyValueStore;
  /** Stockage de la file des créations hors ligne (base à part, jamais effacée par une déconnexion) ; mémoire par défaut. */
  queueStore?: KeyValueStore;
}

/** En lecture hors ligne, nouvel essai de reprise de session à ce rythme (en plus du retour du réseau). */
const RECONNECT_EVERY_MS = 30_000;

/** Racine de l'application : la session (reprise au montage), le cache des données, le temps réel, le routeur. */
export function App({ session, router, realtime = createSignalRConnection, updates: givenUpdates, offlineStore, queueStore }: Props) {
  const [queryClient] = useState(createQueryClient);
  const [queue] = useState(() => createOperationQueue(queueStore ?? createMemoryStore(), queryClient));
  const [updates] = useState(() => givenUpdates ?? createServiceWorkerUpdates());
  const handlers = useMemo(() => createRealtimeHandlers(queryClient), [queryClient]);

  // À la fin d'une session, aucune donnée du compte précédent ne doit rester en mémoire.
  useEffect(
    () =>
      session.subscribe(() => {
        if (session.getState().status === 'anonymous') queryClient.clear();
      }),
    [session, queryClient],
  );

  useOfflineReading(session, queryClient, offlineStore);
  useQueueSending(session, queue);

  return (
    <SessionProvider session={session}>
      <OperationQueueContext.Provider value={queue}>
        <QueryClientProvider client={queryClient}>
          <RealtimeProvider session={session} createConnection={realtime} handlers={handlers}>
            <RouterProvider router={router} />
            <UpdateNotice updates={updates} />
          </RealtimeProvider>
        </QueryClientProvider>
      </OperationQueueContext.Provider>
    </SessionProvider>
  );
}

/**
 * Lecture hors ligne : cache gardé et rechargé pour le compte (seulement s'il y a un stockage), préchargement à chaque
 * session ouverte au serveur, et, hors ligne, nouvel essai de reprise au retour du réseau et toutes les 30 s.
 */
function useOfflineReading(session: SessionManager, queryClient: ReturnType<typeof createQueryClient>, store: KeyValueStore | undefined) {
  const [state, setState] = useState(session.getState);
  useEffect(() => session.subscribe(() => setState(session.getState())), [session]);
  const accountId = accountOf(state)?.id;
  const status = state.status;

  useEffect(() => {
    if (!store || !accountId) return;
    return startOfflineCache(queryClient, store, accountId, () => {
      const current = session.getState();
      return current.status === 'authenticated' && current.account.id === accountId;
    });
  }, [store, accountId, queryClient, session]);

  useEffect(() => {
    if (!store || status !== 'authenticated') return;
    const current = session.getState();
    if (current.status !== 'authenticated') return;
    void (async () => {
      const agencies = await queryClient.fetchQuery({ queryKey: ['organization', 'agencies'], queryFn: () => listAgencies(session.http) }).catch(() => []);
      await prefetchOfflineData(queryClient, session.http, current.account, agencies.map((a) => a.id));
    })();
  }, [store, status, queryClient, session]);

  useEffect(() => {
    if (status !== 'offline') return;
    const retry = () => void session.reconnect();
    window.addEventListener('online', retry);
    const timer = setInterval(retry, RECONNECT_EVERY_MS);
    return () => {
      window.removeEventListener('online', retry);
      clearInterval(timer);
    };
  }, [status, session]);
}

/** En attente d'un nouvel essai d'envoi de la file (en plus du retour du réseau et de chaque nouvelle saisie). */
const SEND_EVERY_MS = 30_000;

/**
 * Envoi de la file des créations hors ligne : dès que la session est ouverte au serveur, au retour du réseau, après
 * chaque nouvelle saisie et toutes les 30 s. Seules les opérations du compte connecté partent.
 */
function useQueueSending(session: SessionManager, queue: OperationQueue) {
  const [state, setState] = useState(session.getState);
  useEffect(() => session.subscribe(() => setState(session.getState())), [session]);
  const accountId = state.status === 'authenticated' ? state.account.id : null;

  useEffect(() => {
    void queue.load();
  }, [queue]);

  useEffect(() => {
    if (!accountId) return;
    const send = () => {
      if (typeof navigator !== 'undefined' && navigator.onLine === false) return;
      if (queue.forAccount(accountId).some((op) => op.status === 'pending')) void queue.flush(session.http, accountId);
    };
    send();
    const unsubscribe = queue.subscribe(send);
    window.addEventListener('online', send);
    const timer = setInterval(send, SEND_EVERY_MS);
    return () => {
      unsubscribe();
      window.removeEventListener('online', send);
      clearInterval(timer);
    };
  }, [accountId, queue, session]);
}
