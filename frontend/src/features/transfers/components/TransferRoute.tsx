import { useTranslation } from 'react-i18next';
import type { Agency } from '@/features/organization/types';
import { AgencyBadge } from '@/shared/ui/AgencyBadge';
import { Icon } from '@/shared/ui/Icon';
import type { Transfer } from '../types';

// Sens d'un transfert (maquette 03) : badges des deux agences, flèche, villes en toutes lettres,
// et « Envoyé » / « Reçu » vu depuis l'agence affichée.

export function TransferRoute({ transfer, agencies }: { transfer: Transfer; agencies: Map<string, Agency> }) {
  const { t } = useTranslation();
  const source = agencies.get(transfer.sourceAgencyId);
  const destination = agencies.get(transfer.destinationAgencyId);

  return (
    <div className="flex flex-wrap items-center gap-2">
      <AgencyBadge code={source?.code ?? '…'} color={source?.color ?? 'var(--muted)'} />
      <Icon name="arrow" className="size-5 text-muted" />
      <AgencyBadge code={destination?.code ?? '…'} color={destination?.color ?? 'var(--muted)'} />
      {source && destination && (
        <span className="text-sm text-muted">
          {/* Flèche dans la traduction : « → » en français, « ← » en arabe (sens de lecture). */}
          {t('transfers.routeText', {
            from: t(`agencies.${source.code}`, { defaultValue: source.name }),
            to: t(`agencies.${destination.code}`, { defaultValue: destination.name }),
          })}
        </span>
      )}
      {transfer.direction && (
        <span className="ms-auto inline-flex items-center gap-1 text-sm font-semibold text-muted">
          <Icon name={transfer.direction === 'Sent' ? 'up' : 'downArrow'} className="size-4" />
          {t(`transfers.direction.${transfer.direction}`)}
        </span>
      )}
    </div>
  );
}
