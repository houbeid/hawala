import { useId } from 'react';
import { useTranslation } from 'react-i18next';

/**
 * Code de retrait en six grandes cases (maquette 04), seul endroit où il apparaît : à la création et lors
 * d'un nouveau code. Les cases sont décoratives ; le code est lu d'un seul tenant par les lecteurs d'écran.
 */
export function WithdrawalCode({ code }: { code: string }) {
  const { t } = useTranslation();
  const id = useId();
  return (
    <div className="flex flex-col items-center gap-2">
      <span className="text-sm font-semibold text-muted" id={`${id}-label`}>
        {t('transfers.create.done.code')}
      </span>
      <div className="flex gap-2" dir="ltr" role="group" aria-labelledby={`${id}-label`} aria-describedby={`${id}-text`}>
        {code.split('').map((digit, index) => (
          <span
            key={index}
            aria-hidden="true"
            className="inline-flex h-14 w-11 items-center justify-center rounded-xl border-2 border-ink font-mono text-3xl font-semibold"
          >
            {digit}
          </span>
        ))}
      </div>
      <span id={`${id}-text`} className="sr-only">
        {code}
      </span>
    </div>
  );
}
