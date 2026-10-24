import { useId } from 'react';
import { useTranslation } from 'react-i18next';
import type { Currency } from '@/features/organization/types';
import { formatAmount } from '@/shared/format/money';
import type { InputUnit } from '@/shared/format/moneyInput';
import { parseAmountOrZero } from '../partyRules';

// Montant d'un solde initial ou d'un plafond, dans la devise de l'agence. À Nouakchott, bascule MRO / MRU
// (saisie en MRU convertie × 10, valeur en MRO affichée dessous) ; à Dakar, FCFA entier. Zéro est permis.

interface Props {
  label: string;
  value: string;
  onChange: (value: string) => void;
  unit: InputUnit;
  onUnitChange: (unit: InputUnit) => void;
  currency: Currency;
  /** Clé de traduction d'une erreur déjà constatée (affichée sous le champ). */
  error?: string;
}

export function AmountInput({ label, value, onChange, unit, onUnitChange, currency, error }: Props) {
  const { t } = useTranslation();
  const id = useId();
  const parsed = unit === 'MRU' && value.trim() ? parseAmountOrZero(value, unit) : null;
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-semibold text-muted">
        {label}
      </label>
      <div className="flex items-center gap-2">
        <input
          id={id}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          inputMode="decimal"
          autoComplete="off"
          dir="ltr"
          placeholder="0"
          aria-invalid={error ? true : undefined}
          className="min-h-14 w-full min-w-0 flex-1 rounded-xl border-[1.5px] border-border bg-surface px-3.5 text-end text-[17px] font-bold tabular-nums text-fg focus-visible:border-ink focus-visible:outline-none aria-invalid:border-red"
        />
        {currency === 'MRO' ? (
          <div className="flex shrink-0 overflow-hidden rounded-xl border-[1.5px] border-border" role="group" aria-label={t('parties.amount.unit')}>
            {(['MRO', 'MRU'] as const).map((choice) => (
              <button
                key={choice}
                type="button"
                aria-pressed={unit === choice}
                onClick={() => onUnitChange(choice)}
                className="min-h-12 min-w-14 px-2 font-semibold aria-pressed:bg-ink aria-pressed:text-on-ink"
              >
                {choice}
              </button>
            ))}
          </div>
        ) : (
          <span className="shrink-0 font-semibold text-muted">FCFA</span>
        )}
      </div>
      {error && <span className="text-sm font-semibold text-red">{t(error)}</span>}
      {parsed?.ok && <span className="amount text-sm font-semibold text-muted">= {formatAmount(parsed.value, 'MRO')}</span>}
    </div>
  );
}

