import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { formatAmount } from '@/shared/format/money';
import { useOperationQueue, useQueuedOperations } from '@/shared/offline/queueContext';
import { QueuedOperationCard } from '@/shared/offline/QueuedOperationCard';
import { queuedErrorMessage } from '@/shared/offline/queuedErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Notice } from '@/shared/ui/Notice';
import { TRANSFER_QUEUE_KIND } from '../offlineQueue';
import type { CreatedTransfer, CreateTransferRequest } from '../types';
import { ReceiptButton } from './ReceiptButton';
import { WithdrawalCode } from './WithdrawalCode';

// Transferts saisis sans réseau par ce compte (en tête de la liste) : en attente d'envoi, rejetés, ou envoyés. Un
// transfert envoyé montre sa référence et son code de retrait, tiré par le serveur à l'envoi, avec le reçu à envoyer
// à l'expéditeur ; « Terminé » efface ce code du téléphone (décision du 24 octobre 2026).

export function QueuedTransfers() {
  const { t, i18n } = useTranslation();
  const operations = useQueuedOperations(TRANSFER_QUEUE_KIND);
  const queue = useOperationQueue();
  if (operations.length === 0) return null;

  return (
    <section className="flex flex-col gap-2" aria-label={t('queue.transfersTitle')}>
      <h2 className="m-0 text-sm font-semibold text-muted">{t('queue.transfersTitle')}</h2>
      <ul className="m-0 flex list-none flex-col overflow-hidden rounded-2xl border border-border p-0">
        {operations.map((operation) => {
          const request = operation.request as CreateTransferRequest;
          const result = operation.result as CreatedTransfer | null;
          return (
            <QueuedOperationCard
              key={operation.id}
              operation={operation}
              title={formatAmount(request.amountXof, 'XOF')}
              rejection={operation.error ? queuedErrorMessage(i18n, operation.error) : null}
              correct={
                <Link
                  to="/transfers/new"
                  search={{ fromQueue: operation.id }}
                  className="inline-flex min-h-12 items-center rounded-[14px] border-2 border-ink px-4 font-semibold text-fg no-underline"
                >
                  {t('queue.correct')}
                </Link>
              }
              sent={
                result && (
                  <div className="flex flex-col gap-3">
                    <span className="amount font-mono text-sm">{result.reference}</span>
                    {result.withdrawalCode ? (
                      <>
                        <WithdrawalCode code={result.withdrawalCode} />
                        <Notice tone="warning">{t('queue.codeOnce')}</Notice>
                        <ReceiptButton transferId={result.id} reference={result.reference} code={result.withdrawalCode} />
                      </>
                    ) : (
                      <Notice tone="info">{t('transfers.create.done.noCodeReplayed')}</Notice>
                    )}
                    <Button variant="primary" fullWidth onClick={() => void queue?.remove(operation.id)}>
                      {t('transfers.create.done.finish')}
                    </Button>
                  </div>
                )
              }
            />
          );
        })}
      </ul>
    </section>
  );
}
