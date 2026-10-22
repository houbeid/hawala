import { useTranslation } from 'react-i18next';
import { useCurrentAccount } from '@/shared/auth/hooks';
import { Icon } from '@/shared/ui/Icon';
import { PreferencesSheet } from '@/shared/ui/PreferencesSheet';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { AgencySwitcher } from './AgencySwitcher';

// En-tête (maquette 02 / bureau D1) : filet de la couleur d'agence, agence, titre de l'écran,
// « Lecture seule » pour la Consultation, réglages langue et thème avec l'indicateur de réseau.
// POINT OUVERT : la pastille « éléments en attente de synchronisation » viendra avec le hors ligne (phase 7).

export function TopBar({ title }: { title: string }) {
  const { t } = useTranslation();
  const account = useCurrentAccount();
  const online = useOnlineStatus();

  return (
    <header className="sticky top-0 z-30 flex min-h-16 items-center gap-1.5 border-b border-t-[5px] border-b-border border-t-agency bg-surface px-2.5 lg:min-h-[68px] lg:gap-3 lg:px-6">
      <AgencySwitcher />
      <h1 className="m-0 min-w-0 flex-1 truncate px-1 text-xl font-semibold lg:text-[22px]">{title}</h1>
      {account?.role === 'ReadOnly' && (
        <span className="inline-flex h-7 shrink-0 items-center gap-1.5 rounded-full border-[1.5px] border-current px-2.5 text-xs font-bold text-muted">
          <Icon name="eye" className="size-4" />
          {t('shell.readOnly')}
        </span>
      )}
      <PreferencesSheet
        trigger={
          <button
            type="button"
            className="inline-flex size-11 shrink-0 items-center justify-center rounded-xl text-fg"
            aria-label={online ? t('shell.preferences') : t('shell.offlinePreferences')}
          >
            <Icon name={online ? 'globe' : 'wifiOff'} />
          </button>
        }
      />
    </header>
  );
}
