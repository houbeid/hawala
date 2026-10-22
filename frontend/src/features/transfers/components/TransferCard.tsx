import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import type { Agency } from '@/features/organization/types';
import { formatAmount, formatRate } from '@/shared/format/money';
import { Icon } from '@/shared/ui/Icon';
import { amountToDeliver, formatOperationTime } from '../display';
import type { Transfer } from '../types';
import { CodeLockedChip, StatusChip } from './StatusChip';
import { TransferRoute } from './TransferRoute';

// Carte d'un transfert (téléphone, maquette 03) : heure et statut, référence, sens, « À livrer » en gros,
// taux, expéditeur → bénéficiaire. Toute la carte ouvre la fiche. Un annulé reste lisible mais atténué.

export function TransferCard({ transfer, agencies, withDay }: { transfer: Transfer; agencies: Map<string, Agency>; withDay: boolean }) {
  const { t } = useTranslation();
  const deliver = amountToDeliver(transfer);
  const cancelled = transfer.status === 'Cancelled';

  return (
    <Link
      to="/transfers/$transferId"
      params={{ transferId: transfer.id }}
      className={`flex flex-col gap-2 rounded-2xl border border-border bg-surface p-3.5 text-fg no-underline focus-visible:outline-3 focus-visible:outline-accent ${
        cancelled ? 'opacity-70' : ''
      }`}
    >
      <div className="flex items-center gap-2">
        <span className="font-bold text-muted tabular-nums">{formatOperationTime(transfer.operationAt, withDay)}</span>
        <span className="flex-1" />
        {transfer.isCodeLocked && transfer.status === 'NotDelivered' && <CodeLockedChip />}
        <StatusChip status={transfer.status} />
      </div>
      <span className="amount font-mono text-xs font-medium">{transfer.reference}</span>
      <TransferRoute transfer={transfer} agencies={agencies} />
      <div>
        <div className="text-sm text-muted">{t('transfers.toDeliver')}</div>
        <div className={`amount text-[28px] leading-tight ${cancelled ? 'line-through' : ''}`}>
          {formatAmount(deliver.amount, deliver.currency)}
        </div>
      </div>
      <span className="amount text-sm font-semibold text-muted">{formatRate(transfer.saleRate)}</span>
      <div className="flex flex-wrap items-center gap-1.5">
        <span>{transfer.senderInfo}</span>
        <Icon name="arrow" className="size-4 text-muted" />
        <b>{transfer.beneficiaryInfo}</b>
      </div>
    </Link>
  );
}
