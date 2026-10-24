import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { usePermission } from '@/shared/auth/hooks';
import { formatDayLong, formatDayTimeShort } from '@/shared/format/dates';
import { formatAmount, formatPercent } from '@/shared/format/money';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { BalanceHistory } from '../components/BalanceHistory';
import { CaisseBalanceCard } from '../components/CaisseBalanceCard';
import { CapitalSheet } from '../components/CapitalSheet';
import { useBalance, useBalanceSnapshots, useSaveBalance } from '../queries';
import type { BalanceSnapshot } from '../types';

// Bilan par caisse (maquettes 12 / D5 ; décision du 21 octobre 2026 : « 2 caisses, 2 capitaux ») : balance.view.
// Les deux caisses côte à côte, chacune dans sa devise ; résultat net = budget de la caisse − (capital initial + apports
// nets du patron). Écarts voulus avec les maquettes : ni « Taux du bilan » ni page « Bilan général » converti en MRO
// (abandonnés : le taux ferait varier le total sans aucune opération). Calcul lourd : jamais à l'ouverture de l'app,
// seulement sur cet écran, avec « Actualiser ». Enregistrer le bilan et saisir le capital : balance.manage, en ligne.

export function BalancePage() {
  const { t, i18n } = useTranslation();
  const canManage = usePermission('balance.manage');
  const online = useOnlineStatus();
  const balance = useBalance();
  const snapshots = useBalanceSnapshots();
  const agencies = useAgencies();
  const byId = useMemo(() => new Map((agencies.data ?? []).map((a) => [a.id, a])), [agencies.data]);
  const save = useSaveBalance();
  const [confirming, setConfirming] = useState<string | null>(null);
  const [capitalFor, setCapitalFor] = useState<string | null>(null);
  const [saved, setSaved] = useState<BalanceSnapshot | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const manageable = canManage && online;
  const data = balance.data;
  const name = (agencyId: string) => {
    const agency = byId.get(agencyId);
    return agency ? t(`agencies.${agency.code}`, { defaultValue: agency.name }) : '';
  };
  const confirmed = data?.caisses.find((c) => c.agencyId === confirming);
  const savedToday = (agencyId: string) => snapshots.data?.find((s) => s.agencyId === agencyId && s.date === data?.date) ?? null;
  const capitalCaisse = data?.caisses.find((c) => c.agencyId === capitalFor);

  async function confirm(agencyId: string) {
    setSaveError(null);
    try {
      setSaved(await save.mutateAsync(agencyId));
      setConfirming(null);
    } catch (error) {
      setSaveError(errorMessage(i18n, error));
    }
  }

  return (
    <div className="mx-auto flex w-full max-w-4xl flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="m-0 text-sm text-muted">{data ? t('balance.asOf', { date: formatDayLong(data.date, i18n.language) }) : ''}</p>
        <Button icon={<Icon name="sync" spin={balance.isFetching} />} onClick={() => void balance.refetch()} disabled={balance.isFetching}>
          {t('balance.refresh')}
        </Button>
      </div>
      {!online && <Notice tone="info">{t('balance.offline')}</Notice>}

      {balance.isPending && (
        <div role="status" className="grid gap-4 lg:grid-cols-2">
          <div className="h-80 animate-pulse rounded-2xl bg-surface-2" />
          <div className="h-80 animate-pulse rounded-2xl bg-surface-2" />
          <span className="sr-only">{t('common.loading')}</span>
        </div>
      )}
      {balance.isError && (
        <div className="flex flex-col gap-3">
          <Notice tone="error">{errorMessage(i18n, balance.error)}</Notice>
          <Button icon={<Icon name="sync" />} onClick={() => void balance.refetch()}>
            {t('common.retry')}
          </Button>
        </div>
      )}

      {saved && (
        <Notice tone="info">
          {t('balance.save.done', { name: name(saved.agencyId), time: formatDayTimeShort(saved.savedAt) })}
        </Notice>
      )}

      {data && (
        <div className="grid gap-4 lg:grid-cols-2">
          {data.caisses.map((view) => (
            <CaisseBalanceCard
              key={view.agencyId}
              view={view}
              agency={byId.get(view.agencyId)}
              manageable={manageable}
              onSave={() => {
                setSaveError(null);
                setConfirming(view.agencyId);
              }}
              onCapital={() => setCapitalFor(view.agencyId)}
            />
          ))}
        </div>
      )}

      {snapshots.data && agencies.data && <BalanceHistory snapshots={snapshots.data} agencies={agencies.data} />}

      {confirmed && (
        <Sheet
          open
          onOpenChange={(value) => !value && setConfirming(null)}
          title={t('balance.save.title', { name: name(confirmed.agencyId) })}
          description={t('balance.save.hint')}
        >
          <div className="flex flex-col gap-3">
            {saveError && <Notice tone="error">{saveError}</Notice>}
            <dl className="m-0 grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 rounded-xl bg-surface-2 p-3">
              <dt className="text-sm text-muted">{t('balance.budget')}</dt>
              <dd className="amount m-0 text-end">{formatAmount(confirmed.budget, confirmed.currency, { maxDecimals: 0 })}</dd>
              <dt className="text-sm text-muted">{t('balance.netResult')}</dt>
              <dd className="amount m-0 text-end">
                {confirmed.netResult === null ? t('balance.notSet') : formatAmount(confirmed.netResult, confirmed.currency, { signed: true, maxDecimals: 0 })}
              </dd>
              {confirmed.netResultPercent !== null && (
                <>
                  <dt className="text-sm text-muted">{t('balance.percent')}</dt>
                  <dd className="m-0 text-end tabular-nums">{formatPercent(confirmed.netResultPercent)}</dd>
                </>
              )}
            </dl>
            <p className="m-0 text-sm">{t('balance.save.separately')}</p>
            {savedToday(confirmed.agencyId) && <p className="m-0 text-sm">{t('balance.save.replaces')}</p>}
            <Button variant="primary" fullWidth busy={save.isPending} busyLabel={t('balance.saving')} onClick={() => void confirm(confirmed.agencyId)}>
              {t('balance.save.confirm')}
            </Button>
            <Button fullWidth onClick={() => setConfirming(null)}>
              {t('balance.save.back')}
            </Button>
          </div>
        </Sheet>
      )}
      {capitalCaisse && (
        <CapitalSheet
          agencyId={capitalCaisse.agencyId}
          agencyName={name(capitalCaisse.agencyId)}
          currency={capitalCaisse.currency}
          onClose={() => setCapitalFor(null)}
        />
      )}
    </div>
  );
}
