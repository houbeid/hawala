/// <reference types="vite-plugin-pwa/client" />

/** Version de l'application (package.json), posée au build par vite.config.ts. */
declare const __APP_VERSION__: string;

interface ImportMetaEnv {
  /** Adresse directe du hub temps réel quand l'interface n'est pas servie par la même origine que l'API (Vercel). */
  readonly VITE_HUB_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
