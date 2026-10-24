import { useTranslation } from 'react-i18next';
import { formatAmount } from '@/shared/format/money';
import { Icon } from '@/shared/ui/Icon';
import type { CashMovement } from '../types';

// Une ligne de la caisse (maquette 09). Ni rouge ni vert (réservés aux soldes des tiers) : le sens est donné par
// l'icône (flèche entrante / sortante), le mot du type et le signe + / −. Un virement interne n'a pas de signe
// (échange entre deux comptes). Annulé : fond grisé, montant barré, mention « Annulé » et motif en clair.

/** « 24/10 · 14:40 » (heure UTC, comme les jours du serveur : Nouakchott et Dakar sont en UTC+0). */
function formatWhen(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(d.getUTCDate())}/${pad(d.getUTCMonth() + 1)} · ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`;
}

/** onCancel : Superviseur (cash.manage) en ligne, sur un mouvement actif ; absent sinon (bouton absent, pas grisé). */
export function CashMovementRow({ movement, onCancel }: { movement: CashMovement; onCancel?: () => void }) {
  const { t } = useTranslation();
  const cancelled = movement.status === 'Cancelled';
  const icon = movement.isInternal ? 'transfer' : movement.incoming ? 'downArrow' : 'up';
  const who = movement.isInternal
    ? `${movement.treasuryAccountName ?? '?'} → ${movement.destinationTreasuryAccountName ?? '?'}`
    : (movement.partyName ?? t('cash.ownerContribution'));
  const where = movement.isInternal ? null : movement.treasuryAccountName;
  const sign = movement.isInternal ? '' : movement.incoming ? '+' : '−';
  return (
    <div className={`flex items-start gap-3 rounded-2xl border border-border p-3.5 ${cancelled ? 'bg-surface-2' : 'bg-surface'}`}>
      <span className="inline-flex size-10 shrink-0 items-center justify-center rounded-full bg-surface-2" aria-hidden="true">
        <Icon name={icon} className="size-5" />
      </span>
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className={`font-semibold ${cancelled ? 'line-through' : ''}`}>{t(`cash.types.${movement.type}`)}</span>
        <span className="truncate text-sm">{who}</span>
        <span className="text-sm text-muted">
          {where ? `${where} · ` : ''}
          <span className="tabular-nums">{formatWhen(movement.operationAt)}</span>
        </span>
        {movement.note && <span className="break-words text-sm text-muted">{movement.note}</span>}
        {cancelled && (
          <span className="flex flex-col gap-0.5 text-sm">
            <span className="inline-flex items-center gap-1 self-start rounded-full border-[1.5px] border-border px-2 font-bold">
              <Icon name="ban" className="size-4" />
              {t('cash.cancelled')}
            </span>
            {movement.cancellationReason && <span>{t('cash.cancellationReason', { reason: movement.cancellationReason })}</span>}
          </span>
        )}
        {onCancel && !cancelled && (
          <button
            type="button"
            onClick={onCancel}
            aria-label={t('cash.cancel.open', { type: t(`cash.types.${movement.type}`), who })}
            className="mt-1 inline-flex min-h-11 items-center gap-1 self-start font-semibold text-red underline"
          >
            <Icon name="x" className="size-4" />
            {t('cash.cancel.button')}
          </button>
        )}
      </span>
      <span className={`amount shrink-0 ${cancelled ? 'text-muted line-through' : ''}`}>
        {sign && `${sign} `}
        {formatAmount(movement.amount, movement.currency)}
      </span>
    </div>
  );
}
