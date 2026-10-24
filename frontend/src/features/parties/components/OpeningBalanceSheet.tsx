import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { isIsoDay, todayIso } from '@/shared/format/dates';
import { toInputText } from '@/shared/format/decimalInput';
import { formatAmount } from '@/shared/format/money';
import { defaultUnit, type InputUnit } from '@/shared/format/moneyInput';
import { Button } from '@/shared/ui/Button';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { setOpeningBalance } from '../api';
import { amountErrorKey, parseAmountOrZero } from '../partyRules';
import { usePartyAction } from '../queries';
import type { PartyAgencyBalance, SetOpeningBalanceRequest } from '../types';
import { AmountInput } from './AmountInput';
import { BalanceAmount } from './BalanceAmount';

// Solde initial d'un tiers dans une agence (parties.manage). On envoie le NOUVEAU solde initial ; le serveur écrit
// une écriture d'ouverture de LA DIFFÉRENCE contre « Soldes d'ouverture », à la date choisie (aujourd'hui par
// défaut, jamais future). La valeur actuelle est affichée et la différence calculée avant l'envoi, pour ne pas
// confondre solde initial et solde total (décision A du 22 octobre 2026). Le sens est choisi en mots : le montant
// tapé reste positif.

type Direction = 'red' | 'green';

interface Props {
  partyId: string;
  balance: PartyAgencyBalance;
  agencyName: string;
  onClose: () => void;
}

export function OpeningBalanceSheet({ partyId, balance, agencyName, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const save = usePartyAction((http, request: SetOpeningBalanceRequest) => setOpeningBalance(http, partyId, balance.agencyId, request));
  const today = todayIso();
  const [direction, setDirection] = useState<Direction>(balance.openingBalance < 0 ? 'green' : 'red');
  const [amount, setAmount] = useState(toInputText(Math.abs(balance.openingBalance)));
  const [unit, setUnit] = useState<InputUnit>(defaultUnit(balance.currency));
  const [date, setDate] = useState(today);
  const [amountError, setAmountError] = useState<string | undefined>();
  const [serverError, setServerError] = useState<string | null>(null);

  const parsed = parseAmountOrZero(amount, unit);
  const target = parsed.ok ? (direction === 'green' ? -parsed.value : parsed.value) : null;
  const difference = target === null ? null : (Math.round(target * 100) - Math.round(balance.openingBalance * 100)) / 100;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    if (!parsed.ok || target === null) {
      setAmountError(parsed.ok ? undefined : amountErrorKey(parsed.error, unit));
      return;
    }
    setAmountError(undefined);
    if (difference === 0) {
      onClose();
      return;
    }
    try {
      await save.mutateAsync({ amount: target, operationDate: date === today ? null : date });
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('parties.manage.openingTitle', { agency: agencyName })} description={t('parties.manage.openingHint')}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}
        <div className="flex flex-col gap-1.5 rounded-xl bg-surface-2 p-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="text-sm text-muted">{t('parties.manage.currentOpening')}</span>
            <BalanceAmount balance={balance.openingBalance} currency={balance.currency} />
          </div>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="text-sm text-muted">{t('parties.manage.currentBalance')}</span>
            <BalanceAmount balance={balance.balance} currency={balance.currency} />
          </div>
        </div>

        <fieldset className="m-0 flex flex-col gap-1.5 border-0 p-0">
          <legend className="mb-1.5 text-sm font-semibold text-muted">{t('parties.manage.direction')}</legend>
          <div className="grid grid-cols-2 gap-2">
            {(['red', 'green'] as const).map((choice) => (
              <label
                key={choice}
                className="flex min-h-12 items-center gap-2 rounded-xl border-[1.5px] border-border px-3 has-[:checked]:border-ink has-[:checked]:bg-surface-2"
              >
                <input type="radio" name="direction" checked={direction === choice} onChange={() => setDirection(choice)} className="size-5 accent-[var(--ink)]" />
                <span className="font-semibold">{t(`parties.balance.${choice}`)}</span>
              </label>
            ))}
          </div>
        </fieldset>

        <AmountInput
          label={t('parties.manage.newOpening')}
          value={amount}
          onChange={setAmount}
          unit={unit}
          onUnitChange={setUnit}
          currency={balance.currency}
          error={amountError}
        />

        <label className="flex flex-col gap-1.5">
          <span className="text-sm font-semibold text-muted">{t('parties.manage.openingDate')}</span>
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
              ? t('parties.manage.noChange')
              : t('parties.manage.difference', { amount: formatAmount(difference, balance.currency, { signed: true }) })}
          </p>
        )}

        <Button type="submit" variant="primary" fullWidth busy={save.isPending} busyLabel={t('parties.manage.saving')}>
          {t('parties.manage.saveOpening')}
        </Button>
      </form>
    </Sheet>
  );
}
