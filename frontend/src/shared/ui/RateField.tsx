import { useId, type InputHTMLAttributes, type Ref } from 'react';

// Taux en « phrase à trou » (maquette 07) : « 5 000 FCFA = [ 3 600 ] MRO ». Préfixe et suffixe fixes,
// seul le nombre se saisit : le sens du taux n'est jamais ambigu. La formule reste de gauche à droite,
// même dans une page en arabe.

interface RateFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string;
  error?: string;
  ref?: Ref<HTMLInputElement>;
}

export function RateField({ label, error, ref, readOnly, ...input }: RateFieldProps) {
  const id = useId();
  const errorId = `${id}-error`;
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-semibold text-muted">
        {label}
      </label>
      <div
        dir="ltr"
        data-invalid={error ? '' : undefined}
        className={`flex min-h-14 items-center gap-2 rounded-xl border-[1.5px] border-border px-3.5 focus-within:border-ink data-invalid:border-red ${
          readOnly ? 'bg-surface-2' : 'bg-surface'
        }`}
      >
        <span className="shrink-0 font-semibold whitespace-nowrap text-muted" aria-hidden="true">
          5&#8239;000 FCFA =
        </span>
        <input
          id={id}
          ref={ref}
          inputMode="decimal"
          autoComplete="off"
          readOnly={readOnly}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : undefined}
          className="min-h-13 w-full min-w-0 border-0 bg-transparent text-end text-[17px] font-bold tabular-nums text-fg outline-none placeholder:text-muted"
          {...input}
        />
        <span className="shrink-0 font-semibold text-muted" aria-hidden="true">
          MRO
        </span>
      </div>
      {error && (
        <span id={errorId} className="text-sm font-semibold text-red">
          {error}
        </span>
      )}
    </div>
  );
}
