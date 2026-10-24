import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useQueuedOperations } from '@/shared/offline/queueContext';
import { Icon } from '@/shared/ui/Icon';

// Rappel discret, sur toutes les pages : des opérations saisies hors ligne attendent l'envoi, ou ont été rejetées et
// demandent une décision de l'agent (« Corriger » ou « Abandonner »). Le lien mène à la liste des transferts, où la
// file des transferts est en tête (celle de la caisse est en tête de l'écran Caisse).

export function PendingOperationsNotice() {
  const { t } = useTranslation();
  const operations = useQueuedOperations();
  const pending = operations.filter((op) => op.status === 'pending').length;
  const rejected = operations.filter((op) => op.status === 'rejected').length;
  if (pending === 0 && rejected === 0) return null;

  return (
    <p role="status" className="m-0 mb-3 flex flex-wrap items-center gap-2 rounded-xl bg-surface-2 px-3 py-2 text-sm font-semibold">
      <Icon name="clock" className="size-5 shrink-0" />
      {pending > 0 && <span>{t('queue.pendingCount', { count: pending })}</span>}
      {rejected > 0 && <span>{t('queue.rejectedCount', { count: rejected })}</span>}
      <Link to="/transfers" className="font-semibold text-fg underline">
        {t('queue.see')}
      </Link>
    </p>
  );
}
