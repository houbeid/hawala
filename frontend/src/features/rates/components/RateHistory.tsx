import { useTranslation } from 'react-i18next';
import { formatDayShort } from '@/shared/format/dates';
import { formatNumber } from '@/shared/format/money';
import type { DailyRate } from '../types';

// Historique (maquette 07) : une ligne par jour qui a un taux, du plus récent au plus ancien.
// La colonne « Achat » n'existe que si le compte voit le prix d'achat (profit.view).

export function RateHistory({ rates, selectedDay, onSelect }: { rates: DailyRate[]; selectedDay: string; onSelect: (day: string) => void }) {
  const { t } = useTranslation();
  const showPurchase = rates.some((rate) => rate.purchaseRate !== null);
  const sorted = [...rates].sort((a, b) => b.date.localeCompare(a.date));

  if (sorted.length === 0) return <p className="m-0 text-muted">{t('rates.historyEmpty')}</p>;

  return (
    <table className="w-full border-collapse overflow-hidden rounded-2xl border border-border bg-surface tabular-nums">
      <caption className="sr-only">{t('rates.history')}</caption>
      <thead>
        <tr className="bg-surface-2 text-xs font-bold tracking-wide text-muted uppercase">
          <th scope="col" className="px-3.5 py-2 text-start">
            {t('rates.date')}
          </th>
          {showPurchase && (
            <th scope="col" className="px-3.5 py-2 text-end">
              {t('rates.purchaseShort')}
            </th>
          )}
          <th scope="col" className="px-3.5 py-2 text-end">
            {t('rates.saleShort')}
          </th>
        </tr>
      </thead>
      <tbody>
        {sorted.map((rate) => {
          const selected = rate.date === selectedDay;
          return (
            <tr key={rate.date} className="border-t border-border" aria-current={selected ? 'date' : undefined}>
              <td className="px-3.5 py-0">
                <button
                  type="button"
                  onClick={() => onSelect(rate.date)}
                  className={`min-h-12 w-full text-start underline-offset-4 hover:underline ${selected ? 'font-bold' : ''}`}
                >
                  {formatDayShort(rate.date)}
                </button>
              </td>
              {showPurchase && (
                <td className="px-3.5 text-end">
                  <span className="amount text-[15px]">{rate.purchaseRate === null ? '—' : formatNumber(rate.purchaseRate)}</span>
                </td>
              )}
              <td className="px-3.5 text-end">
                <span className="amount text-[15px]">{formatNumber(rate.saleRate)}</span>
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
