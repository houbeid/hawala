import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { isIsoDay, todayIso } from '@/shared/format/dates';
import { toInputText } from '@/shared/format/decimalInput';
import { formatAmount } from '@/shared/format/money';
import { defaultUnit, type InputUnit } from '@/shared/format/moneyInput';
import { Button } from '@/shared/ui/Button';
import { MoneyField } from '@/shared/ui/MoneyField';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { setTreasuryOpeningBalance } from '../api';
import { useTreasuryAction } from '../queries';
import { openingDifference, parseOpening } from '../treasuryOpening';
import type { SetTreasuryOpeningBalanceRequest, TreasuryAccount } from '../types';

// Solde de départ d'un compte de trésorerie (treasury.manage, mise en service ; décision du 22 octobre 2026). On envoie
// le NOUVEAU solde de départ ; le serveur écrit une écriture d'ouverture de LA DIFFÉRENCE contre « Soldes d'ouverture »,
// à la date choisie (aujourd'hui par défaut, jamais future). Le solde de départ actuel et le solde total sont affichés,
// la différence calculée avant l'envoi ; même valeur : rien n'est envoyé. Jamais un ajustement de solde.

interface Props {
  account: TreasuryAccount & { openingBalance: number };
  onClose: () => void;
}

export function OpeningBalanceSheet({ account, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const save = useTreasuryAction((http, request: SetTreasuryOpeningBalanceRequest) => setTreasuryOpeningBalance(http, account.id, request));
  const today = todayIso();
  const [amount, setAmount] = useState(toInputText(Math.abs(account.openingBalance)));
  const [negative, setNegative] = useState(account.openingBalance < 0);
  const [unit, setUnit] = useState<InputUnit>(defaultUnit(account.currency));
  const [date, setDate] = useState(today);
  const [amountError, setAmountError] = useState<string | undefined>();
  const [serverError, setServerError] = useState<string | null>(null);

  const parsed = parseOpening(amount, unit, negative);
  const difference = parsed.ok ? openingDifference(parsed.value, account.openingBalance) : null;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    if (!parsed.ok) {
      setAmountError(t(`treasury.opening.amount.${parsed.error === 'tooPrecise' ? `tooPrecise${unit}` : parsed.error}`));
      return;
    }
    setAmountError(undefined);
    if (difference === 0) {
      onClose();
      return;
    }
    try {
      await save.mutateAsync({ amount: parsed.value, operationDate: date === today ? null : date });
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('treasury.opening.title', { name: account.name })} description={t('treasury.opening.hint')}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}
        <dl className="m-0 grid grid-cols-[1fr_auto] gap-2 rounded-xl bg-surface-2 p-3">
          <dt className="text-sm text-muted">{t('treasury.opening.current')}</dt>
          <dd className="m-0 text-end">
            <span className="amount">{formatAmount(account.openingBalance, account.currency)}</span>
          </dd>
          <dt className="text-sm text-muted">{t('treasury.balance')}</dt>
          <dd className="m-0 text-end">
            <span className="amount">{account.balance === null ? '—' : formatAmount(account.balance, account.currency)}</span>
          </dd>
        </dl>

        <MoneyField
          label={t('treasury.opening.new')}
          value={amount}
          onChange={setAmount}
          unit={unit}
          onUnitChange={setUnit}
          currency={account.currency}
          error={amountError}
        />
        <label className="flex min-h-11 items-center gap-2">
          <input type="checkbox" checked={negative} onChange={(e) => setNegative(e.target.checked)} className="size-5 accent-[var(--ink)]" />
          <span>{t('treasury.opening.negative')}</span>
        </label>

        <label className="flex flex-col gap-1.5">
          <span className="text-sm font-semibold text-muted">{t('treasury.opening.date')}</span>
          <input
            type="date"
            value={date}
            max={today}
            onChange={(e) => setDate(isIsoDay(e.target.value) ? e.target.value : today)}
            className="min-h-14 rounded-xl border-[1.5px] border-border bg-surface px-3.5 text-[17px] text-fg"
          />
        </label>

        {difference !== null && (
          <p className="m-0 text-[15px]" aria-live="polite">
            {difference === 0
              ? t('treasury.opening.noChange')
              : t('treasury.opening.difference', { amount: formatAmount(difference, account.currency, { signed: true }) })}
          </p>
        )}

        <Button type="submit" variant="primary" fullWidth busy={save.isPending} busyLabel={t('treasury.opening.saving')}>
          {t('treasury.opening.save')}
        </Button>
      </form>
    </Sheet>
  );
}
