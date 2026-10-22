import { useTranslation } from 'react-i18next';
import type { Agency } from '@/features/organization/types';
import { formatAmount, formatNumber, UNIT_SPACE } from '@/shared/format/money';
import { AgencyBadge } from '@/shared/ui/AgencyBadge';
import type { CaisseDashboard } from '../types';

// Carte d'une caisse du tableau de bord (maquette 12, décisions du 21 octobre 2026) : sur les transferts
// qu'elle a envoyés, dans sa devise : envoyé (montant + nombre), livrés, non livrés, taux de livraison
// en nombre de transferts (texte ET barre). Pas de rapport caisse / capital.

export function CaisseCard({ caisse, agency }: { caisse: CaisseDashboard; agency: Agency | undefined }) {
  const { t } = useTranslation();
  const color = agency?.color ?? 'var(--muted)';
  const city = agency ? t(`agencies.${agency.code}`, { defaultValue: agency.name }) : '';
  const rate = caisse.deliveryRatePercent;

  return (
    <section
      aria-label={t('dashboard.caisseLabel', { city })}
      className="flex flex-col gap-3 overflow-hidden rounded-2xl border border-border bg-surface p-4"
      style={{ borderTop: `5px solid ${color}` }}
    >
      <div className="flex items-center gap-2">
        <AgencyBadge code={agency?.code ?? '…'} color={color} />
        <h2 className="m-0 text-base font-semibold">{t('dashboard.sentBy', { city })}</h2>
      </div>

      <div className="amount text-[30px] leading-tight">{formatAmount(caisse.sent.amount, caisse.currency, { maxDecimals: 0 })}</div>

      <dl className="m-0 grid grid-cols-[1fr_auto] gap-x-3 gap-y-2">
        <dt className="text-muted">{t('dashboard.transfers')}</dt>
        <dd className="m-0 text-end font-semibold tabular-nums">{caisse.sent.count}</dd>
        <dt className="text-muted">{t('dashboard.delivered')}</dt>
        <dd className="m-0 text-end">
          <span className="amount font-semibold">{formatAmount(caisse.delivered.amount, caisse.currency, { maxDecimals: 0 })}</span>
          <span className="ms-2 text-sm text-muted tabular-nums">· {caisse.delivered.count}</span>
        </dd>
        <dt className="text-muted">{t('dashboard.notDelivered')}</dt>
        <dd className="m-0 text-end">
          <span className="amount font-semibold">{formatAmount(caisse.notDelivered.amount, caisse.currency, { maxDecimals: 0 })}</span>
          <span className="ms-2 text-sm text-muted tabular-nums">· {caisse.notDelivered.count}</span>
        </dd>
      </dl>

      <div className="flex flex-col gap-1.5">
        <div className="flex items-baseline justify-between">
          <span className="text-muted">{t('dashboard.deliveryRate')}</span>
          <span className="amount text-lg">{rate === null ? '—' : `${formatNumber(rate, 1)}${UNIT_SPACE}%`}</span>
        </div>
        {/* Barre décorative : la valeur est déjà écrite juste au-dessus. */}
        <div className="h-2 overflow-hidden rounded-full bg-surface-2" aria-hidden="true">
          <div className="h-full rounded-full" style={{ width: `${Math.min(100, rate ?? 0)}%`, backgroundColor: color }} />
        </div>
        {rate === null && <span className="text-sm text-muted">{t('dashboard.noTransfers')}</span>}
      </div>
    </section>
  );
}
