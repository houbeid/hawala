import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import { readPreference, writePreference } from '@/shared/storage';
import ar from './locales/ar.json';
import fr from './locales/fr.json';

export const LANGUAGES = ['fr', 'ar'] as const;
export type Language = (typeof LANGUAGES)[number];

const STORAGE_KEY = 'hawala.language';

export function isLanguage(value: unknown): value is Language {
  return typeof value === 'string' && (LANGUAGES as readonly string[]).includes(value);
}

export function directionOf(language: Language): 'ltr' | 'rtl' {
  return language === 'ar' ? 'rtl' : 'ltr';
}

/** Pose la langue et le sens d'écriture sur <html> : tout le CSS logique en dépend. */
export function applyDocumentLanguage(language: Language): void {
  const root = document.documentElement;
  root.lang = language;
  root.dir = directionOf(language);
}

function initialLanguage(): Language {
  const saved = readPreference(STORAGE_KEY);
  return isLanguage(saved) ? saved : 'fr';
}

void i18n.use(initReactI18next).init({
  resources: { fr: { translation: fr }, ar: { translation: ar } },
  lng: initialLanguage(),
  fallbackLng: 'fr',
  supportedLngs: LANGUAGES,
  interpolation: { escapeValue: false },
  returnNull: false,
});

applyDocumentLanguage(i18n.language as Language);

i18n.on('languageChanged', (language) => {
  if (!isLanguage(language)) return;
  applyDocumentLanguage(language);
  writePreference(STORAGE_KEY, language);
});

export default i18n;
