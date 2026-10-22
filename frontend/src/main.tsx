import '@fontsource/ibm-plex-sans/400.css';
import '@fontsource/ibm-plex-sans/500.css';
import '@fontsource/ibm-plex-sans/600.css';
import '@fontsource/ibm-plex-sans/700.css';
import '@fontsource/ibm-plex-sans-arabic/400.css';
import '@fontsource/ibm-plex-sans-arabic/500.css';
import '@fontsource/ibm-plex-sans-arabic/600.css';
import '@fontsource/ibm-plex-sans-arabic/700.css';
import '@fontsource/ibm-plex-mono/500.css';
import '@fontsource/ibm-plex-mono/600.css';
import './app/styles.css';
import './i18n';

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app/App';
import { createAppRouter } from './app/router';
import { SessionManager } from './shared/auth/session';
import { applyTheme, readThemePreference } from './shared/theme/theme';

// Thème posé avant le premier rendu, pour éviter un flash clair en mode sombre.
applyTheme(readThemePreference());

// Une seule session et un seul routeur pour toute la durée de la page.
const session = new SessionManager();
const router = createAppRouter();

const root = document.getElementById('root');
if (!root) throw new Error('Élément #root absent de index.html');

createRoot(root).render(
  <StrictMode>
    <App session={session} router={router} />
  </StrictMode>,
);
