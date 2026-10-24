import { useTranslation } from 'react-i18next';
import { useSession } from '@/shared/auth/hooks';
import { formatDayTimeShort } from '@/shared/format/dates';
import { Icon } from '@/shared/ui/Icon';

// Lecture hors ligne (app rouverte sans réseau) : on dit de quand datent les données affichées et que la session
// reprendra seule au retour du réseau. Les actions qui exigent le serveur affichent déjà leur propre explication.

export function OfflineSessionNotice() {
  const { t } = useTranslation();
  const state = useSession();
  if (state.status !== 'offline') return null;
  return (
    <p role="status" className="m-0 mb-3 flex items-start gap-2 rounded-xl bg-warn-bg px-3 py-2 text-sm font-semibold text-warn-fg">
      <Icon name="wifiOff" className="size-5 shrink-0" />
      {t('offline.reading', { since: formatDayTimeShort(state.since) })}
    </p>
  );
}
