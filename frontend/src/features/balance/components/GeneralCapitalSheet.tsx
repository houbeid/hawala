import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { formatDayTimeShort } from '@/shared/format/dates';
import { formatAmount } from '@/shared/format/money';
import { parseMoneyInput, type InputUnit } from '@/shared/format/moneyInput';
import { Button } from '@/shared/ui/Button';
import { MoneyField } from '@/shared/ui/MoneyField';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { TextField } from '@/shared/ui/TextField';
import { useGeneralCapital, useSetGeneralCapital } from '../queries';
import { CAPITAL_REASON_MAX } from '../types';

// Capital général du bilan général (balance.manage ; décision du 31 octobre 2026) : en MRO (saisie possible en MRU,
// convertie × 10), positif, fixe : il ne bouge pas avec le taux. Chaque saisie est gardée et tracée dans l'audit ; la plus
// récente fait foi. Les capitaux de chaque caisse restent pour le bilan par caisse.

export function GeneralCapitalSheet({ onClose }: { onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const capital = useGeneralCapital();
  const save = useSetGeneralCapital();
  const [amount, setAmount] = useState('');
  const [unit, setUnit] = useState<InputUnit>('MRO');
  const [reason, setReason] = useState('');
  const [amountError, setAmountError] = useState<string | undefined>();
  const [serverError, setServerError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    const parsed = parseMoneyInput(amount, unit);
    if (!parsed.ok) {
      setAmountError(parsed.error === 'tooPrecise' ? t(`cash.form.errors.tooPrecise${unit}`) : t(`cash.form.errors.amount.${parsed.error}`));
      return;
    }
    setAmountError(undefined);
    try {
      await save.mutateAsync({ amount: parsed.value, reason: reason.trim() || null });
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  const current = capital.data?.current ?? null;
  const history = capital.data?.history ?? [];
  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('balance.general.capitalSheet.title')} description={t('balance.general.capitalSheet.hint')}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}
        <p className="m-0 text-[15px]">
          {current === null ? t('balance.general.capitalSheet.none') : t('balance.general.capitalSheet.current', { amount: formatAmount(current, 'MRO') })}
        </p>
        <MoneyField
          label={t('balance.general.capitalSheet.amount')}
          value={amount}
          onChange={setAmount}
          unit={unit}
          onUnitChange={setUnit}
          currency="MRO"
          error={amountError}
        />
        <TextField label={t('balance.capital.reason')} value={reason} maxLength={CAPITAL_REASON_MAX} onChange={(e) => setReason(e.target.value)} />
        <Button type="submit" variant="primary" fullWidth busy={save.isPending} busyLabel={t('balance.saving')}>
          {t('balance.capital.save')}
        </Button>
        {history.length > 0 && (
          <section className="flex flex-col gap-1.5" aria-label={t('balance.capital.history')}>
            <h3 className="m-0 text-sm font-semibold text-muted">{t('balance.capital.history')}</h3>
            <ul className="m-0 flex list-none flex-col gap-1 p-0 text-sm">
              {history.map((change) => (
                <li key={change.id} className="flex flex-wrap justify-between gap-x-3">
                  <span className="tabular-nums text-muted">{formatDayTimeShort(change.setAt)}</span>
                  <span className="amount">{formatAmount(change.amount, 'MRO')}</span>
                  {change.reason && <span className="w-full text-muted">{change.reason}</span>}
                </li>
              ))}
            </ul>
          </section>
        )}
      </form>
    </Sheet>
  );
}
