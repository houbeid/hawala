import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { Agency } from '@/features/organization/types';
import { formatAmount, formatRate } from '@/shared/format/money';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { amountPaid, amountToDeliver } from '../display';
import type { Transfer } from '../types';
import { TransferRoute } from '../components/TransferRoute';
import { formatDateTime } from './formatDateTime';

// Onglet « Détails » (maquette 05). Ce que le serveur ne renvoie pas n'existe pas dans la page : répartition et
// crédit pour l'agence de destination (null), prix d'achat et bénéfice sans profit.view (null).

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-muted">{label}</dt>
      <dd className="m-0 text-end font-semibold break-words">{children}</dd>
    </>
  );
}

export function TransferDetails({ transfer, agencies, createdBy }: { transfer: Transfer; agencies: Map<string, Agency>; createdBy: string | null }) {
  const { t } = useTranslation();
  const deliver = amountToDeliver(transfer);
  const paid = amountPaid(transfer);
  const city = (id: string) => {
    const agency = agencies.get(id);
    return agency ? t(`agencies.${agency.code}`, { defaultValue: agency.name }) : '';
  };

  return (
    <div className="flex flex-col gap-4">
      {transfer.status === 'Delivered' && (
        <Notice tone="info">
          {transfer.deliveredWithoutCode ? t('transfers.detail.deliveredWithoutCode') : t('transfers.detail.locked')}
        </Notice>
      )}
      {transfer.status === 'Cancelled' && (
        <Notice tone="warning">
          {t('transfers.detail.cancelled', { reason: transfer.cancellationReason ?? '' })}
        </Notice>
      )}
      {transfer.isCodeLocked && transfer.status === 'NotDelivered' && <Notice tone="error">{t('transfers.detail.codeLocked')}</Notice>}

      <section className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4">
        <span className="amount font-mono text-sm font-medium">{transfer.reference}</span>
        <TransferRoute transfer={transfer} agencies={agencies} />
        <div>
          <div className="text-sm text-muted">{t('transfers.detail.toDeliverIn', { city: city(transfer.destinationAgencyId) })}</div>
          <div className={`amount text-[32px] leading-tight ${transfer.status === 'Cancelled' ? 'line-through' : ''}`}>
            {formatAmount(deliver.amount, deliver.currency)}
          </div>
        </div>
        <span className="amount text-sm font-semibold text-muted">
          {transfer.purchaseRate !== null && `${t('rates.saleShort')} · `}
          {formatRate(transfer.saleRate)}
        </span>
        {transfer.purchaseRate !== null && (
          <span className="amount text-sm font-semibold text-muted">
            {t('rates.purchaseShort')} · {formatRate(transfer.purchaseRate)}
          </span>
        )}
      </section>

      <dl className="m-0 grid grid-cols-[auto_1fr] gap-x-4 gap-y-2.5 rounded-2xl border border-border bg-surface p-4">
        <Row label={t('transfers.detail.collectedIn', { city: city(transfer.sourceAgencyId) })}>
          <span className="amount">{formatAmount(paid.amount, paid.currency)}</span>
        </Row>
        {transfer.profitMro !== null && (
          <Row label={t('transfers.detail.profit')}>
            <span className="amount inline-flex items-center gap-1">
              <Icon name="shield" className="size-4 text-muted" />
              {formatAmount(transfer.profitMro, 'MRO', { signed: true, maxDecimals: 0 })}
            </span>
          </Row>
        )}
        <Row label={t('transfers.create.sender')}>{transfer.senderInfo}</Row>
        <Row label={t('transfers.create.beneficiary')}>{transfer.beneficiaryInfo}</Row>
        {transfer.note && <Row label={t('transfers.detail.note')}>{transfer.note}</Row>}
        <Row label={t('transfers.detail.operationAt')}>{formatDateTime(transfer.operationAt)}</Row>
        {createdBy && <Row label={t('transfers.detail.createdBy')}>{createdBy}</Row>}
        {transfer.deliveryOperationAt && (
          <Row label={t('transfers.detail.deliveredAt')}>{formatDateTime(transfer.deliveryOperationAt)}</Row>
        )}
        {transfer.cancellationOperationAt && (
          <Row label={t('transfers.detail.cancelledAt')}>{formatDateTime(transfer.cancellationOperationAt)}</Row>
        )}
      </dl>

      {transfer.payments !== null && (
        <section className="flex flex-col gap-2 rounded-2xl border border-border bg-surface p-4" aria-label={t('transfers.detail.payment')}>
          <h2 className="m-0 text-sm font-semibold text-muted">{t('transfers.detail.payment')}</h2>
          <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
            {transfer.payments.map((payment) => (
              <li key={payment.treasuryAccountId} className="flex items-center justify-between gap-3">
                <span>{payment.treasuryAccountName ?? t('transfers.detail.unknownAccount')}</span>
                <span className="amount">{formatAmount(payment.amount, payment.currency)}</span>
              </li>
            ))}
            {transfer.credit && (
              <li className="flex items-center justify-between gap-3">
                <span>
                  {t('transfers.create.recapCredit', { name: transfer.credit.partyName ?? '' })}{' '}
                  <span className="text-sm text-muted">· {t('transfers.detail.owesUs')}</span>
                </span>
                <span className="amount">{formatAmount(transfer.credit.amount, transfer.credit.currency)}</span>
              </li>
            )}
          </ul>
        </section>
      )}

      {transfer.payments === null && transfer.status === 'NotDelivered' && (
        <Notice tone="info">{t('transfers.detail.codeHeldBySender')}</Notice>
      )}
    </div>
  );
}
