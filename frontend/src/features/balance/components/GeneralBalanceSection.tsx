import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useDailyRate } from '@/features/rates/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { formatDayTimeShort, todayIso } from '@/shared/format/dates';
import { toInputText } from '@/shared/format/decimalInput';
import { formatAmount, formatPercent, formatRate } from '@/shared/format/money';
import { Button } from '@/shared/ui/Button';
import { ExpandChevron } from '@/shared/ui/ExpandChevron';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { RateField } from '@/shared/ui/RateField';
import { Sheet } from '@/shared/ui/Sheet';
import { SUMMARY_WITHOUT_MARKER } from '@/shared/ui/summary';
import { balanceRateError, COMPONENT_ROWS, parseBalanceRate, partiesResultOf, treasuryOf } from '../generalBalance';
import { useGeneralBalance, useGeneralSnapshots, useSaveGeneralBalance } from '../queries';
import type { GeneralBalanceSnapshot, GeneralBalanceView } from '../types';
import { GeneralCapitalSheet } from './GeneralCapitalSheet';
import { VariationSheet } from './VariationSheet';

// Bilan général (décisions du 31 octobre 2026, sur sa propre page) : Nouakchott en MRO
// + Dakar converti au taux du bilan. Le taux est prérempli avec le prix d'achat du jour et modifiable ; le calcul (lourd)
// part au chargement, puis seulement avec « Actualiser » (ou Entrée), jamais à chaque chiffre tapé. Résultat net = bilan
// général − (capital général + apports nets du patron). Totaux seulement : le détail par tiers est sur l'écran Tiers.
// Sauvegarder et saisir le capital : balance.manage, en ligne ; « Expliquer la variation » (lecture) : en ligne.

interface Props {
  manageable: boolean;
  online: boolean;
  dakarName: string;
  nouakchottName: string;
}

const mro = (value: number, signed = false) => formatAmount(value, 'MRO', { signed, maxDecimals: 0 });

