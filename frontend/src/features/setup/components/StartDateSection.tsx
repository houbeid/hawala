import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { usePermission, useSessionManager } from '@/shared/auth/hooks';
import { formatDayLong, isIsoDay, todayIso } from '@/shared/format/dates';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { getStartDate, setStartDate } from '../api';
import { startDateKey } from '../queries';

// Date de démarrage (phase 8, décision du 24 octobre 2026) : fixée une seule fois par le Superviseur (balance.manage),
// jamais future ; ensuite, toute opération datée avant est refusée par le serveur (période de l'ancienne app fermée).
// À fixer AVANT les imports : les soldes initiaux et les reprises sont datés de ce jour.

export function StartDateSection() {
  const { t, i18n } = useTranslation();
  const session = useSessionManager();
  const queryClient = useQueryClient();
  const canSet = usePermission('balance.manage');
  const today = todayIso();
  const [date, setDate] = useState(today);
  const [confirming, setConfirming] = useState(false);
  const current = useQuery({ queryKey: startDateKey, queryFn: () => getStartDate(session.http) });
  const save = useMutation({
    mutationFn: (day: string) => setStartDate(session.http, day),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: startDateKey }),
  });

  const fixed = current.data?.startDate ?? null;
  return (
    <section className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4" aria-label={t('setup.startDate.title')}>
      <h2 className="m-0 text-lg font-bold">{t('setup.startDate.title')}</h2>
      {current.isPending && <span className="text-muted">{t('common.loading')}</span>}
      {fixed ? (
        <Notice tone="info">{t('setup.startDate.fixed', { date: formatDayLong(fixed, i18n.language) })}</Notice>
      ) : current.isSuccess ? (
        <>
          <p className="m-0">{t('setup.startDate.hint')}</p>
          {canSet ? (
            <div className="flex flex-wrap items-end gap-2">
              <label className="flex flex-col gap-1.5">
                <span className="text-sm font-semibold text-muted">{t('setup.startDate.date')}</span>
                <input
                  type="date"
                  value={date}
                  max={today}
                  onChange={(e) => setDate(isIsoDay(e.target.value) ? e.target.value : today)}
                  className="min-h-14 rounded-xl border-[1.5px] border-border bg-surface px-3.5 text-[17px] text-fg"
                />
              </label>
              <Button icon={<Icon name="lock" />} onClick={() => setConfirming(true)}>
                {t('setup.startDate.set')}
              </Button>
            </div>
          ) : (
            <span className="text-muted">{t('setup.startDate.supervisorOnly')}</span>
          )}
        </>
      ) : null}
      {save.isError && <Notice tone="error">{errorMessage(i18n, save.error)}</Notice>}

      {confirming && (
        <Sheet open onOpenChange={(open) => !open && setConfirming(false)} title={t('setup.startDate.confirmTitle', { date: formatDayLong(date, i18n.language) })}>
          <div className="flex flex-col gap-3">
            <Notice tone="warning">{t('setup.startDate.confirmHint')}</Notice>
            <Button
              variant="primary"
              fullWidth
              busy={save.isPending}
              busyLabel={t('setup.busy')}
              onClick={() => {
                setConfirming(false);
                save.mutate(date);
              }}
            >
              {t('setup.startDate.set')}
            </Button>
            <Button fullWidth onClick={() => setConfirming(false)}>
              {t('setup.cancel')}
            </Button>
          </div>
        </Sheet>
      )}
    </section>
  );
}
