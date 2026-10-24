import { useTranslation } from 'react-i18next';
import type { Agency } from '@/features/organization/types';
import { formatDayShort, formatDayTimeShort } from '@/shared/format/dates';
import { formatAmount, formatPercent, unitOf } from '@/shared/format/money';
import { HistoryChart } from '@/shared/ui/HistoryChart';
import { historySeries } from '@/shared/ui/historySeries';
import type { BalanceSnapshot } from '../types';

// Bilans enregistrés (30 derniers jours), par caisse dans sa devise (jamais deux devises sur un même graphique) :
// courbe du résultat net (maquettes 12 / D5, à partir de deux jours ; un jour sans capital saisi n'y figure pas),
// puis la liste, plus récent d'abord : jour, heure, résultat net et rapport au capital (« — » sans capital).

export function BalanceHistory({ snapshots, agencies }: { snapshots: BalanceSnapshot[]; agencies: Agency[] }) {
  const { t } = useTranslation();
  return (
    <section className="flex flex-col gap-3" aria-label={t('balance.history.title')}>
      <h2 className="m-0 text-base font-bold">{t('balance.history.title')}</h2>
      <div className="grid gap-4 lg:grid-cols-2">
        {agencies.map((agency) => {
          const own = snapshots.filter((s) => s.agencyId === agency.id).sort((a, b) => b.date.localeCompare(a.date));
          const name = t(`agencies.${agency.code}`, { defaultValue: agency.name });
          const currency = own[0]?.currency;
          return (
            <div key={agency.id} className="flex flex-col gap-1.5">
              <h3 className="m-0 text-sm font-semibold text-muted">{t('balance.caisse', { name })}</h3>
              {currency && (
                <HistoryChart
                  title={`${t('balance.caisse', { name })} · ${t('balance.history.chartTitle', { unit: unitOf(currency) })}`}
                  points={historySeries(own, (s) => s.date, (s) => s.netResult)}
                  formatValue={(value) => formatAmount(value, currency, { signed: true, maxDecimals: 0 })}
                />
              )}
              {own.length === 0 ? (
                <p className="m-0 text-sm text-muted">{t('balance.history.empty')}</p>
              ) : (
                <ul className="m-0 flex list-none flex-col rounded-2xl border border-border bg-surface p-0" aria-label={t('balance.caisse', { name })}>
                  {own.map((s) => (
                    <li key={s.id} className="flex items-center justify-between gap-3 border-b border-border px-3.5 py-2.5 last:border-b-0">
                      <span className="flex flex-col">
                        <span className="font-semibold tabular-nums">{formatDayShort(s.date)}</span>
                        <span className="text-sm text-muted">{formatDayTimeShort(s.savedAt).slice(-5)}</span>
                      </span>
                      <span className="flex flex-col items-end">
                        <span className="amount">{s.netResult === null ? '—' : formatAmount(s.netResult, s.currency, { signed: true, maxDecimals: 0 })}</span>
                        {s.netResultPercent !== null && <span className="text-sm text-muted">{formatPercent(s.netResultPercent)}</span>}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          );
        })}
      </div>
    </section>
  );
}
