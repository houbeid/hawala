import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { formatDayTimeShort } from '../format/dates';
import { Button } from '../ui/Button';
import { Icon } from '../ui/Icon';
import { Notice } from '../ui/Notice';
import { Sheet } from '../ui/Sheet';
import { useOnlineStatus } from '../useOnlineStatus';
import type { QueuedOperation } from './operationQueue';
import { useOperationQueue } from './queueContext';

// Une opération de la file hors ligne (décisions du 24 octobre 2026) : en attente d'envoi, rejetée (motif, « Corriger »
// en ligne, « Abandonner » après confirmation) ou envoyée (contenu fourni par l'écran : code de retrait et reçu).
// L'état est écrit en mots, avec une icône : jamais par la couleur seule.

interface Props {
  operation: QueuedOperation;
  /** Montant, en gros. */
  title: string;
  /** Motif d'un rejet, déjà traduit. */
  rejection: string | null;
  /** Lien « Corriger » (formulaire rempli) ; affiché seulement en ligne. */
  correct: ReactNode;
  /** Contenu d'une opération envoyée (référence, code, reçu, « Terminé »). */
  sent?: ReactNode;
}

export function QueuedOperationCard({ operation, title, rejection, correct, sent }: Props) {
  const { t } = useTranslation();
  const queue = useOperationQueue();
  const online = useOnlineStatus();
  const [abandoning, setAbandoning] = useState(false);

  const state =
    operation.status === 'pending'
      ? { icon: 'clock' as const, text: t('queue.status.pending') }
      : operation.status === 'rejected'
        ? { icon: 'alert' as const, text: t('queue.status.rejected') }
        : { icon: 'check' as const, text: t('queue.status.sent') };

  return (
    <li className="flex flex-col gap-2 border-b border-border bg-surface p-3.5 last:border-b-0">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="amount font-semibold">{title}</span>
        <span className="inline-flex items-center gap-1.5 rounded-full bg-surface-2 px-2.5 py-1 text-sm font-semibold">
          <Icon name={state.icon} className="size-4" />
          {state.text}
        </span>
      </div>
      {operation.label && <span>{operation.label}</span>}
      <span className="text-sm text-muted">{t('queue.enteredAt', { at: formatDayTimeShort(operation.enteredAt) })}</span>

      {operation.status === 'rejected' && (
        <>
          {rejection && <Notice tone="error">{rejection}</Notice>}
          <div className="flex flex-wrap gap-2">
            {online ? correct : <span className="text-sm text-muted">{t('queue.correctOnline')}</span>}
            <Button variant="danger" icon={<Icon name="x" />} onClick={() => setAbandoning(true)}>
              {t('queue.abandon')}
            </Button>
          </div>
        </>
      )}
      {operation.status === 'sent' && sent}

      {abandoning && (
        <Sheet open onOpenChange={(open) => !open && setAbandoning(false)} title={t('queue.abandonTitle')}>
          <div className="flex flex-col gap-3">
            <Notice tone="warning">{t('queue.abandonHint')}</Notice>
            <Button variant="danger" fullWidth onClick={() => void queue?.remove(operation.id)}>
              {t('queue.abandon')}
            </Button>
            <Button fullWidth onClick={() => setAbandoning(false)}>
              {t('queue.keep')}
            </Button>
          </div>
        </Sheet>
      )}
    </li>
  );
}
