import { useTranslation } from 'react-i18next';
import type { Step } from './createForm';

/** « 1 · Montant  2 · Paiement  3 · Personnes » : l'étape en cours est écrite en gras et annoncée. */
export function StepIndicator({ step }: { step: Step }) {
  const { t } = useTranslation();
  const steps: Step[] = [1, 2, 3];
  return (
    <ol className="m-0 flex list-none gap-2 p-0" aria-label={t('transfers.create.steps.label')}>
      {steps.map((s) => (
        <li
          key={s}
          aria-current={s === step ? 'step' : undefined}
          className={`flex-1 border-b-[3px] pb-1.5 text-sm ${s === step ? 'border-accent font-bold text-fg' : s < step ? 'border-ink text-muted' : 'border-border text-muted'}`}
        >
          {s} · {t(`transfers.create.steps.${s}`)}
        </li>
      ))}
    </ol>
  );
}
