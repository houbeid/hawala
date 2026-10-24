import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '../ui/Button';
import { Icon } from '../ui/Icon';
import type { AppUpdates } from './appUpdates';

// Bandeau « Nouvelle version disponible » (PWA) : en bas de l'écran, au-dessus de la barre de navigation, sur
// toutes les pages (connexion comprise). « Mettre à jour » recharge l'app sur la nouvelle version ; « Plus tard »
// referme le bandeau, la mise à jour se fera à la prochaine ouverture de l'app.

export function UpdateNotice({ updates }: { updates: AppUpdates }) {
  const { t } = useTranslation();
  const [waiting, setWaiting] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => updates.start(() => setWaiting(true)), [updates]);

  if (!waiting) return null;
  return (
    <div
      role="status"
      className="fixed inset-x-3 bottom-[calc(env(safe-area-inset-bottom)+5.5rem)] z-50 mx-auto flex max-w-md flex-col gap-3 rounded-2xl border border-border bg-surface p-3.5 shadow-lg lg:bottom-4"
    >
      <p className="m-0 flex items-center gap-2 font-semibold">
        <Icon name="download" className="size-5 shrink-0" />
        {t('pwa.updateAvailable')}
      </p>
      <div className="flex gap-2">
        <Button
          variant="primary"
          busy={busy}
          busyLabel={t('pwa.updating')}
          onClick={() => {
            setBusy(true);
            void updates.apply();
          }}
        >
          {t('pwa.update')}
        </Button>
        <Button onClick={() => setWaiting(false)}>{t('pwa.later')}</Button>
      </div>
    </div>
  );
}
