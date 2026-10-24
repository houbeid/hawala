import { useTranslation } from 'react-i18next';
import { formatAmount, formatRate } from '@/shared/format/money';
import { Icon, type IconName } from '@/shared/ui/Icon';
import type { Transfer, TransferEvent, TransferEventType } from '../types';
import { readChangeValue, wrongCodeAttempt, type ChangeValue } from './detailRules';
import { formatDateTime } from './formatDateTime';

// Onglet « Historique » (maquette 05) : chronologie verticale, une icône par type d'événement, auteur, heure,
// motif, et un bloc Avant / Après pour chaque champ changé. Le serveur a déjà retiré les champs que le lecteur
// n'a pas le droit de voir ; la pièce d'identité d'une livraison sans code n'y figure jamais.

const ICONS: Record<TransferEventType, IconName> = {
  Created: 'plus',
  Edited: 'pencil',
  Delivered: 'check',
  DeliveredWithoutCode: 'doc',
  Cancelled: 'ban',
  CodeUnlocked: 'unlock',
  CodeReplaced: 'sync',
  WrongCode: 'x',
  Resumed: 'download',
};

/** Compteur d'essais : son évolution est dite par le titre « essai n sur 5 », pas dans le bloc Avant / Après. */
const HIDDEN_FIELDS = new Set(['FailedCodeAttempts']);

function Value({ value, transfer }: { value: ChangeValue; transfer: Transfer }) {
  const { t } = useTranslation();
  switch (value.kind) {
    case 'empty':
      return <span className="text-muted">—</span>;
    case 'changed':
      return <span>{t('transfers.history.changed')}</span>;
    case 'amount':
      return <span className="amount font-semibold">{formatAmount(value.value, value.currency)}</span>;
    case 'rate':
      return <span className="amount font-semibold">{formatRate(value.value)}</span>;
    case 'date':
      return <span>{formatDateTime(value.iso)}</span>;
    case 'status':
      return <span>{t(`transfers.status.${value.status}`, { defaultValue: value.status })}</span>;
    case 'payments': {
      const names = new Map((transfer.payments ?? []).map((p) => [p.treasuryAccountId, p.treasuryAccountName]));
      return (
        <span>
          {value.lines.map((line, index) => (
            <span key={`${line.accountId}-${index}`}>
              {index > 0 && ', '}
              {names.get(line.accountId) ?? t('transfers.detail.unknownAccount')}{' '}
              <span className="amount">{formatAmount(line.amount, transfer.sourceCurrency)}</span>
            </span>
          ))}
        </span>
      );
    }
    case 'text':
      return <span className="break-words">{value.text}</span>;
  }
}

function EventItem({ event, transfer }: { event: TransferEvent; transfer: Transfer }) {
  const { t } = useTranslation();
  const attempt = event.type === 'WrongCode' ? wrongCodeAttempt(event) : null;
  const changes = event.changes.filter((change) => !HIDDEN_FIELDS.has(change.field));

  return (
    <li className="relative flex gap-3 pb-5 last:pb-0">
      <span
        className="relative z-10 inline-flex size-9 shrink-0 items-center justify-center rounded-full border-2 border-ink bg-surface"
        aria-hidden="true"
      >
        <Icon name={ICONS[event.type]} className="size-5" />
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-1.5">
        <div className="font-semibold">{t(`transfers.history.types.${event.type}`)}</div>
        <div className="text-sm text-muted">
          {event.authorName} · {formatDateTime(event.at)}
          {attempt !== null && ` · ${t('transfers.history.attempt', { n: attempt })}`}
        </div>
        {event.reason && <div className="text-sm">{t('transfers.history.reason', { reason: event.reason })}</div>}
        {changes.length > 0 && (
          <table className="w-full border-collapse rounded-xl bg-surface-2 text-sm">
            <thead>
              <tr className="text-xs font-bold text-muted uppercase">
                <th scope="col" className="px-2.5 pt-2 text-start">
                  <span className="sr-only">{t('transfers.history.field')}</span>
                </th>
                <th scope="col" className="px-2.5 pt-2 text-start">
                  {t('transfers.history.before')}
                </th>
                <th scope="col" className="px-2.5 pt-2 text-start">
                  {t('transfers.history.after')}
                </th>
              </tr>
            </thead>
            <tbody>
              {changes.map((change) => (
                <tr key={change.field} className="align-top">
                  <th scope="row" className="px-2.5 py-1.5 text-start font-normal text-muted">
                    {t(`transfers.history.fields.${change.field}`, { defaultValue: change.field })}
                  </th>
                  <td className="px-2.5 py-1.5">
                    <Value value={readChangeValue(change, 'before', transfer)} transfer={transfer} />
                  </td>
                  <td className="px-2.5 py-1.5">
                    <Value value={readChangeValue(change, 'after', transfer)} transfer={transfer} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </li>
  );
}

export function TransferHistory({ events, transfer }: { events: TransferEvent[]; transfer: Transfer }) {
  const { t } = useTranslation();
  if (events.length === 0) return <p className="m-0 text-muted">{t('transfers.history.empty')}</p>;
  return (
    <ol className="relative m-0 list-none p-0 before:absolute before:start-[17px] before:top-2 before:bottom-2 before:w-0.5 before:bg-border">
      {events.map((event, index) => (
        <EventItem key={`${event.at}-${index}`} event={event} transfer={transfer} />
      ))}
    </ol>
  );
}
