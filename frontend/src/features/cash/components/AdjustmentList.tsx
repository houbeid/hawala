import { useTranslation } from 'react-i18next';
import { formatAmount } from '@/shared/format/money';
import type { BalanceAdjustment } from '../types';

// Ajustements de solde de la période (cash.budget.read) : affichés à part des mouvements, avec leur cible, la
// différence signée et le motif, pour qu'une correction reste visible de tous ceux qui lisent la caisse.

export function AdjustmentList({ adjustments }: { adjustments: BalanceAdjustment[] }) {
  const { t } = useTranslation();
  if (adjustments.length === 0) return null;
  return (
    <section className="flex flex-col gap-2" aria-label={t('cash.adjustment.listTitle')}>
      <h2 className="m-0 text-sm font-semibold text-muted">{t('cash.adjustment.listTitle')}</h2>
      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {adjustments.map((adjustment) => (
          <li key={adjustment.id} className="flex items-start justify-between gap-3 rounded-2xl border border-border bg-surface p-3.5">
            <span className="flex min-w-0 flex-col gap-0.5">
              <span className="font-semibold">{adjustment.targetName ?? '?'}</span>
              <span className="text-sm text-muted">{t(`cash.adjustment.targets.${adjustment.target}`)}</span>
              <span className="break-words text-sm">{t('cash.cancellationReason', { reason: adjustment.reason })}</span>
            </span>
            <span className="amount shrink-0">{formatAmount(adjustment.amount, adjustment.currency, { signed: true })}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}
