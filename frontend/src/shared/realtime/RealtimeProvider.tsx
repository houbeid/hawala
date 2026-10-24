import { useEffect, useRef, useState, type ReactNode } from 'react';
import type { SessionManager } from '../auth/session';
import {
  REALTIME_MESSAGES,
  type RatesChangedMessage,
  type RealtimeConnection,
  type RealtimeConnectionFactory,
  type RealtimeStatus,
  type TransferChangedMessage,
} from './connection';
import { RealtimeStatusContext } from './realtimeStatus';

// Connexion temps réel liée à la session : ouverte quand le compte est connecté, fermée à la déconnexion (jamais de
// connexion anonyme). « SignedOut » (compte bloqué, mot de passe réinitialisé) et « SessionClosed » (session fermée
// depuis un autre appareil) renvoient à l'écran de connexion avec le motif : l'API refuse de toute façon ces
// sessions, le message rend l'effet visible tout de suite. Les autres messages sont confiés à l'application (handlers),
// qui relit ses données : shared/ ne connaît aucune fonctionnalité.
// Si la première connexion échoue (réseau, serveur redémarré), un nouvel essai part après RETRY_MS.

export interface RealtimeHandlers {
  onTransferChanged: (message: TransferChangedMessage) => void;
  onRatesChanged: (message: RatesChangedMessage) => void;
}

const RETRY_MS = 15_000;

interface Props {
  session: SessionManager;
  createConnection: RealtimeConnectionFactory;
  handlers: RealtimeHandlers;
  children: ReactNode;
}

export function RealtimeProvider({ session, createConnection, handlers, children }: Props) {
  const [status, setStatus] = useState<RealtimeStatus>('off');
  const [authenticated, setAuthenticated] = useState(session.getState().status === 'authenticated');
  // Les gestionnaires peuvent changer d'un rendu à l'autre : la connexion lit toujours les derniers.
  const handlersRef = useRef(handlers);
  useEffect(() => {
    handlersRef.current = handlers;
  }, [handlers]);

  useEffect(() => session.subscribe(() => setAuthenticated(session.getState().status === 'authenticated')), [session]);

  useEffect(() => {
    if (!authenticated) return;
    let stopped = false;
    let retry: ReturnType<typeof setTimeout> | null = null;
    const connection: RealtimeConnection = createConnection(() => session.getAccessToken());
    connection.on(REALTIME_MESSAGES.transferChanged, (payload) => handlersRef.current.onTransferChanged(payload as TransferChangedMessage));
    connection.on(REALTIME_MESSAGES.ratesChanged, (payload) => handlersRef.current.onRatesChanged(payload as RatesChangedMessage));
    connection.on(REALTIME_MESSAGES.signedOut, () => session.signOut('signedOut'));
    connection.on(REALTIME_MESSAGES.sessionClosed, () => session.signOut('sessionClosed'));
    connection.onStatus((next) => {
      if (stopped) return;
      setStatus(next);
      if (next === 'disconnected') schedule();
    });

    function schedule() {
      if (stopped || retry) return;
      retry = setTimeout(() => {
        retry = null;
        void connect();
      }, RETRY_MS);
    }

    async function connect() {
      if (stopped) return;
      setStatus('connecting');
      try {
        await connection.start();
        if (!stopped) setStatus('connected');
      } catch {
        if (stopped) return;
        setStatus('disconnected');
        schedule();
      }
    }

    void connect();
    return () => {
      stopped = true;
      if (retry) clearTimeout(retry);
      void connection.stop();
    };
  }, [authenticated, createConnection, session]);

  // Sans session, pas de connexion : l'état se déduit, sans être écrit dans un effet.
  return <RealtimeStatusContext.Provider value={authenticated ? status : 'off'}>{children}</RealtimeStatusContext.Provider>;
}
