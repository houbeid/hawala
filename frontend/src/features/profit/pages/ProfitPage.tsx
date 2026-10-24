import { useNavigate, useSearch } from '@tanstack/react-router';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { scopedAgencyId, useAgencyScope } from '@/shared/agency/agencyScope';
import { formatDayShort, formatDayTimeShort, isIsoDay, todayIso } from '@/shared/format/dates';
import { formatAmount, formatRate } from '@/shared/format/money';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { ProfitDayRow, ProfitValue } from '../components/ProfitDayRow';
import { parseProfitSearch, periodDays, presets, type ProfitSearch } from '../profitRules';
import { useProfitReport, useSaveProfit } from '../queries';
import type { ProfitSnapshot } from '../types';

// Écran « Bénéfice » (maquettes 11 et D4 ; proposition validée le 21 octobre 2026) : profit.view (Superviseur).
// Période sur la date d'opération (aujourd'hui par défaut, raccourcis ou dates au choix, dans l'adresse) ; l'agence
// est celle de l'en-tête (« les deux agences » = bénéfice général) ; total de la période, une ligne par jour qui se
// déplie sur ses transferts. Bénéfice d'une agence = transferts qu'elle a envoyés ; annulés exclus, non livrés comptés.
// « Enregistrer le bénéfice du jour » : le jour courant seulement, remplacé s'il l'était déjà (exige le réseau).
// POINT OUVERT : bouton « Exporter » de la maquette D4 : la permission « export » et le format ne sont pas tranchés.

