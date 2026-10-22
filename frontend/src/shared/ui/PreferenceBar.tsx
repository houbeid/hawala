import { useTranslation } from 'react-i18next';
import { LANGUAGES } from '@/i18n';
import { useTheme } from '@/shared/theme/useTheme';
import { resolveTheme } from '@/shared/theme/theme';
import { Icon } from './Icon';

// Langue (FR / عربي) et thème, en haut des écrans d'accueil (connexion, maquette 01).

const LANGUAGE_SHORT = { fr: 'FR', ar: 'عربي' } as const;

export function PreferenceBar() {
  const { t, i18n } = useTranslation();
  const { preference, choose } = useTheme();
  const dark = resolveTheme(preference) === 'dark';

  return (
    <div className="flex items-center gap-2">
      {LANGUAGES.map((language) => (
        <button
          key={language}
          type="button"
          lang={language}
          aria-label={t(`language.${language}`)}
          aria-pressed={i18n.language === language}
          onClick={() => void i18n.changeLanguage(language)}
          className="min-h-11 rounded-full border-[1.5px] border-border bg-surface px-3.5 text-[15px] font-semibold aria-pressed:border-ink aria-pressed:bg-ink aria-pressed:text-on-ink"
        >
          {LANGUAGE_SHORT[language]}
        </button>
      ))}
      <span className="flex-1" />
      <button
        type="button"
        onClick={() => choose(dark ? 'light' : 'dark')}
        aria-label={dark ? t('theme.toLight') : t('theme.toDark')}
        className="inline-flex size-11 items-center justify-center rounded-xl text-fg"
      >
        <Icon name={dark ? 'sun' : 'moon'} />
      </button>
    </div>
  );
}
