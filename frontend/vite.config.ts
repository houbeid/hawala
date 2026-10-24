/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url';
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import { VitePWA } from 'vite-plugin-pwa';
import packageJson from './package.json' with { type: 'json' };

// L'API n'a pas de CORS : en dev, tout passe par le serveur Vite (même origine),
// ce qui laisse aussi passer le cookie hawala_refresh (SameSite=Strict, chemin /api/v1/auth).
const apiTarget = process.env.HAWALA_API_URL ?? 'http://localhost:5215';

// PWA installable (phase 7, 24 octobre 2026). Le service worker ne garde QUE les fichiers de l'application (code,
// styles, polices latin et arabe, icônes) pour qu'elle s'ouvre sans réseau ; jamais les réponses de l'API ni du hub
// (données de l'agence, droits du compte) : la lecture hors ligne des données se fera à part, effacée à la déconnexion.
// Nouvelle version : l'app le propose (« prompt »), elle ne se recharge jamais d'elle-même au milieu d'une saisie.
const pwa = VitePWA({
  registerType: 'prompt',
  injectRegister: false,
  includeAssets: ['icons/apple-touch-icon.png', 'icons/favicon-64.png'],
  manifest: {
    id: '/',
    name: 'ETS EL-VETH',
    short_name: 'EL-VETH',
    description: 'Transferts Nouakchott ⇄ Dakar',
    lang: 'fr',
    dir: 'ltr',
    start_url: '/',
    scope: '/',
    display: 'standalone',
    orientation: 'portrait',
    background_color: '#F5F2EB',
    theme_color: '#161A22',
    icons: [
      { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png' },
      { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png' },
      { src: '/icons/icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
    ],
  },
  workbox: {
    globPatterns: ['**/*.{js,css,html,woff2,png,svg}'],
    // Polices : latin et arabe seulement (les autres alphabets ne servent pas ; le format .woff est l'ancien).
    globIgnores: ['**/*cyrillic*', '**/*greek*', '**/*vietnamese*', '**/*.woff'],
    navigateFallback: '/index.html',
    navigateFallbackDenylist: [/^\/api\//, /^\/hubs\//],
    cleanupOutdatedCaches: true,
  },
  devOptions: { enabled: false },
});

export default defineConfig({
  plugins: [react(), tailwindcss(), pwa],
  define: {
    // Version affichée dans l'app (« v0.1.0 »), lue dans package.json au build.
    __APP_VERSION__: JSON.stringify(packageJson.version),
  },
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: false },
      '/hubs': { target: apiTarget, changeOrigin: false, ws: true },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    // Les parcours complets (création d'un transfert : trois étapes de saisie simulée) dépassent 5 s
    // quand toute la suite tourne en parallèle.
    testTimeout: 15_000,
  },
});
