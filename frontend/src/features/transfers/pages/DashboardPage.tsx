import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { usePermission } from '@/shared/auth/hooks';
import { addDays, formatDayShort, todayIso } from '@/shared/format/dates';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { CaisseCard } from '../components/CaisseCard';
import { useTransferDashboard } from '../queries';

// Tableau de bord (maquette 12) : page d'ouverture du Superviseur (décision du 22 octobre 2026), calcul
// léger limité aux transferts de la période. Le bilan par caisse, lourd, n'est JAMAIS calculé ici :
// seulement un lien vers son écran. Les deux caisses côte à côte, chacune dans sa devise.

type PresetId = 'today' | 'yesterday' | 'last7' | 'month';

function presets(today: string): { id: PresetId; from: string; to: string }[] {
  return [
    { id: 'today', from: today, to: today },
    { id: 'yesterday', from: addDays(today, -1), to: addDays(today, -1) },
    { id: 'last7', from: addDays(today, -6), to: today },
    { id: 'month', from: `${today.slice(0, 8)}01`, to: today },
  ];
}

export function DashboardPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const search = useSearch({ strict: false }) as { from?: string; to?: string };
  const canSeeBalance = usePermission('balance.view');
  const agencies = useAgencies();
  const byId = useMemo(() => new Map((agencies.data ?? []).map((a) => [a.id, a])), [agencies.data]);

  const today = todayIso();
  const from = search.from ?? today;
  const to = search.to ?? today;
  const dashboard = useTransferDashboard({ from: search.from, to: search.to });
  const options = presets(today);
  const active = options.find((p) => p.from === from && p.to === to)?.id;

  const choose = (preset: { id: PresetId; from: string; to: string }) =>
    void navigate({ to: '/dashboard', search: preset.id === 'today' ? {} : { from: preset.from, to: preset.to }, replace: true });

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2" role="group" aria-label={t('dashboard.period')}>
        {options.map((preset) => (
          <button
            key={preset.id}
            type="button"
            aria-pressed={active === preset.id}
            onClick={() => choose(preset)}
            className="min-h-11 rounded-full border-[1.5px] border-border bg-surface px-3.5 text-[15px] font-semibold aria-pressed:border-ink aria-pressed:bg-ink aria-pressed:text-on-ink"
          >
            {t(`dashboard.presets.${preset.id}`)}
          </button>
        ))}
        <span className="text-sm text-muted">{from === to ? formatDayShort(from) : `${formatDayShort(from)} – ${formatDayShort(to)}`}</span>
      </div>

      {dashboard.isPending && (
        <div role="status" className="grid gap-4 lg:grid-cols-2">
          <div className="h-64 animate-pulse rounded-2xl bg-surface-2" />
          <div className="h-64 animate-pulse rounded-2xl bg-surface-2" />
          <span className="sr-only">{t('common.loading')}</span>
        </div>
      )}
      {dashboard.isError && (
        <div className="flex flex-col gap-3">
          <Notice tone="error">{errorMessage(i18n, dashboard.error)}</Notice>
          <Button icon={<Icon name="sync" />} onClick={() => void dashboard.refetch()}>
            {t('common.retry')}
          </Button>
        </div>
      )}
      {dashboard.data && (
        <div className={`grid gap-4 lg:grid-cols-2 ${dashboard.isPlaceholderData ? 'opacity-60' : ''}`}>
          {dashboard.data.caisses.map((caisse) => (
            <CaisseCard key={caisse.agencyId} caisse={caisse} agency={byId.get(caisse.agencyId)} />
          ))}
        </div>
      )}

      <div className="flex flex-wrap gap-3">
        <Link to="/transfers" className="inline-flex min-h-11 items-center gap-2 font-semibold text-fg underline">
          <Icon name="transfer" />
          {t('dashboard.toTransfers')}
        </Link>
        {canSeeBalance && (
          <Link to="/balance" className="inline-flex min-h-11 items-center gap-2 font-semibold text-fg underline">
            <Icon name="cash" />
            {t('dashboard.toBalance')}
          </Link>
        )}
      </div>
    </div>
  );
}
