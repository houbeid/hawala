import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Icon, type IconName } from '@/shared/ui/Icon';
import { PreferenceBar } from '@/shared/ui/PreferenceBar';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { BrandPanel } from './BrandPanel';

// Cadre des écrans sans session (maquette 01) : filet d'agence, langue et thème, logo, une colonne
// de 420 px au plus. Téléphone : le formulaire en plein écran. Ordinateur (≥ 1024 px) : panneau de
// marque à gauche (à droite en arabe), formulaire centré dans l'autre moitié.

export function AuthLayout({ icon = 'transfer', title, children }: { icon?: IconName; title: string; children: ReactNode }) {
  const { t } = useTranslation();
  const online = useOnlineStatus();

  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-2">
      <BrandPanel />
      <div className="flex min-h-dvh flex-col border-t-[5px] border-t-agency">
        {!online && (
          <div role="status" className="flex items-center gap-2 bg-warn-bg px-4 py-2.5 text-sm font-semibold text-warn-fg">
            <Icon name="wifiOff" />
            <span>{t('common.offline')}</span>
          </div>
        )}
        <div className="mx-auto flex w-full max-w-[420px] flex-1 flex-col gap-3.5 p-4 lg:justify-center lg:py-12">
          <PreferenceBar />
          <div className="flex items-center gap-3">
            <span className="inline-flex size-12 items-center justify-center rounded-[14px] bg-ink text-on-ink">
              <Icon name={icon} />
            </span>
            <div>
              <div className="text-xl font-bold">{t('app.name')}</div>
              <div className="text-sm text-muted">{t('app.corridor')}</div>
            </div>
          </div>
          <h1 className="m-0 text-[26px] font-bold leading-tight">{title}</h1>
          {children}
        </div>
      </div>
    </div>
  );
}
