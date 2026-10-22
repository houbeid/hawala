import { useTranslation } from 'react-i18next';
import { useAgencyScope } from '@/shared/agency/agencyScope';
import { AgencyBadge } from '@/shared/ui/AgencyBadge';
import { Icon } from '@/shared/ui/Icon';

/**
 * En-tête : badge verrouillé pour un compte d'agence (non cliquable), sélecteur pour le Superviseur
 * (« Les deux agences » par défaut). Liste native : accessible au clavier et au lecteur d'écran sans effort.
 */
export function AgencySwitcher() {
  const { t } = useTranslation();
  const { scope, select } = useAgencyScope();

  if (scope.kind === 'locked') {
    return <AgencyBadge code={scope.agency.code} color={scope.agency.color} label={t('shell.agencyLocked', { name: scope.agency.name })} />;
  }

  const value = scope.kind === 'selected' ? scope.agency.id : 'all';
  return (
    <label className="relative inline-flex min-h-11 items-center">
      <span className="sr-only">{t('shell.agencyChoice')}</span>
      <select
        value={value}
        onChange={(event) => select(event.target.value)}
        // Place réservée à droite pour la pastille de couleur ET la flèche (sinon la pastille chevauche le nom).
        className={`min-h-11 appearance-none rounded-full border-[1.5px] border-border bg-surface ps-3.5 text-[15px] font-semibold text-fg ${
          scope.kind === 'selected' ? 'pe-14' : 'pe-9'
        }`}
      >
        <option value="all">{t('shell.allAgencies')}</option>
        {scope.agencies.map((agency) => (
          <option key={agency.id} value={agency.id}>
            {agency.code} · {agency.name}
          </option>
        ))}
      </select>
      <span className="pointer-events-none absolute end-2.5 flex items-center" aria-hidden="true">
        {scope.kind === 'selected' ? (
          <i className="me-1 size-2.5 rounded-full" style={{ backgroundColor: scope.agency.color }} />
        ) : null}
        <Icon name="down" className="size-5" />
      </span>
    </label>
  );
}
