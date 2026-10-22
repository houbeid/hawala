import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import type { Agency } from '@/features/organization/types';
import { formatAmount } from '@/shared/format/money';
import { AgencyBadge } from '@/shared/ui/AgencyBadge';
import { Icon } from '@/shared/ui/Icon';
import { amountPaid, amountToDeliver, formatOperationTime } from '../display';
import type { Transfer } from '../types';
import { CodeLockedChip, StatusChip } from './StatusChip';

// Tableau des transferts (ordinateur, bureau D1). Colonne « Bénéfice » seulement si le serveur l'a renvoyé
// (profit.view) : un agent ne la voit jamais. La référence ouvre la fiche.

export function TransferTable({ transfers, agencies, withDay }: { transfers: Transfer[]; agencies: Map<string, Agency>; withDay: boolean }) {
  const { t } = useTranslation();
  const showProfit = transfers.some((transfer) => transfer.profitMro !== null);
  const th = 'whitespace-nowrap border-b border-border bg-surface-2 px-3.5 py-2.5 text-start text-[13px] font-bold text-muted';
  const td = 'border-b border-border px-3.5 py-2.5 align-middle';

  return (
    <div className="overflow-x-auto rounded-2xl border border-border bg-surface">
      <table className="w-full border-collapse">
        <caption className="sr-only">{t('transfers.tableCaption')}</caption>
        <thead>
          <tr>
            <th scope="col" className={th}>{t('transfers.columns.time')}</th>
            <th scope="col" className={th}>{t('transfers.columns.reference')}</th>
            <th scope="col" className={th}>{t('transfers.columns.route')}</th>
            <th scope="col" className={th}>{t('transfers.columns.parties')}</th>
            <th scope="col" className={`${th} text-end`}>{t('transfers.columns.paid')}</th>
            <th scope="col" className={`${th} text-end`}>{t('transfers.toDeliver')}</th>
            {showProfit && <th scope="col" className={`${th} text-end`}>{t('transfers.columns.profit')}</th>}
            <th scope="col" className={th}>{t('transfers.columns.status')}</th>
          </tr>
        </thead>
        <tbody>
          {transfers.map((transfer) => {
            const source = agencies.get(transfer.sourceAgencyId);
            const destination = agencies.get(transfer.destinationAgencyId);
            const paid = amountPaid(transfer);
            const deliver = amountToDeliver(transfer);
            const cancelled = transfer.status === 'Cancelled';
            return (
              <tr key={transfer.id} className={cancelled ? 'text-muted' : ''}>
                <td className={`${td} tabular-nums`}>{formatOperationTime(transfer.operationAt, withDay)}</td>
                <td className={td}>
                  <Link
                    to="/transfers/$transferId"
                    params={{ transferId: transfer.id }}
                    className="amount font-mono text-xs font-medium text-fg underline-offset-4 hover:underline"
                  >
                    {transfer.reference}
                  </Link>
                </td>
                <td className={td}>
                  <span className="inline-flex items-center gap-1.5">
                    <AgencyBadge code={source?.code ?? '…'} color={source?.color ?? 'var(--muted)'} />
                    <Icon name="arrow" className="size-4 text-muted" />
                    <AgencyBadge code={destination?.code ?? '…'} color={destination?.color ?? 'var(--muted)'} />
                  </span>
                </td>
                <td className={td}>
                  <div>{transfer.senderInfo}</div>
                  <div className="font-semibold">{transfer.beneficiaryInfo}</div>
                </td>
                <td className={`${td} text-end`}>
                  <span className="amount font-semibold">{formatAmount(paid.amount, paid.currency)}</span>
                </td>
                <td className={`${td} text-end`}>
                  <span className={`amount ${cancelled ? 'line-through' : ''}`}>{formatAmount(deliver.amount, deliver.currency)}</span>
                </td>
                {showProfit && (
                  <td className={`${td} text-end`}>
                    {transfer.profitMro !== null && (
                      <span className="amount font-semibold">{formatAmount(transfer.profitMro, 'MRO', { signed: true, maxDecimals: 0 })}</span>
                    )}
                  </td>
                )}
                <td className={td}>
                  <span className="inline-flex flex-wrap gap-1.5">
                    <StatusChip status={transfer.status} />
                    {transfer.isCodeLocked && transfer.status === 'NotDelivered' && <CodeLockedChip />}
                  </span>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
