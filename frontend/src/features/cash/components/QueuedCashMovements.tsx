import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { formatAmount } from '@/shared/format/money';
import { useQueuedOperations } from '@/shared/offline/queueContext';
import { QueuedOperationCard } from '@/shared/offline/QueuedOperationCard';
import { queuedErrorMessage } from '@/shared/offline/queuedErrorMessage';
import { CASH_QUEUE_KIND } from '../offlineQueue';
import type { CreateCashMovementRequest } from '../types';

// Encaissements et décaissements saisis sans réseau par ce compte (en tête de la caisse) : en attente d'envoi ou
// rejetés. Une fois envoyés, ils disparaissent d'ici et apparaissent dans la liste du serveur.

export function QueuedCashMovements({ currency }: { currency: 'MRO' | 'XOF' }) {
  const { t, i18n } = useTranslation();
  const operations = useQueuedOperations(CASH_QUEUE_KIND);
  if (operations.length === 0) return null;

  return (
    <section className="flex flex-col gap-2" aria-label={t('queue.cashTitle')}>
      <h2 className="m-0 text-sm font-semibold text-muted">{t('queue.cashTitle')}</h2>
      <ul className="m-0 flex list-none flex-col overflow-hidden rounded-2xl border border-border p-0">
        {operations.map((operation) => {
          const request = operation.request as CreateCashMovementRequest;
          return (
            <QueuedOperationCard
              key={operation.id}
              operation={operation}
              title={`${t(`cash.types.${request.type}`)} · ${formatAmount(request.amount, currency)}`}
              rejection={operation.error ? queuedErrorMessage(i18n, operation.error) : null}
              correct={
                <Link
                  to="/cash/new"
                  search={{ fromQueue: operation.id }}
                  className="inline-flex min-h-12 items-center rounded-[14px] border-2 border-ink px-4 font-semibold text-fg no-underline"
                >
                  {t('queue.correct')}
                </Link>
              }
            />
          );
        })}
      </ul>
    </section>
  );
}
