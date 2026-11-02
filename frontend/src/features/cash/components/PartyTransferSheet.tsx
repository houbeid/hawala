import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { PartyPicker, type PartySummary } from '@/features/parties';
import type { Currency } from '@/features/organization/types';
import { errorMessage } from '@/shared/api/errorMessage';
import { newIdempotencyKey } from '@/shared/api/idempotency';
import { formatAmount } from '@/shared/format/money';
import { parseMoneyInput, type InputUnit } from '@/shared/format/moneyInput';
import { Button } from '@/shared/ui/Button';
import { MoneyField } from '@/shared/ui/MoneyField';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { TextField } from '@/shared/ui/TextField';
import { createPartyTransfer } from '../api';
import {
  buildPartyTransferRequest,
  partyTransferBalances,
  validatePartyTransfer,
  type PartyTransferErrors,
  type PartyTransferForm,
} from '../cashRules';
import { useCashAction } from '../queries';
import { CASH_REASON_MAX, type CreatePartyTransferRequest } from '../types';

// Transfert de solde entre tiers (parties.transfer_balance : Superviseur et Agent ; décisions du patron du
// 2 novembre 2026) : le tiers débité donne le montant au tiers crédité, dans l'agence affichée, sans toucher aux
// caisses ; note facultative. Les soldes avant / après sont écrits en mots quand ils sont visibles ; le plafond du tiers
// débité est rappelé, mais c'est le serveur qui refuse. La clé d'idempotence est gardée tant que la fenêtre est ouverte.

interface Props {
  agencyId: string;
  agencyName: string;
  currency: Currency;
  onClose: () => void;
}

export function PartyTransferSheet({ agencyId, agencyName, currency, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const save = useCashAction((http, args: { request: CreatePartyTransferRequest; key: string }) =>
    createPartyTransfer(http, args.request, args.key),
  );
  const [key] = useState(newIdempotencyKey);
  const [from, setFrom] = useState<PartySummary | null>(null);
  const [to, setTo] = useState<PartySummary | null>(null);
  const [amount, setAmount] = useState('');
  const [unit, setUnit] = useState<InputUnit>(currency === 'XOF' ? 'XOF' : 'MRO');
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState<PartyTransferErrors>({});
  const [serverError, setServerError] = useState<string | null>(null);

  const form: PartyTransferForm = {
    fromPartyId: from?.id ?? null,
    toPartyId: to?.id ?? null,
    amount,
    unit: currency === 'XOF' ? 'XOF' : unit,
    note,
  };
  const parsed = parseMoneyInput(amount, form.unit);
  const fromBalance = from?.balances?.find((b) => b.agencyId === agencyId) ?? null;
  const toBalance = to?.balances?.find((b) => b.agencyId === agencyId) ?? null;
  const after =
    parsed.ok && fromBalance && toBalance ? partyTransferBalances(fromBalance.balance, toBalance.balance, parsed.value) : null;
  const limit = fromBalance?.creditLimit ?? null;
  const overLimit = after !== null && limit !== null && after.from > limit;

  function describe(balance: number): string {
    if (balance > 0) return t('cash.partyTransfer.owesUs', { amount: formatAmount(balance, currency) });
    if (balance < 0) return t('cash.partyTransfer.weOwe', { amount: formatAmount(-balance, currency) });
    return t('cash.partyTransfer.settled');
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    const found = validatePartyTransfer(form);
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    try {
      await save.mutateAsync({ request: buildPartyTransferRequest(form, agencyId), key });
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  return (
    <Sheet
      open
      onOpenChange={(value) => !value && onClose()}
      title={t('cash.partyTransfer.title', { agency: agencyName })}
      description={t('cash.partyTransfer.hint')}
    >
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}

        <PartyPicker label={t('cash.partyTransfer.from')} selected={from} onSelect={setFrom} error={errors.from && t(errors.from)} />
        {fromBalance && <p className="m-0 text-sm text-muted">{describe(fromBalance.balance)}</p>}

        <PartyPicker label={t('cash.partyTransfer.to')} selected={to} onSelect={setTo} error={errors.to && t(errors.to)} />
        {/* Le sélecteur n'affiche son erreur qu'avant le choix : « deux fois le même tiers » s'écrit ici. */}
        {to && errors.to && <span className="text-sm font-semibold text-red">{t(errors.to)}</span>}
        {toBalance && <p className="m-0 text-sm text-muted">{describe(toBalance.balance)}</p>}

        <MoneyField
          label={t('cash.partyTransfer.amount')}
          value={amount}
          onChange={setAmount}
          unit={form.unit}
          onUnitChange={setUnit}
          currency={currency}
          error={errors.amount && t(errors.amount)}
        />

        {after && from && to && (
          <div className="flex flex-col gap-1 text-[15px]" aria-live="polite">
            <p className="m-0">{t('cash.partyTransfer.after', { name: from.name, state: describe(after.from) })}</p>
            <p className="m-0">{t('cash.partyTransfer.after', { name: to.name, state: describe(after.to) })}</p>
          </div>
        )}
        {overLimit && limit !== null && (
          <Notice tone="warning">
            {t('cash.partyTransfer.overLimit', { name: from?.name ?? '', limit: formatAmount(limit, currency) })}
          </Notice>
        )}

        <TextField
          label={t('cash.partyTransfer.note')}
          value={note}
          maxLength={CASH_REASON_MAX}
          error={errors.note && t(errors.note)}
          onChange={(e) => setNote(e.target.value)}
        />

        <Button type="submit" variant="primary" fullWidth busy={save.isPending} busyLabel={t('cash.form.saving')}>
          {t('cash.partyTransfer.save')}
        </Button>
      </form>
    </Sheet>
  );
}