export function GeneralBalanceSection({ manageable, online, dakarName, nouakchottName }: Props) {
  const { t, i18n } = useTranslation();
  const dayRate = useDailyRate(todayIso());
  const purchase = dayRate.data?.purchaseRate ?? null;
  const defaultText = purchase === null ? '' : toInputText(purchase);
  // Tant que l'agent n'a rien tapé ni appliqué, le prix d'achat du jour fait foi.
  const [edited, setEdited] = useState<string | null>(null);
  const [applied, setApplied] = useState<number | null | undefined>(undefined);
  const [rateError, setRateError] = useState<string | null>(null);
  const rateText = edited ?? defaultText;
  const appliedRate = applied === undefined ? parseBalanceRate(defaultText) : applied;

  const general = useGeneralBalance(appliedRate);
  const snapshots = useGeneralSnapshots();
  const save = useSaveGeneralBalance();
  const [confirming, setConfirming] = useState(false);
  const [capitalOpen, setCapitalOpen] = useState(false);
  const [variationOpen, setVariationOpen] = useState(false);
  const [saved, setSaved] = useState<GeneralBalanceSnapshot | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const data = general.data;
  const savedToday = data ? snapshots.data?.some((s) => s.date === data.date) : false;

  function apply(event: FormEvent) {
    event.preventDefault();
    const error = balanceRateError(rateText);
    setRateError(error);
    if (error) return;
    const rate = parseBalanceRate(rateText);
    if (rate === appliedRate) void general.refetch();
    else setApplied(rate);
  }

  async function confirmSave(view: GeneralBalanceView) {
    setSaveError(null);
    try {
      setSaved(await save.mutateAsync(view.rate));
      setConfirming(false);
    } catch (error) {
      setSaveError(errorMessage(i18n, error));
    }
  }

  return (
    <section className="flex flex-col gap-4 rounded-2xl border border-border bg-surface p-4" aria-label={t('balance.general.title')}>
      <form noValidate onSubmit={apply} className="flex flex-wrap items-end gap-2">
        <div className="min-w-60 flex-1">
          <RateField
            label={t('balance.general.rate')}
            value={rateText}
            onChange={(event) => setEdited(event.target.value)}
            error={rateError ? t(rateError) : undefined}
          />
        </div>
        <Button type="submit" icon={<Icon name="sync" spin={general.isFetching} />} disabled={general.isFetching}>
          {t('balance.general.apply')}
        </Button>
      </form>
      <p className="m-0 -mt-2 text-sm text-muted">
        {purchase === null ? t('balance.general.rateNone') : t('balance.general.rateHint', { rate: formatRate(purchase) })}
      </p>

      {general.isPending && appliedRate !== null && (
        <div role="status" className="h-40 animate-pulse rounded-2xl bg-surface-2">
          <span className="sr-only">{t('common.loading')}</span>
        </div>
      )}
      {general.isError && <Notice tone="error">{errorMessage(i18n, general.error)}</Notice>}
      {saved && <Notice tone="info">{t('balance.general.save.done', { time: formatDayTimeShort(saved.savedAt) })}</Notice>}

      {data && (
        <>
          <div className="flex flex-wrap items-end justify-between gap-4">
            <div className="flex flex-col gap-1">
              <span className="text-sm text-muted">{t('balance.general.total')}</span>
              <span className="amount text-[34px] leading-tight font-bold">{mro(data.generalBalance)}</span>
              <span className="text-xs text-muted">{t('balance.general.formula')}</span>
            </div>
            <dl className="m-0 flex min-w-60 flex-col gap-1.5 rounded-xl bg-surface-2 p-3">
              <Row label={t('balance.general.capital')} value={data.capital === null ? t('balance.notSet') : mro(data.capital)} />
              <Row label={t('balance.general.ownerContributions')} value={mro(data.ownerContributionsMro, true)} />
              {data.capitalUsed !== null && <Row label={t('balance.capitalUsed')} value={mro(data.capitalUsed)} />}
              {data.netResult !== null ? (
                <>
                  <Row label={t('balance.netResult')} value={mro(data.netResult, true)} strong />
                  {data.netResultPercent !== null && <Row label={t('balance.percent')} value={formatPercent(data.netResultPercent)} />}
                </>
              ) : (
                <p className="m-0 text-sm font-semibold">{t('balance.general.noCapital')}</p>
              )}
            </dl>
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
            <Tile label={t('balance.general.treasury')} value={mro(treasuryOf(data.total))} />
            <Tile label={t('balance.general.components.undelivered')} value={mro(data.total.undelivered)} />
            <Tile label={t('balance.general.partiesResult')} value={mro(partiesResultOf(data.total), true)} />
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <Tile label={t('balance.general.caisseBalance', { name: nouakchottName })} value={mro(data.nouakchott.budget)} />
            <Tile
              label={t('balance.general.caisseBalance', { name: dakarName })}
              value={formatAmount(data.dakar.budget, 'XOF')}
              detail={t('balance.general.inMro', { amount: mro(data.dakarInMro.budget) })}
            />
          </div>

          <section className="flex flex-col gap-1.5" aria-label={t('balance.general.details')}>
            <h3 className="m-0 text-sm font-semibold text-muted">{t('balance.general.details')}</h3>
            {COMPONENT_ROWS.map(({ key, sign }) => (
              <details key={key} className="group rounded-xl border border-border bg-surface">
                <summary className={`flex min-h-12 items-center gap-3 px-3.5 ${SUMMARY_WITHOUT_MARKER}`}>
                  <span className="flex-1">
                    <span aria-hidden="true" className="me-2 text-muted">{sign}</span>
                    {t(`balance.general.components.${key}`)}
                  </span>
                  <span className="amount">{mro(data.total[key])}</span>
                  <ExpandChevron />
                </summary>
                <dl className="m-0 flex flex-col gap-1.5 border-t border-border px-3.5 py-2.5">
                  <Row label={nouakchottName} value={mro(data.nouakchott[key])} />
                  <Row
                    label={dakarName}
                    value={`${formatAmount(data.dakar[key], 'XOF')} · ${t('balance.general.inMro', { amount: mro(data.dakarInMro[key]) })}`}
                  />
                </dl>
              </details>
            ))}
          </section>

          <dl className="m-0 flex flex-col gap-1">
            <Row label={t('balance.general.savedProfits')} value={mro(data.savedProfitsMro)} />
            <p className="m-0 text-xs text-muted">{t('balance.general.savedProfitsHint')}</p>
          </dl>

          {online && (
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
              {manageable && (
                <>
                  <Button
                    variant="primary"
                    icon={<Icon name="check" />}
                    onClick={() => {
                      setSaveError(null);
                      setConfirming(true);
                    }}
                  >
                    {t('balance.general.save.open')}
                  </Button>
                  <Button icon={<Icon name="pencil" />} onClick={() => setCapitalOpen(true)}>
                    {t('balance.general.capitalSheet.open')}
                  </Button>
                </>
              )}
              <Button icon={<Icon name="trend" />} onClick={() => setVariationOpen(true)}>
                {t('balance.general.variation.open')}
              </Button>
            </div>
          )}

          {confirming && (
            <Sheet
              open
              onOpenChange={(value) => !value && setConfirming(false)}
              title={t('balance.general.save.title')}
              description={t('balance.general.save.hint')}
            >
              <div className="flex flex-col gap-3">
                {saveError && <Notice tone="error">{saveError}</Notice>}
                <dl className="m-0 flex flex-col gap-2 rounded-xl bg-surface-2 p-3">
                  <Row label={t('balance.general.rate')} value={formatRate(data.rate)} />
                  <Row label={t('balance.general.total')} value={mro(data.generalBalance)} />
                  <Row label={t('balance.netResult')} value={data.netResult === null ? t('balance.notSet') : mro(data.netResult, true)} />
                  {data.netResultPercent !== null && <Row label={t('balance.percent')} value={formatPercent(data.netResultPercent)} />}
                </dl>
                {savedToday && <p className="m-0 text-sm">{t('balance.general.save.replaces')}</p>}
                <Button variant="primary" fullWidth busy={save.isPending} busyLabel={t('balance.saving')} onClick={() => void confirmSave(data)}>
                  {t('balance.save.confirm')}
                </Button>
                <Button fullWidth onClick={() => setConfirming(false)}>
                  {t('balance.save.back')}
                </Button>
              </div>
            </Sheet>
          )}
        </>
      )}
      {capitalOpen && <GeneralCapitalSheet onClose={() => setCapitalOpen(false)} />}
      {variationOpen && <VariationSheet rate={appliedRate} onClose={() => setVariationOpen(false)} />}
    </section>
  );
}

function Row({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex flex-wrap items-baseline justify-between gap-2">
      <dt className={strong ? 'font-semibold' : 'text-sm text-muted'}>{label}</dt>
      <dd className={`amount m-0 ${strong ? 'text-xl' : ''}`}>{value}</dd>
    </div>
  );
}

function Tile({ label, value, detail }: { label: string; value: string; detail?: string }) {
  return (
    <div className="flex flex-col gap-1 rounded-xl border border-border bg-surface-2 p-3">
      <span className="text-sm text-muted">{label}</span>
      <span className="amount text-xl font-bold">{value}</span>
      {detail && <span className="text-xs text-muted">{detail}</span>}
    </div>
  );
}
