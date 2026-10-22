import { useTranslation } from 'react-i18next';
import { LANGUAGES, type Language } from '@/i18n';
import { formatRate } from '@/shared/format/money';
import { THEME_PREFERENCES } from '@/shared/theme/theme';
import { useTheme } from '@/shared/theme/useTheme';

// Page de démarrage du squelette : vérifie polices, thèmes et sens d'écriture.
// Elle sera remplacée par l'écran de connexion (tour 3).
export function App() {
  const { t, i18n } = useTranslation();
  const { preference, choose } = useTheme();

  return (
    <div className="flex min-h-dvh flex-col">
      <header className="flex min-h-16 items-center gap-2 border-b border-t-[5px] border-b-border border-t-agency bg-surface px-4">
        <h1 className="m-0 flex-1 text-xl font-semibold">{t('app.name')}</h1>
        <span className="text-sm text-muted">{t('app.corridor')}</span>
      </header>

      <main className="mx-auto flex w-full max-w-md flex-1 flex-col gap-4 px-4 py-4">
        <p className="m-0 text-muted">{t('app.skeleton')}</p>

        <section className="flex flex-col gap-2 rounded-2xl border border-border bg-surface p-4">
          <h2 className="m-0 text-sm font-semibold text-muted">{t('app.rateOfDay')}</h2>
          <span className="amount text-xl">{formatRate(3650)}</span>
        </section>

        <fieldset className="m-0 flex flex-wrap gap-2 border-0 p-0">
          <legend className="mb-2 text-sm font-semibold text-muted">{t('language.label')}</legend>
          {LANGUAGES.map((language: Language) => (
            <button
              key={language}
              type="button"
              lang={language}
              aria-pressed={i18n.language === language}
              onClick={() => void i18n.changeLanguage(language)}
              className="min-h-11 rounded-full border-[1.5px] border-border bg-surface px-4 font-semibold aria-pressed:border-ink aria-pressed:bg-ink aria-pressed:text-on-ink"
            >
              {t(`language.${language}`)}
            </button>
          ))}
        </fieldset>

        <fieldset className="m-0 flex flex-wrap gap-2 border-0 p-0">
          <legend className="mb-2 text-sm font-semibold text-muted">{t('theme.label')}</legend>
          {THEME_PREFERENCES.map((option) => (
            <button
              key={option}
              type="button"
              aria-pressed={preference === option}
              onClick={() => choose(option)}
              className="min-h-11 rounded-full border-[1.5px] border-border bg-surface px-4 font-semibold aria-pressed:border-ink aria-pressed:bg-ink aria-pressed:text-on-ink"
            >
              {t(`theme.${option}`)}
            </button>
          ))}
        </fieldset>
      </main>
    </div>
  );
}
