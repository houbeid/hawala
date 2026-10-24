import { useTranslation } from 'react-i18next';
import { formatDayShort, formatDayTimeShort } from '@/shared/format/dates';
import { formatAmount, unitOf } from '@/shared/format/money';
import { HistoryChart } from '@/shared/ui/HistoryChart';
import { historySeries } from '@/shared/ui/historySeries';
import type { CashBudgetSnapshot } from '../types';

// Historique des budgets enregistrés (30 derniers jours) : la courbe du budget (maquette 10, à partir de deux jours),
// puis la liste, plus récent d'abord : date, heure, budget, nombre d'alertes gardées avec la photo. La liste est la
// vue en texte de la courbe. Une seule caisse, donc une seule devise.

export function SnapshotHistory({ snapshots }: { snapshots: CashBudgetSnapshot[] }) {
  const { t } = useTranslation();
  const sorted = [...snapshots].sort((a, b) => b.date.localeCompare(a.date));
  const currency = sorted[0]?.currency;
  return (
    <section className="flex flex-col gap-2" aria-label={t('budget.history.title')}>
      <h2 className="m-0 text-base font-bold">{t('budget.history.title')}</h2>
      {currency && (
        <HistoryChart
          title={t('budget.history.chartTitle', { unit: unitOf(currency) })}
          points={historySeries(sorted, (s) => s.date, (s) => s.budget)}
          formatValue={(value) => formatAmount(value, currency, { maxDecimals: 0 })}
        />
      )}
      {sorted.length === 0 ? (
        <p className="m-0 text-muted">{t('budget.history.empty')}</p>
      ) : (
        <ul className="m-0 flex list-none flex-col rounded-2xl border border-border bg-surface p-0">
          {sorted.map((snapshot) => (
            <li key={snapshot.id} className="flex items-center justify-between gap-3 border-b border-border px-3.5 py-2.5 last:border-b-0">
              <span className="flex flex-col">
                <span className="font-semibold tabular-nums">{formatDayShort(snapshot.date)}</span>
                <span className="text-sm text-muted">
                  {t('budget.history.savedAt', { time: formatDayTimeShort(snapshot.savedAt).slice(-5) })}
                  {snapshot.alerts.length > 0 && ` · ${t('budget.history.alerts', { count: snapshot.alerts.length })}`}
                </span>
              </span>
              <span className="amount">{formatAmount(snapshot.budget, snapshot.currency)}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
