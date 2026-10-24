import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { formatDayTimeShort } from '@/shared/format/dates';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { useCloseMySession, useMySessions } from '../sessions';

// Mes appareils (route /me/sessions, tout compte connecté) : chaque session ouverte (appareil, IP, ouverture,
// dernière activité). « Fermer » déconnecte l'appareil tout de suite (effet immédiat côté serveur, déconnexion en
// direct). L'appareil en cours n'a pas ce bouton : il se ferme par « Se déconnecter », qui nettoie tout ici aussi.

export function MySessionsPage() {
  const { t, i18n } = useTranslation();
  const online = useOnlineStatus();
  const sessions = useMySessions();
  const close = useCloseMySession();
  const [error, setError] = useState<string | null>(null);

  async function run(id: string) {
    setError(null);
    try {
      await close.mutateAsync(id);
    } catch (e) {
      setError(errorMessage(i18n, e));
    }
  }

  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-4">
      <p className="m-0 text-sm text-muted">{t('me.sessions.hint')}</p>
      {!online && <Notice tone="info">{t('me.sessions.offline')}</Notice>}
      {error && <Notice tone="error">{error}</Notice>}
      {sessions.isPending && (
        <p role="status" className="m-0 text-muted">
          {t('common.loading')}
        </p>
      )}
      {sessions.isError && <Notice tone="error">{errorMessage(i18n, sessions.error)}</Notice>}
      {sessions.data && (
        <ul className="m-0 flex list-none flex-col gap-2 p-0" aria-label={t('nav.sessions')}>
          {[...sessions.data]
            .sort((a, b) => Number(b.current) - Number(a.current) || b.lastUsedAt.localeCompare(a.lastUsedAt))
            .map((s) => (
              <li key={s.id} className="flex items-start gap-3 rounded-2xl border border-border bg-surface p-3.5">
                <Icon name="devices" className="mt-0.5 text-muted" />
                <span className="flex min-w-0 flex-1 flex-col gap-0.5 text-sm">
                  <span className="break-words font-semibold">
                    {s.device ?? t('users.sessions.unknownDevice')}
                    {s.current && ` · ${t('users.sessions.current')}`}
                  </span>
                  {s.ipAddress && (
                    <span className="text-muted" dir="ltr">
                      {s.ipAddress}
                    </span>
                  )}
                  <span className="text-muted">{t('users.sessions.times', { started: formatDayTimeShort(s.startedAt), used: formatDayTimeShort(s.lastUsedAt) })}</span>
                  {s.current && <span className="text-muted">{t('me.sessions.currentHint')}</span>}
                </span>
                {!s.current && online && (
                  <Button variant="danger" onClick={() => void run(s.id)} disabled={close.isPending}>
                    {t('users.sessions.close')}
                  </Button>
                )}
              </li>
            ))}
        </ul>
      )}
    </div>
  );
}
