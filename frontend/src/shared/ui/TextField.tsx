import { useId, useState, type InputHTMLAttributes, type Ref } from 'react';
import { useTranslation } from 'react-i18next';
import { Icon } from './Icon';

// Champs des maquettes : libellé au-dessus (14 px, gris), champ de 56 px, texte 17 px.
// Le message d'erreur est relié au champ (aria-describedby) et le champ marqué aria-invalid.

interface FieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string;
  error?: string;
}

const boxClass =
  'flex min-h-14 items-center rounded-xl border-[1.5px] border-border bg-surface focus-within:border-ink ' +
  'data-invalid:border-red';
const inputClass = 'min-h-13 w-full min-w-0 border-0 bg-transparent px-3.5 text-[17px] text-fg outline-none placeholder:text-muted';

function FieldError({ id, message }: { id: string; message?: string }) {
  if (!message) return null;
  return (
    <span id={id} className="text-sm font-semibold text-red">
      {message}
    </span>
  );
}

export function TextField({ label, error, ref, ...input }: FieldProps & { ref?: Ref<HTMLInputElement> }) {
  const id = useId();
  const errorId = `${id}-error`;
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-semibold text-muted">
        {label}
      </label>
      <div className={boxClass} data-invalid={error ? '' : undefined}>
        <input
          id={id}
          ref={ref}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : undefined}
          className={inputClass}
          {...input}
        />
      </div>
      <FieldError id={errorId} message={error} />
    </div>
  );
}

/** Mot de passe avec bouton « afficher » (44 px). */
export function PasswordField({ label, error, ref, ...input }: FieldProps & { ref?: Ref<HTMLInputElement> }) {
  const { t } = useTranslation();
  const id = useId();
  const errorId = `${id}-error`;
  const [visible, setVisible] = useState(false);
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-semibold text-muted">
        {label}
      </label>
      <div className={`${boxClass} pe-1`} data-invalid={error ? '' : undefined}>
        <input
          id={id}
          ref={ref}
          type={visible ? 'text' : 'password'}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : undefined}
          className={inputClass}
          {...input}
        />
        <button
          type="button"
          onClick={() => setVisible((v) => !v)}
          aria-label={visible ? t('common.hidePassword') : t('common.showPassword')}
          aria-pressed={visible}
          className="inline-flex size-11 shrink-0 items-center justify-center rounded-xl text-fg"
        >
          <Icon name={visible ? 'eyeOff' : 'eye'} />
        </button>
      </div>
      <FieldError id={errorId} message={error} />
    </div>
  );
}
