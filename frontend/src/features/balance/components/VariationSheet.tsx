import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { formatDayShort, formatDayTimeShort } from '@/shared/format/dates';
import { formatAmount, formatNumber } from '@/shared/format/money';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { useGeneralVariation } from '../queries';
import type { ComponentVariation } from '../types';

// « Expliquer la variation » (décision du 31 octobre 2026) : d'où vient l'écart entre la dernière photo du bilan général
// et maintenant, au taux affiché. Une ligne par composant qui a bougé (effet signé en MRO, puis Nouakchott en MRO et Dakar
// en FCFA avec son équivalent), l'effet du seul changement de taux, et le total, qui redonne exactement l'écart. Puis le
// capital, les apports du patron et le résultat net. Totaux seulement : le détail par tiers est sur l'écran Tiers.

const mro = (value: number) => formatAmount(value, 'MRO', { signed: true, maxDecimals: 0 });

const componentKey = (c: ComponentVariation) =>
  (c.component.charAt(0).toLowerCase() + c.component.slice(1)) as 'cash' | 'banks' | 'debtors' | 'creditors' | 'undelivered';

export function VariationSheet({ rate, onClose }: { rate: number | null; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const variation = useGeneralVariation(rate, true);
  const data = variation.data;
  const moved = data?.components.filter((c) => c.nouakchottChange !== 0 || c.dakarChange !== 0) ?? [];

  return (
    <Sheet
      open
      onOpenChange={(value) => !value && onClose()}
      title={t('balance.general.variation.title')}
      description={data ? t('balance.general.variation.since', { day: formatDayShort(data.fromDate), time: formatDayTimeShort(data.fromSavedAt).slice(-5) }) : undefined}
    >
      <div className="flex flex-col gap-3">
        {variation.isPending && <p className="m-0 text-muted">{t('common.loading')}</p>}
        {variation.isError && <Notice tone="info">{errorMessage(i18n, variation.error)}</Notice>}
        {data && (
          <>
            <dl className="m-0 flex flex-col gap-2 rounded-xl bg-surface-2 p-3">
              <Line label={t('balance.general.variation.before')} value={formatAmount(data.fromGeneralBalance, 'MRO', { maxDecimals: 0 })} />
              <Line label={t('balance.general.variation.now')} value={formatAmount(data.toGeneralBalance, 'MRO', { maxDecimals: 0 })} />
              <Line label={t('balance.general.variation.change')} value={mro(data.generalBalanceChange)} strong />
            </dl>

            <section className="flex flex-col gap-2" aria-label={t('balance.general.variation.why')}>
              <h3 className="m-0 text-sm font-semibold text-muted">{t('balance.general.variation.why')}</h3>
              {moved.length === 0 && data.rateEffect === 0 && <p className="m-0 text-sm">{t('balance.general.variation.nothing')}</p>}
              <ul className="m-0 flex list-none flex-col gap-2 p-0">
                {moved.map((c) => (
                  <li key={c.component} className="flex flex-col gap-0.5 rounded-xl border border-border px-3 py-2">
                    <span className="flex flex-wrap items-baseline justify-between gap-2">
                      <span className="font-semibold">{t(`balance.general.components.${componentKey(c)}`)}</span>
                      <span className="amount">{mro(c.effect)}</span>
                    </span>
                    <span className="text-sm text-muted">
                      {t('balance.general.variation.split', {
                        nouakchott: mro(c.nouakchottChange),
                        dakar: formatAmount(c.dakarChange, 'XOF', { signed: true }),
                        dakarMro: mro(c.dakarChangeMro),
                      })}
                    </span>
                  </li>
                ))}
                {data.rateEffect !== 0 && (
                  <li className="flex flex-col gap-0.5 rounded-xl border border-border px-3 py-2">
                    <span className="flex flex-wrap items-baseline justify-between gap-2">
                      <span className="font-semibold">{t('balance.general.variation.rateEffect')}</span>
                      <span className="amount">{mro(data.rateEffect)}</span>
                    </span>
                    <span className="text-sm text-muted">
                      {t('balance.general.variation.rates', { from: formatNumber(data.fromRate), to: formatNumber(data.toRate) })}
                    </span>
                  </li>
                )}
              </ul>
            </section>

            <dl className="m-0 flex flex-col gap-2 rounded-xl bg-surface-2 p-3">
              {data.capitalChange !== null && <Line label={t('balance.general.capital')} value={mro(data.capitalChange)} />}
              <Line label={t('balance.general.ownerContributions')} value={mro(data.ownerContributionsChange)} />
              {data.netResultChange !== null && <Line label={t('balance.netResult')} value={mro(data.netResultChange)} strong />}
            </dl>
          </>
        )}
      </div>
    </Sheet>
  );
}

function Line({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex flex-wrap items-baseline justify-between gap-2">
      <dt className={strong ? 'font-semibold' : 'text-sm text-muted'}>{label}</dt>
      <dd className={`amount m-0 ${strong ? 'text-lg' : ''}`}>{value}</dd>
    </div>
  );
}
