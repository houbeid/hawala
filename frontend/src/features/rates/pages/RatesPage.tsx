import { useNavigate, useSearch } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { usePermission } from '@/shared/auth/hooks';
import { addDays, isIsoDay, todayIso } from '@/shared/format/dates';
import { formatRate } from '@/shared/format/money';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { DayNavigator } from '../components/DayNavigator';
import { RateEditor } from '../components/RateEditor';
import { RateHistory } from '../components/RateHistory';
import { useDailyRate, useRateHistory } from '../queries';
import type { DailyRate } from '../types';

// Écran « Taux du jour » (maquette 07). Deux versions :
// - avec rates.set_daily (Superviseur) : saisie des deux taux de la date choisie ;
// - sinon : lecture seule. Écart voulu avec la maquette (réservée au Superviseur) : le backend laisse tout
//   compte connecté lire le prix de vente, et le prix d'achat seulement avec profit.view.

/** Historique affiché : 30 jours avant aujourd'hui, et une semaine à l'avance (taux fixés d'avance). */
const HISTORY_PAST_DAYS = 30;
const HISTORY_FUTURE_DAYS = 7;

function RateReadOnly({ rate }: { rate: DailyRate | null }) {
  const { t } = useTranslation();
  if (!rate) return <Notice tone="warning">{t('rates.notSetReadOnly')}</Notice>;
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      <div className="flex flex-col gap-1 rounded-2xl border border-border bg-surface p-3.5">
        <span className="text-sm text-muted">{t('rates.saleLabel')}</span>
        <span className="amount text-xl">{formatRate(rate.saleRate)}</span>
      </div>
      {rate.purchaseRate !== null && (
        <div className="flex flex-col gap-1 rounded-2xl border border-border bg-surface p-3.5">
          <span className="text-sm text-muted">{t('rates.purchaseLabel')}</span>
          <span className="amount text-xl">{formatRate(rate.purchaseRate)}</span>
        </div>
      )}
    </div>
  );
}

export function RatesPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const search = useSearch({ strict: false }) as { date?: string };
  const canSet = usePermission('rates.set_daily');

  const today = todayIso();
  const day = isIsoDay(search.date) ? search.date : today;
  const rate = useDailyRate(day);
  const history = useRateHistory(addDays(today, -HISTORY_PAST_DAYS), addDays(today, HISTORY_FUTURE_DAYS));

  const goTo = (next: string) => void navigate({ to: '/rates', search: next === today ? {} : { date: next }, replace: true });

  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-4">
      <DayNavigator day={day} today={today} onChange={goTo} />

      {rate.isPending && (
        <div role="status" className="flex items-center gap-2 text-muted">
          <Icon name="sync" spin />
          {t('common.loading')}
        </div>
      )}
      {rate.isError && (
        <div className="flex flex-col gap-3">
          <Notice tone="error">{errorMessage(i18n, rate.error)}</Notice>
          <Button icon={<Icon name="sync" />} onClick={() => void rate.refetch()}>
            {t('common.retry')}
          </Button>
        </div>
      )}
      {rate.isSuccess &&
        (canSet ? (
          // Une autre date recharge le formulaire avec ses valeurs ; après un enregistrement, le formulaire reste
          // (et son message de confirmation aussi).
          <RateEditor key={day} day={day} rate={rate.data} />
        ) : (
          <RateReadOnly rate={rate.data} />
        ))}

      <section className="flex flex-col gap-2">
        <h2 className="m-0 text-sm font-semibold text-muted">{t('rates.history')}</h2>
        {history.isSuccess && <RateHistory rates={history.data} selectedDay={day} onSelect={goTo} />}
        {history.isError && <Notice tone="error">{errorMessage(i18n, history.error)}</Notice>}
      </section>
    </div>
  );
}
