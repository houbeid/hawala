import { useTranslation } from 'react-i18next';
import { Icon } from '../ui/Icon';
import { useOnlineStatus } from '../useOnlineStatus';
import { useRealtimeStatus } from './realtimeStatus';

// Bandeau discret quand les mises à jour en direct sont coupées alors que le réseau est là (hors ligne, le bandeau
// « pas de réseau » suffit). L'application continue de marcher : elle relit les données à chaque écran ouvert ;
// seules les nouveautés de l'autre agence n'arrivent plus d'elles-mêmes. La reconnexion est automatique.

export function RealtimeNotice() {
  const { t } = useTranslation();
  const online = useOnlineStatus();
  const status = useRealtimeStatus();
  if (!online || (status !== 'reconnecting' && status !== 'disconnected')) return null;
  return (
    <p role="status" className="m-0 mb-3 flex items-center gap-2 rounded-xl bg-surface-2 px-3 py-2 text-sm">
      <Icon name="sync" className="size-5 shrink-0" spin={status === 'reconnecting'} />
      {status === 'reconnecting' ? t('realtime.reconnecting') : t('realtime.disconnected')}
    </p>
  );
}
