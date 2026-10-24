import type { HubConnection } from '@microsoft/signalr';

// Temps réel (hub /hubs/live, Hawala.Api/Realtime/LiveHub.cs ; règles validées le 21 et le 22 octobre 2026).
// Messages légers, SANS montant ni nom : l'application relit toujours les données par l'API, qui applique les règles
// de visibilité. Le serveur choisit les groupes (agence, Superviseurs, compte, session) à partir du jeton.
// Le jeton est passé dans « access_token » pour ce chemin seulement (exigence des WebSockets du navigateur).

/** Noms des messages (LiveHub.*Message). */
export const REALTIME_MESSAGES = {
  transferChanged: 'TransferChanged',
  ratesChanged: 'RatesChanged',
  sessionClosed: 'SessionClosed',
  signedOut: 'SignedOut',
} as const;

/** Copié sur Transfers.Application/Abstractions/ITransferChangePublisher.cs (TransferChanged). */
export interface TransferChangedMessage {
  transferId: string;
  reference: string;
  event: string;
  version: number;
  status: string;
  sourceAgencyId: string;
  destinationAgencyId: string;
}

/** Envoyé à tous quand un taux du jour change (RateEndpoints : { date }). */
export interface RatesChangedMessage {
  date: string;
}

export type RealtimeStatus = 'off' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

/** Ce dont l'application a besoin d'une connexion : de quoi la remplacer par un faux hub dans les tests. */
export interface RealtimeConnection {
  start(): Promise<void>;
  stop(): Promise<void>;
  on(message: string, handler: (payload: unknown) => void): void;
  /** Reconnexion en cours, rétablie ou perdue (après les essais automatiques). */
  onStatus(handler: (status: RealtimeStatus) => void): void;
}

export type RealtimeConnectionFactory = (accessToken: () => string | null) => RealtimeConnection;

/** Délais des reconnexions automatiques (ms) ; après le dernier, la connexion est « perdue » et l'app réessaie plus tard. */
const RECONNECT_DELAYS = [0, 2000, 5000, 10000, 30000];

/**
 * Vraie connexion SignalR vers /hubs/live (même origine : proxy Vite en dev, Caddy en prod). Le client SignalR
 * (~130 Ko) est chargé à la demande, au premier démarrage : l'écran de connexion n'en a pas besoin. Les abonnements
 * enregistrés avant sont posés sur le hub dès qu'il existe.
 */
export const createSignalRConnection: RealtimeConnectionFactory = (accessToken) => {
  const messages: [string, (payload: unknown) => void][] = [];
  const statusHandlers: ((status: RealtimeStatus) => void)[] = [];
  let hub: HubConnection | null = null;
  let stopped = false;

  async function build(): Promise<HubConnection> {
    if (hub) return hub;
    const signalR = await import('@microsoft/signalr');
    const built = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/live', { accessTokenFactory: () => accessToken() ?? '' })
      .withAutomaticReconnect(RECONNECT_DELAYS)
      .configureLogging(signalR.LogLevel.Warning)
      .build();
    for (const [message, handler] of messages) built.on(message, handler);
    built.onreconnecting(() => statusHandlers.forEach((h) => h('reconnecting')));
    built.onreconnected(() => statusHandlers.forEach((h) => h('connected')));
    built.onclose(() => statusHandlers.forEach((h) => h('disconnected')));
    hub = built;
    return built;
  }

  return {
    start: async () => {
      const built = await build();
      // Arrêtée pendant le chargement du client (déconnexion rapide) : ne pas ouvrir.
      if (!stopped) await built.start();
    },
    stop: async () => {
      stopped = true;
      if (hub && hub.state !== 'Disconnected') await hub.stop();
    },
    on: (message, handler) => {
      messages.push([message, handler]);
      hub?.on(message, handler);
    },
    onStatus: (handler) => statusHandlers.push(handler),
  };
};
