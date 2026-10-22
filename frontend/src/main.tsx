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
import { applyTheme, readThemePreference } from './shared/theme/theme';

// Thème posé avant le premier rendu, pour éviter un flash clair en mode sombre.
applyTheme(readThemePreference());

const root = document.getElementById('root');
if (!root) throw new Error('Élément #root absent de index.html');

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