export function ProfitPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const search = parseProfitSearch(useSearch({ strict: false }) as Record<string, unknown>);
  const { scope } = useAgencyScope();
  const agencyId = scopedAgencyId(scope);
  const online = useOnlineStatus();
  const agencies = useAgencies();
  const agencyCodes = useMemo(() => new Map((agencies.data ?? []).map((a) => [a.id, a.code])), [agencies.data]);

  const today = todayIso();
  const from = search.from ?? today;
  const to = search.to ?? today;
  const options = presets(today);
  const active = options.find((p) => p.from === from && p.to === to)?.id;
  const report = useProfitReport({ agencyId, from: search.from, to: search.to });
  const save = useSaveProfit();
  const [confirming, setConfirming] = useState(false);
  const [saved, setSaved] = useState<ProfitSnapshot | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const setPeriod = (next: ProfitSearch) => {
    const clean: ProfitSearch = {};
    if (next.from && next.from !== today) clean.from = next.from;
    if (next.to && next.to !== today) clean.to = next.to;
    void navigate({ to: '/profit', search: clean as Record<string, unknown>, replace: true });
  };

  const data = report.data;
  const todayRow = data?.days.find((d) => d.date === today);

  async function confirm() {
    setSaveError(null);
    try {
      setSaved(await save.mutateAsync(agencyId ?? null));
      setConfirming(false);
    } catch (error) {
      setSaveError(errorMessage(i18n, error));
    }
  }

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2" role="group" aria-label={t('profit.period')}>
        {options.map((preset) => (
          <button
            key={preset.id}
            type="button"
            aria-pressed={active === preset.id}
            onClick={() => setPeriod({ from: preset.from, to: preset.to })}
            className="min-h-11 rounded-full border-[1.5px] border-border bg-surface px-3.5 text-[15px] font-semibold aria-pressed:border-ink aria-pressed:bg-ink aria-pressed:text-on-ink"
          >
            {t(`profit.presets.${preset.id}`)}
          </button>
        ))}
      </div>
      <div className="flex flex-wrap items-end gap-2">
        <label className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-muted">{t('profit.from')}</span>
          <input
            type="date"
            value={from}
            max={to}
            onChange={(e) => isIsoDay(e.target.value) && setPeriod({ from: e.target.value, to })}
            className="min-h-12 rounded-xl border-[1.5px] border-border bg-surface px-3 text-fg"
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-muted">{t('profit.to')}</span>
          <input
            type="date"
            value={to}
            min={from}
            max={today}
            onChange={(e) => isIsoDay(e.target.value) && setPeriod({ from, to: e.target.value })}
            className="min-h-12 rounded-xl border-[1.5px] border-border bg-surface px-3 text-fg"
          />
        </label>
        <span className="pb-3 text-sm text-muted">{agencyId ? t(`agencies.${agencyCodes.get(agencyId) ?? ''}`, { defaultValue: '' }) : t('profit.bothAgencies')}</span>
      </div>

      {report.isPending && (
        <div role="status" className="flex flex-col gap-2">
          <div className="h-24 animate-pulse rounded-2xl bg-surface-2" />
          <span className="sr-only">{t('common.loading')}</span>
        </div>
      )}
      {report.isError && (
        <div className="flex flex-col gap-3">
          <Notice tone="error">{errorMessage(i18n, report.error)}</Notice>
          <Button icon={<Icon name="sync" />} onClick={() => void report.refetch()}>
            {t('common.retry')}
          </Button>
        </div>
      )}

      {data && (
        <>
          <section
            className={`flex flex-col gap-1 rounded-2xl border border-border bg-surface p-4 ${report.isPlaceholderData ? 'opacity-60' : ''}`}
            aria-label={t('profit.total', { count: periodDays(data.from, data.to) })}
          >
            <span className="text-sm text-muted">{t('profit.total', { count: periodDays(data.from, data.to) })}</span>
            <ProfitValue value={data.totalProfitMro} size="xl" />
            <span className="text-sm text-muted">{t('profit.counted', { count: data.transferCount })}</span>
          </section>

          {saved && (
            <Notice tone="info">
              {t('profit.save.done', {
                time: formatDayTimeShort(saved.savedAt),
                amount: formatAmount(saved.profitMro, 'MRO', { signed: true, maxDecimals: 0 }),
              })}
            </Notice>
          )}
          {online ? (
            <Button variant="primary" icon={<Icon name="check" />} className="self-start" onClick={() => setConfirming(true)}>
              {t('profit.save.open')}
            </Button>
          ) : (
            <Notice tone="info">{t('profit.offline')}</Notice>
          )}

          {data.days.length === 0 ? (
            <p className="m-0 rounded-2xl bg-surface-2 p-6 text-center font-semibold">{t('profit.empty')}</p>
          ) : (
            <section className="flex flex-col gap-2" aria-label={t('profit.byDay')}>
              <h2 className="m-0 text-sm font-semibold text-muted">{t('profit.byDay')}</h2>
              <ul className="m-0 flex list-none flex-col gap-2 p-0">
                {[...data.days]
                  .sort((a, b) => b.date.localeCompare(a.date))
                  .map((day) => (
                    <ProfitDayRow key={day.date} day={day} agencyId={agencyId} agencyCodes={agencyCodes} isToday={day.date === today} />
                  ))}
              </ul>
            </section>
          )}
        </>
      )}

      {confirming && (
        <Sheet open onOpenChange={(value) => !value && setConfirming(false)} title={t('profit.save.title', { date: formatDayShort(today) })} description={t('profit.save.hint')}>
          <div className="flex flex-col gap-3">
            {saveError && <Notice tone="error">{saveError}</Notice>}
            {todayRow ? (
              <dl className="m-0 grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 rounded-xl bg-surface-2 p-3">
                <dt className="text-sm text-muted">{t('profit.save.count')}</dt>
                <dd className="m-0 text-end font-semibold">{todayRow.transferCount}</dd>
                <dt className="text-sm text-muted">{t('profit.save.rate')}</dt>
                <dd className="m-0 text-end tabular-nums">{todayRow.purchaseRate === null ? t('profit.severalRates') : formatRate(todayRow.purchaseRate)}</dd>
                <dt className="text-sm text-muted">{t('profit.save.profit')}</dt>
                <dd className="m-0 text-end">
                  <ProfitValue value={todayRow.profitMro} />
                </dd>
              </dl>
            ) : (
              <p className="m-0 text-[15px]">{t('profit.save.notInPeriod')}</p>
            )}
            {todayRow?.savedAt && <p className="m-0 text-sm">{t('profit.save.replaces', { time: formatDayTimeShort(todayRow.savedAt) })}</p>}
            <Button variant="primary" fullWidth busy={save.isPending} busyLabel={t('profit.save.saving')} onClick={() => void confirm()}>
              {t('profit.save.confirm')}
            </Button>
            <Button fullWidth onClick={() => setConfirming(false)}>
              {t('profit.save.back')}
            </Button>
          </div>
        </Sheet>
      )}
    </div>
  );
}
