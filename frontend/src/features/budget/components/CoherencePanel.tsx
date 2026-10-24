import { useTranslation } from 'react-i18next';
import type { Currency } from '@/features/organization/types';
import { formatDayTimeShort } from '@/shared/format/dates';
import { Icon } from '@/shared/ui/Icon';
import { alertDetail, groupAlerts } from '../budgetRules';
import type { CoherenceCheckView } from '../types';

// Contrôle de cohérence (maquette 10) : une famille par bloc, compteur dans un rond ambre + titre + chaque alerte
// (référence ou nom, montant ou taux, motif). Aucune alerte ne bloque l'enregistrement du budget.

export function CoherencePanel({ check, currency }: { check: CoherenceCheckView; currency: Currency }) {
  const { t } = useTranslation();
  const groups = groupAlerts(check.alerts);
  return (
    <section className="flex flex-col gap-2" aria-label={t('budget.coherence.title')}>
      <h2 className="m-0 text-base font-bold">{t('budget.coherence.title')}</h2>
      <p className="m-0 text-sm text-muted">{t('budget.coherence.since', { date: formatDayTimeShort(check.since) })}</p>
      {groups.length === 0 ? (
        <p className="m-0 flex items-center gap-2 rounded-2xl bg-surface-2 p-3.5 font-semibold">
          <Icon name="check" />
          {t('budget.coherence.none')}
        </p>
      ) : (
        <ul className="m-0 flex list-none flex-col gap-2 p-0">
          {groups.map((group) => (
            <li key={group.kind} className="flex gap-3 rounded-2xl border border-border bg-surface p-3.5">
              <span className="inline-flex size-8 shrink-0 items-center justify-center rounded-full border-[1.5px] border-warn-fg bg-warn-bg font-bold text-warn-fg">
                {group.alerts.length}
              </span>
              <div className="flex min-w-0 flex-1 flex-col gap-1">
                <h3 className="m-0 text-[15px] font-bold">{t(`budget.coherence.kinds.${group.kind}`)}</h3>
                <ul className="m-0 flex list-none flex-col gap-1 p-0 text-sm">
                  {group.alerts.map((alert, index) => {
                    const detail = alertDetail(alert, currency);
                    return (
                      <li key={`${alert.sourceId ?? 'x'}-${index}`} className="flex flex-col">
                        <span className="flex flex-wrap gap-x-2">
                          {detail.subject && <span className="break-words font-semibold">{detail.subject}</span>}
                          {detail.value && <span className="amount">{detail.value}</span>}
                          <span className="tabular-nums text-muted">{formatDayTimeShort(alert.at)}</span>
                        </span>
                        {detail.reason && <span className="text-muted">{t('budget.coherence.reason', { reason: detail.reason })}</span>}
                      </li>
                    );
                  })}
                </ul>
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
