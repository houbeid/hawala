import { useTranslation } from 'react-i18next';
import { formatDayShort, formatDayTimeShort } from '@/shared/format/dates';
import { formatAmount, formatPercent, formatRate } from '@/shared/format/money';
import { ExpandChevron } from '@/shared/ui/ExpandChevron';
import { HistoryChart } from '@/shared/ui/HistoryChart';
import { historySeries } from '@/shared/ui/historySeries';
import { SUMMARY_WITHOUT_MARKER } from '@/shared/ui/summary';
import { partiesResultOf, treasuryOf } from '../generalBalance';
import type { GeneralBalanceSnapshot } from '../types';

// Bilans généraux sauvegardés (30 derniers jours ; demande du 31 octobre 2026) : la courbe du bilan général (une série, MRO),
// puis la liste, plus récent d'abord. Chaque photo donne son jour et son heure, le bilan général, le résultat net et le taux
// du bilan, et se déplie sur ses totaux en MRO au taux de la photo : trésorerie toutes caisses, créances, non livrés, dettes,
// résultat tiers. Jamais le détail par tiers : l'écran Tiers le donne déjà.

const mro = (value: number, signed = false) => formatAmount(value, 'MRO', { signed, maxDecimals: 0 });

export function GeneralBalanceHistory({ snapshots }: { snapshots: GeneralBalanceSnapshot[] }) {
  const { t } = useTranslation();
  const recentFirst = [...snapshots].sort((a, b) => b.date.localeCompare(a.date));
  return (
    <section className="flex flex-col gap-3" aria-label={t('balance.general.history.title')}>
      <h2 className="m-0 text-base font-bold">{t('balance.general.history.title')}</h2>
      <HistoryChart
        title={t('balance.general.history.chartTitle')}
        points={historySeries(snapshots, (s) => s.date, (s) => s.generalBalance)}
        formatValue={(value) => mro(value)}
      />
      {recentFirst.length === 0 ? (
        <p className="m-0 text-sm text-muted">{t('balance.history.empty')}</p>
      ) : (
        <ul className="m-0 flex list-none flex-col gap-2 p-0" aria-label={t('balance.general.history.list')}>
          {recentFirst.map((s) => (
            <li key={s.id}>
              <details className="group rounded-2xl border border-border bg-surface">
                <summary className={`flex items-start gap-3 px-3.5 py-3 ${SUMMARY_WITHOUT_MARKER}`}>
                  <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                    <span className="font-semibold tabular-nums">
                      {formatDayShort(s.date)} · {formatDayTimeShort(s.savedAt).slice(-5)}
                    </span>
                    <span className="text-sm text-muted">
                      {s.netResult === null
                        ? t('balance.general.history.noResult')
                        : t('balance.general.history.netResult', {
                            amount: mro(s.netResult, true),
                            percent: s.netResultPercent === null ? '' : `(${formatPercent(s.netResultPercent)})`,
                          })}
                    </span>
                    <span className="text-sm text-muted">{formatRate(s.rate)}</span>
                  </span>
                  <span className="amount text-lg font-bold">{mro(s.generalBalance)}</span>
                  <ExpandChevron className="mt-1" />
                </summary>
                <dl className="m-0 flex flex-col gap-1.5 border-t border-border px-3.5 py-2.5">
                  <Line label={t('balance.general.treasury')} value={mro(treasuryOf(s.total))} />
                  <Line label={t('balance.general.components.debtors')} value={mro(s.total.debtors)} />
                  <Line label={t('balance.general.components.undelivered')} value={mro(s.total.undelivered)} />
                  <Line label={t('balance.general.components.creditors')} value={mro(s.total.creditors)} />
                  <Line label={t('balance.general.partiesResult')} value={mro(partiesResultOf(s.total), true)} />
                </dl>
              </details>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function Line({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-wrap items-baseline justify-between gap-2">
      <dt className="text-sm text-muted">{label}</dt>
      <dd className="amount m-0">{value}</dd>
    </div>
  );
}
