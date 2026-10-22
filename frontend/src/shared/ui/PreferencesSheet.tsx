import * as Dialog from '@radix-ui/react-dialog';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { LANGUAGES } from '@/i18n';
import { THEME_PREFERENCES } from '@/shared/theme/theme';
import { useTheme } from '@/shared/theme/useTheme';
import { Icon } from './Icon';

// Réglage rapide « Langue et thème » (maquette 02) : feuille en bas sur téléphone, fenêtre centrée sur
// ordinateur. Radix Dialog gère le focus, la touche Échap et l'annonce au lecteur d'écran.

const optionClass =
  'inline-flex min-h-11 items-center gap-1.5 rounded-full border-[1.5px] border-border bg-surface px-3.5 text-[15px] font-semibold ' +
  'aria-pressed:border-ink aria-pressed:bg-ink aria-pressed:text-on-ink';

export function PreferencesSheet({ trigger }: { trigger: ReactNode }) {
  const { t, i18n } = useTranslation();
  const { preference, choose } = useTheme();

  return (
    <Dialog.Root>
      <Dialog.Trigger asChild>{trigger}</Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-black/55" />
        <Dialog.Content
          dir={i18n.dir()}
          className="fixed inset-x-0 bottom-0 z-50 flex flex-col gap-3 rounded-t-3xl border-t border-border bg-surface p-4 pb-6 text-fg lg:inset-x-auto lg:bottom-auto lg:start-1/2 lg:top-1/2 lg:w-[420px] lg:-translate-x-1/2 lg:-translate-y-1/2 lg:rounded-3xl lg:border rtl:lg:translate-x-1/2"
        >
          <div className="mx-auto h-1.5 w-11 rounded-full bg-border lg:hidden" aria-hidden="true" />
          <div className="flex items-center">
            <Dialog.Title className="m-0 flex-1 text-lg font-bold">{t('shell.preferences')}</Dialog.Title>
            <Dialog.Close className="inline-flex size-11 items-center justify-center rounded-xl" aria-label={t('common.close')}>
              <Icon name="x" />
            </Dialog.Close>
          </div>
          <Dialog.Description className="sr-only">{t('shell.preferencesHint')}</Dialog.Description>

          <span className="text-sm font-semibold text-muted">{t('language.label')}</span>
          <div className="flex flex-wrap gap-2">
            {LANGUAGES.map((language) => (
              <button
                key={language}
                type="button"
                lang={language}
                aria-pressed={i18n.language === language}
                onClick={() => void i18n.changeLanguage(language)}
                className={optionClass}
              >
                {i18n.language === language && <Icon name="check" className="size-5" />}
                {t(`language.${language}`)}
              </button>
            ))}
          </div>

          <span className="text-sm font-semibold text-muted">{t('theme.label')}</span>
          <div className="flex flex-wrap gap-2">
            {THEME_PREFERENCES.map((option) => (
              <button key={option} type="button" aria-pressed={preference === option} onClick={() => choose(option)} className={optionClass}>
                {option === 'light' && <Icon name="sun" className="size-5" />}
                {option === 'dark' && <Icon name="moon" className="size-5" />}
                {t(`theme.${option}`)}
              </button>
            ))}
          </div>
          <p className="m-0 text-sm text-muted">{t('shell.rtlHint')}</p>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
