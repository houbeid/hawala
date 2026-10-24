import { useId } from 'react';
import { useTranslation } from 'react-i18next';
import { formatAmount, type Currency } from '../format/money';
import { parseMoneyInput, type InputUnit } from '../format/moneyInput';

// Saisie d'un montant dans la devise d'un compte : à Nouakchott, bascule MRO / MRU (MRU converti × 10 sur le texte,
// valeur en MRO affichée dessous) ; à Dakar, FCFA entier. La lecture du nombre reste faite par l'écran
// (parseMoneyInput) : ce champ ne fait qu'afficher.

interface Props {
  label: string;
  value: string;
  onChange: (value: string) => void;
  unit: InputUnit;
  onUnitChange: (unit: InputUnit) => void;
  currency: Currency;
  /** Message d'erreur déjà traduit. */
  error?: string;
}

export function MoneyField({ label, value, onChange, unit, onUnitChange, currency, error }: Props) {
  const { t } = useTranslation();
  const id = useId();
  const errorId = `${id}-error`;
  const converted = unit === 'MRU' && value.trim() ? parseMoneyInput(value, unit) : null;
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
          aria-describedby={error ? errorId : undefined}
          className="min-h-14 w-full min-w-0 flex-1 rounded-xl border-[1.5px] border-border bg-surface px-3.5 text-end text-[17px] font-bold tabular-nums text-fg focus-visible:border-ink focus-visible:outline-none aria-invalid:border-red"
        />
        {currency === 'MRO' ? (
          <div className="flex shrink-0 overflow-hidden rounded-xl border-[1.5px] border-border" role="group" aria-label={t('money.unit')}>
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
      {error && (
        <span id={errorId} className="text-sm font-semibold text-red">
          {error}
        </span>
      )}
      {converted?.ok && <span className="amount text-sm font-semibold text-muted">= {formatAmount(converted.value, 'MRO')}</span>}
    </div>
  );
}
