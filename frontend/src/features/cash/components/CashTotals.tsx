import { useTranslation } from 'react-i18next';
import type { Currency } from '@/features/organization/types';
import { formatAmount } from '@/shared/format/money';
import type { CashMovementList } from '../types';

// Totaux de la période (maquette 09) : entrées, sorties, net (annulés exclus, calculés par le serveur) et, à part,
// les virements internes, qui ne changent pas le net. Une seule agence, donc une seule devise.

export function CashTotals({ list, currency }: { list: CashMovementList; currency: Currency }) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-2">
      <dl className="m-0 grid grid-cols-3 gap-2 rounded-2xl border border-border bg-surface p-3.5">
        <div className="flex flex-col gap-0.5">
          <dt className="text-xs text-muted">{t('cash.totals.incoming')}</dt>
          <dd className="amount m-0">{formatAmount(list.incoming.amount, currency)}</dd>
        </div>
        <div className="flex flex-col gap-0.5">
          <dt className="text-xs text-muted">{t('cash.totals.outgoing')}</dt>
          <dd className="amount m-0">{formatAmount(list.outgoing.amount, currency)}</dd>
        </div>
        <div className="flex flex-col gap-0.5">
          <dt className="text-xs text-muted">{t('cash.totals.net')}</dt>
          <dd className="amount m-0">{formatAmount(list.net, currency, { signed: true })}</dd>
        </div>
      </dl>
      {list.internalTransfers.count > 0 && (
        <p className="m-0 text-sm text-muted">
          {t('cash.totals.internal', { count: list.internalTransfers.count, amount: formatAmount(list.internalTransfers.amount, currency) })}
        </p>
      )}
    </div>
  );
}
