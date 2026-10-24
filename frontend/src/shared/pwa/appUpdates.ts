// Mises à jour de l'application installée (PWA, phase 7). Le service worker garde les fichiers de l'app ; quand une
// nouvelle version est publiée, il la télécharge puis attend : l'app le dit à l'agent, qui choisit le moment
// (jamais de rechargement imposé au milieu d'une saisie). Le jeton n'étant qu'en mémoire, la session reprend
// après le rechargement par le cookie de rafraîchissement.

export interface AppUpdates {
  /** Enregistre le service worker ; `onNeedRefresh` est appelé quand une nouvelle version attend. */
  start(onNeedRefresh: () => void): void;
  /** Active la nouvelle version et recharge la page. */
  apply(): Promise<void>;
}

/** Vérifie s'il existe une nouvelle version toutes les heures (une app ouverte toute la journée la verra). */
const CHECK_EVERY_MS = 60 * 60 * 1000;

/** Vrai service worker (vite-plugin-pwa). Sans service worker (vieux navigateur) : rien, l'app marche en ligne. */
export function createServiceWorkerUpdates(): AppUpdates {
  let update: ((reloadPage?: boolean) => Promise<void>) | null = null;
  let started = false;
  return {
    start(onNeedRefresh) {
      // Une seule inscription, même si l'écran est monté deux fois (StrictMode en dev).
      if (started || !('serviceWorker' in navigator)) return;
      started = true;
      void import('virtual:pwa-register').then(({ registerSW }) => {
        update = registerSW({
          onNeedRefresh,
          onRegisteredSW(_url, registration) {
            if (registration) setInterval(() => void registration.update(), CHECK_EVERY_MS);
          },
        });
      });
    },
    apply: async () => {
      await update?.(true);
    },
  };
}
