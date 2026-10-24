import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { toInputText } from '@/shared/format/decimalInput';
import { formatAmount } from '@/shared/format/money';
import { defaultUnit, type InputUnit } from '@/shared/format/moneyInput';
import { Button } from '@/shared/ui/Button';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { setCreditLimit } from '../api';
import { amountErrorKey, parseAmountOrZero } from '../partyRules';
import { usePartyAction } from '../queries';
import type { PartyAgencyBalance, SetCreditLimitRequest } from '../types';
import { AmountInput } from './AmountInput';

// Plafond de crédit d'un tiers dans une agence (parties.manage), dans la devise de l'agence : « pas de plafond »
// (null) ou un montant positif ou nul (0 = aucun crédit). Il bloque tout le monde, Superviseur compris.

type Mode = 'none' | 'limit';

interface Props {
  partyId: string;
  balance: PartyAgencyBalance;
  agencyName: string;
  onClose: () => void;
}

export function CreditLimitSheet({ partyId, balance, agencyName, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const save = usePartyAction((http, request: SetCreditLimitRequest) => setCreditLimit(http, partyId, balance.agencyId, request));
  const [mode, setMode] = useState<Mode>(balance.creditLimit === null ? 'none' : 'limit');
  const [amount, setAmount] = useState(balance.creditLimit === null ? '' : toInputText(balance.creditLimit));
  const [unit, setUnit] = useState<InputUnit>(defaultUnit(balance.currency));
  const [amountError, setAmountError] = useState<string | undefined>();
  const [serverError, setServerError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    let creditLimit: number | null = null;
    if (mode === 'limit') {
      const parsed = parseAmountOrZero(amount, unit);
      if (!parsed.ok) {
        setAmountError(amountErrorKey(parsed.error, unit));
        return;
      }
      creditLimit = parsed.value;
    }
    setAmountError(undefined);
    try {
      await save.mutateAsync({ creditLimit });
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('parties.manage.limitTitle', { agency: agencyName })}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}
        <p className="m-0 text-sm text-muted">
          {balance.creditLimit === null
            ? t('parties.credit.noLimit')
            : t('parties.manage.currentLimit', { amount: formatAmount(balance.creditLimit, balance.currency) })}
        </p>
        <fieldset className="m-0 flex flex-col gap-2 border-0 p-0">
          <legend className="sr-only">{t('parties.credit.limit')}</legend>
          {(['none', 'limit'] as const).map((choice) => (
            <label
              key={choice}
              className="flex min-h-12 items-center gap-2 rounded-xl border-[1.5px] border-border px-3 has-[:checked]:border-ink has-[:checked]:bg-surface-2"
            >
              <input type="radio" name="limit-mode" checked={mode === choice} onChange={() => setMode(choice)} className="size-5 accent-[var(--ink)]" />
              <span className="font-semibold">{t(`parties.manage.limitMode.${choice}`)}</span>
            </label>
          ))}
        </fieldset>
        {mode === 'limit' && (
          <>
            <AmountInput
              label={t('parties.credit.limit')}
              value={amount}
              onChange={setAmount}
              unit={unit}
              onUnitChange={setUnit}
              currency={balance.currency}
              error={amountError}
            />
            <p className="m-0 text-sm text-muted">{t('parties.manage.limitHint')}</p>
          </>
        )}
        <Button type="submit" variant="primary" fullWidth busy={save.isPending} busyLabel={t('parties.manage.saving')}>
          {t('parties.manage.saveLimit')}
        </Button>
      </form>
    </Sheet>
  );
}
