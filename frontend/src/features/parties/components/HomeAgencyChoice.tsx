import { useTranslation } from 'react-i18next';
import type { AgencyRef } from '@/shared/agency/agencyScope';

// Agence de rattachement d'un tiers (2 novembre 2026) : « Les deux agences » ou une agence. Un agent ne voit que les
// tiers de son agence et ceux des deux ; seul le Superviseur choisit (un agent rattache toujours à la sienne).

interface Props {
  agencies: readonly AgencyRef[];
  value: string | null;
  onChange: (agencyId: string | null) => void;
}

export function HomeAgencyChoice({ agencies, value, onChange }: Props) {
  const { t } = useTranslation();
  const options: { id: string | null; label: string }[] = [
    { id: null, label: t('parties.home.both') },
    ...agencies.map((a) => ({ id: a.id, label: t(`agencies.${a.code}`, { defaultValue: a.name }) })),
  ];

  return (
    <fieldset className="m-0 flex flex-col gap-1.5 border-0 p-0">
      <legend className="mb-1.5 text-sm font-semibold text-muted">{t('parties.home.label')}</legend>
      {options.map((option) => (
        <label
          key={option.id ?? 'both'}
          className="flex min-h-12 items-center gap-2 rounded-xl border-[1.5px] border-border px-3 has-[:checked]:border-ink has-[:checked]:bg-surface-2"
        >
          <input
            type="radio"
            name="home-agency"
            checked={value === option.id}
            onChange={() => onChange(option.id)}
            className="size-5 accent-[var(--ink)]"
          />
          <span className="font-semibold">{option.label}</span>
        </label>
      ))}
      <span className="text-sm text-muted">{t('parties.home.hint')}</span>
    </fieldset>
  );
}
