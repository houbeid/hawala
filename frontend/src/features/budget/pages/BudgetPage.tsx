import { useNavigate, useSearch } from '@tanstack/react-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { usePermission } from '@/shared/auth/hooks';
import { AgencyChoice } from '@/shared/agency/AgencyChoice';
import { useAgencyScope, type AgencyRef } from '@/shared/agency/agencyScope';
import { formatDayTimeShort, todayIso } from '@/shared/format/dates';
import { formatAmount } from '@/shared/format/money';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { snapshotOf } from '../budgetRules';
import { BudgetLines } from '../components/BudgetLines';
import { CoherencePanel } from '../components/CoherencePanel';
import { SnapshotHistory } from '../components/SnapshotHistory';
import { useBudgetSnapshots, useCashBudget, useCoherenceCheck, useSaveCashBudget } from '../queries';
import type { CashBudgetSnapshot } from '../types';

// Budget de la caisse (maquette 10 ; skill « Budget de la caisse ») : budget en direct = espèces + banques + soldes
// débiteurs − soldes créditeurs − transferts non livrés, dans la devise de l'agence ; chaque ligne s'ouvre sur son
// détail. Le contrôle de cohérence liste les alertes depuis le dernier budget enregistré, sans blocage.
// « Enregistrer le budget du jour » (cash.budget.save : Superviseur, Caissier) : confirmation qui rappelle le nombre
// d'alertes ; le serveur calcule les chiffres ; réenregistrer le même jour remplace la photo (règle validée).
// Une caisse = une agence : sur « les deux agences », le Superviseur choisit la caisse. Enregistrer exige le réseau.

export function BudgetPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const search = useSearch({ strict: false }) as { agencyId?: string };
  const { scope } = useAgencyScope();
  const canSave = usePermission('cash.budget.save');
  const online = useOnlineStatus();

  const choices: AgencyRef[] = scope.kind === 'all' ? scope.agencies : [];
  const agency: AgencyRef | undefined = scope.kind === 'all' ? (choices.find((a) => a.id === search.agencyId) ?? choices[0]) : scope.agency;

  const budget = useCashBudget(agency?.id);
  const coherence = useCoherenceCheck(agency?.id);
  const snapshots = useBudgetSnapshots(agency?.id);
  const save = useSaveCashBudget();
  const [confirming, setConfirming] = useState(false);
  const [saved, setSaved] = useState<CashBudgetSnapshot | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const today = todayIso();
  const savedToday = snapshots.data ? snapshotOf(snapshots.data, today) : null;
  const alertCount = coherence.data?.alerts.length ?? 0;

  async function confirm() {
    if (!agency) return;
    setSaveError(null);
    try {
      setSaved(await save.mutateAsync(agency.id));
      setConfirming(false);
    } catch (error) {
      setSaveError(errorMessage(i18n, error));
    }
  }

  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-4">
      {choices.length > 1 && (
        <AgencyChoice
          label={t('budget.agencyChoice')}
          agencies={choices}
          selectedId={agency?.id}
          onSelect={(id) => {
            setSaved(null);
            void navigate({ to: '/budget', search: { agencyId: id }, replace: true });
          }}
        />
      )}

      {budget.isPending && agency && (
        <div role="status" className="flex flex-col gap-2">
          <div className="h-24 animate-pulse rounded-2xl bg-surface-2" />
          <div className="h-14 animate-pulse rounded-2xl bg-surface-2" />
          <span className="sr-only">{t('common.loading')}</span>
        </div>
      )}
      {budget.isError && (
        <div className="flex flex-col gap-3">
          <Notice tone="error">{errorMessage(i18n, budget.error)}</Notice>
          <Button icon={<Icon name="sync" />} onClick={() => void budget.refetch()}>
            {t('common.retry')}
          </Button>
        </div>
      )}

      {budget.data && (
        <>
          <section className="flex flex-col gap-1 rounded-2xl border border-border bg-surface p-4" aria-label={t('budget.total')}>
            <span className="text-sm text-muted">{t('budget.total')}</span>
            <span className={`amount text-[34px] leading-tight ${budget.data.budget < 0 ? 'text-red' : ''}`}>
              {formatAmount(budget.data.budget, budget.data.currency)}
            </span>
            <span className="text-sm text-muted">{t('budget.formula')}</span>
          </section>
          <BudgetLines budget={budget.data} />
        </>
      )}

      {coherence.data && agency?.currency && <CoherencePanel check={coherence.data} currency={agency.currency} />}
      {coherence.isError && <Notice tone="error">{errorMessage(i18n, coherence.error)}</Notice>}

      {saved && (
        <Notice tone="info">
          {t('budget.save.done', { time: formatDayTimeShort(saved.savedAt), amount: formatAmount(saved.budget, saved.currency) })}
        </Notice>
      )}

      {canSave &&
        budget.data &&
        (online ? (
          <div className="flex flex-col gap-1.5">
            {savedToday && !saved && (
              <p className="m-0 text-sm text-muted">{t('budget.save.alreadyToday', { time: formatDayTimeShort(savedToday.savedAt) })}</p>
            )}
            <Button variant="primary" fullWidth icon={<Icon name="check" />} onClick={() => setConfirming(true)}>
              {t('budget.save.open')}
            </Button>
          </div>
        ) : (
          <Notice tone="info">{t('budget.offline')}</Notice>
        ))}

      {snapshots.data && <SnapshotHistory snapshots={snapshots.data} />}

      {confirming && budget.data && (
        <Sheet open onOpenChange={(value) => !value && setConfirming(false)} title={t('budget.save.title')} description={t('budget.save.hint')}>
          <div className="flex flex-col gap-3">
            {saveError && <Notice tone="error">{saveError}</Notice>}
            <dl className="m-0 grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 rounded-xl bg-surface-2 p-3">
              <dt className="text-sm text-muted">{t('budget.save.budget')}</dt>
              <dd className="amount m-0 text-end">{formatAmount(budget.data.budget, budget.data.currency)}</dd>
              <dt className="text-sm text-muted">{t('budget.save.alerts')}</dt>
              <dd className="m-0 text-end font-semibold">{t('budget.save.alertCount', { count: alertCount })}</dd>
            </dl>
            {savedToday && <p className="m-0 text-sm">{t('budget.save.replaces')}</p>}
            <Button variant="primary" fullWidth busy={save.isPending} busyLabel={t('budget.save.saving')} onClick={() => void confirm()}>
              {alertCount > 0 ? t('budget.save.confirmAnyway') : t('budget.save.confirm')}
            </Button>
            <Button fullWidth onClick={() => setConfirming(false)}>
              {t('budget.save.back')}
            </Button>
          </div>
        </Sheet>
      )}
    </div>
  );
}
