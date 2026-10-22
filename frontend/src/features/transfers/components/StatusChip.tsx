import { useTranslation } from 'react-i18next';
import { Icon, type IconName } from '@/shared/ui/Icon';
import type { TransferStatus } from '../types';

// Statut d'un transfert (maquette 03) : icône ET texte, jamais la couleur seule.
// Non livré = ambre, Livré = encre, Annulé = gris barré.

const LOOKS: Record<TransferStatus, { icon: IconName; className: string }> = {
  NotDelivered: { icon: 'clock', className: 'border-current bg-warn-bg text-warn-fg' },
  Delivered: { icon: 'check', className: 'border-ink bg-ink text-on-ink' },
  Cancelled: { icon: 'ban', className: 'border-current bg-surface-2 text-muted line-through' },
};

const chip = 'inline-flex h-7 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-full border-[1.5px] px-2.5 text-[13px] font-bold';

export function StatusChip({ status }: { status: TransferStatus }) {
  const { t } = useTranslation();
  const look = LOOKS[status];
  return (
    <span className={`${chip} ${look.className}`}>
      <Icon name={look.icon} className="size-4" />
      {t(`transfers.status.${status}`)}
    </span>
  );
}

/** Bloqué après 5 codes faux (déblocage par le Superviseur). */
export function CodeLockedChip() {
  const { t } = useTranslation();
  return (
    <span className={`${chip} border-red bg-red-bg text-red`}>
      <Icon name="lock" className="size-4" />
      {t('transfers.codeLocked')}
    </span>
  );
}
