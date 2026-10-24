import { useTranslation } from 'react-i18next';
import type { AgencyRef } from './agencyScope';

// Choix d'une caisse quand le Superviseur regarde « les deux agences » : un écran qui ne vaut que pour une agence
// (Caisse, Budget : une devise, un total) lui demande laquelle afficher. Boutons « NKC | DKR », état écrit
// par aria-pressed (pas par la couleur seule).

interface Props {
  label: string;
  agencies: AgencyRef[];
  selectedId: string | undefined;
  onSelect: (agencyId: string) => void;
}

export function AgencyChoice({ label, agencies, selectedId, onSelect }: Props) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-wrap items-center gap-2" role="group" aria-label={label}>
      <span className="text-sm font-semibold text-muted">{label}</span>
      {agencies.map((agency) => (
        <button
          key={agency.id}
          type="button"
          aria-pressed={agency.id === selectedId}
          title={t(`agencies.${agency.code}`, { defaultValue: agency.name })}
          onClick={() => onSelect(agency.id)}
          className="min-h-11 rounded-full border-[1.5px] border-border bg-surface px-3.5 text-[15px] font-semibold aria-pressed:border-ink aria-pressed:bg-ink aria-pressed:text-on-ink"
        >
          {agency.code}
        </button>
      ))}
    </div>
  );
}
