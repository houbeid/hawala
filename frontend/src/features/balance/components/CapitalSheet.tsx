import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { Currency } from '@/features/organization/types';
import { errorMessage } from '@/shared/api/errorMessage';
import { formatDayTimeShort } from '@/shared/format/dates';
import { formatAmount } from '@/shared/format/money';
import { parseMoneyInput, type InputUnit } from '@/shared/format/moneyInput';
import { Button } from '@/shared/ui/Button';
import { MoneyField } from '@/shared/ui/MoneyField';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { TextField } from '@/shared/ui/TextField';
import { useCapital, useSetCapital } from '../queries';
import { CAPITAL_REASON_MAX } from '../types';

// Capital initial d'une caisse (balance.manage, Superviseur ; décision du 21 octobre 2026) : un par caisse, dans sa
// devise (MRO à 2 décimales au plus, FCFA entier), positif ; chaque saisie est gardée (la plus récente fait foi) et
// tracée dans l'audit ; motif facultatif. Les bilans déjà enregistrés gardent le capital qu'ils ont utilisé.

interface Props {
  agencyId: string;
  agencyName: string;
  currency: Currency;
  onClose: () => void;
}

export function CapitalSheet({ agencyId, agencyName, currency, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const capital = useCapital();
  const save = useSetCapital();
  const mine = capital.data?.find((c) => c.agencyId === agencyId);
  const [amount, setAmount] = useState('');
  const [unit, setUnit] = useState<InputUnit>('MRO');
  const [reason, setReason] = useState('');
  const [amountError, setAmountError] = useState<string | undefined>();
  const [serverError, setServerError] = useState<string | null>(null);
  const effectiveUnit: InputUnit = currency === 'XOF' ? 'XOF' : unit;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    const parsed = parseMoneyInput(amount, effectiveUnit);
    if (!parsed.ok) {
      setAmountError(parsed.error === 'tooPrecise' ? t(`cash.form.errors.tooPrecise${effectiveUnit}`) : t(`cash.form.errors.amount.${parsed.error}`));
      return;
    }
    setAmountError(undefined);
    try {
      await save.mutateAsync({ agencyId, amount: parsed.value, reason: reason.trim() || null });
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('balance.capital.title', { name: agencyName })} description={t('balance.capital.hint')}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}
        <p className="m-0 text-[15px]">
          {mine?.current == null ? t('balance.capital.none') : t('balance.capital.current', { amount: formatAmount(mine.current, currency) })}
        </p>
        <MoneyField
          label={t('balance.capital.amount')}
          value={amount}
          onChange={setAmount}
          unit={effectiveUnit}
          onUnitChange={setUnit}
          currency={currency}
          error={amountError}
        />
        <TextField label={t('balance.capital.reason')} value={reason} maxLength={CAPITAL_REASON_MAX} onChange={(e) => setReason(e.target.value)} />
        <Button type="submit" variant="primary" fullWidth busy={save.isPending} busyLabel={t('balance.saving')}>
          {t('balance.capital.save')}
        </Button>
        {mine && mine.history.length > 0 && (
          <section className="flex flex-col gap-1.5" aria-label={t('balance.capital.history')}>
            <h3 className="m-0 text-sm font-semibold text-muted">{t('balance.capital.history')}</h3>
            <ul className="m-0 flex list-none flex-col gap-1 p-0 text-sm">
              {[...mine.history]
                .sort((a, b) => b.setAt.localeCompare(a.setAt))
                .map((change) => (
                  <li key={change.id} className="flex flex-wrap justify-between gap-x-3">
                    <span className="tabular-nums text-muted">{formatDayTimeShort(change.setAt)}</span>
                    <span className="amount">{formatAmount(change.amount, change.currency)}</span>
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
