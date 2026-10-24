import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { PartyPicker, type PartySummary } from '@/features/parties';
import { useTreasuryAccounts } from '@/features/treasury/queries';
import type { Currency } from '@/features/organization/types';
import { errorMessage } from '@/shared/api/errorMessage';
import { formatAmount } from '@/shared/format/money';
import { type InputUnit } from '@/shared/format/moneyInput';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { MoneyField } from '@/shared/ui/MoneyField';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { TextField } from '@/shared/ui/TextField';
import { createBalanceAdjustment } from '../api';
import { buildAdjustmentRequest, signedAdjustment, validateAdjustment, type AdjustmentErrors, type AdjustmentForm } from '../cashRules';
import { useCashAction } from '../queries';
import { CASH_REASON_MAX, type AdjustmentTarget, type CreateBalanceAdjustmentRequest } from '../types';

// Ajustement de solde (Superviseur, cash.manage ; skill « Caisse », décision du 21 octobre 2026) : sur un compte de
// trésorerie actif ou sur un tiers de l'agence, différence signée contre « Écarts et ajustements », motif obligatoire.
// Le sens est choisi en mots, jamais par un signe à taper ; pour un compte, le nouveau solde est affiché avant l'envoi.
// Personne n'écrit un solde à la place du calcul : on corrige par la différence, et c'est tracé dans l'audit.

const selectClass =
  'min-h-14 rounded-xl border-[1.5px] border-border bg-surface px-3.5 text-[17px] text-fg focus-visible:border-ink focus-visible:outline-none aria-invalid:border-red';

interface Props {
  agencyId: string;
  agencyName: string;
  currency: Currency;
  onClose: () => void;
}

export function AdjustmentSheet({ agencyId, agencyName, currency, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const accounts = useTreasuryAccounts(agencyId);
  const save = useCashAction((http, request: CreateBalanceAdjustmentRequest) => createBalanceAdjustment(http, request));
  const [form, setForm] = useState<AdjustmentForm>({
    target: 'TreasuryAccount',
    targetId: null,
    direction: 'down',
    amount: '',
    unit: 'MRO',
    reason: '',
    operationAt: '',
  });
  const [party, setParty] = useState<PartySummary | null>(null);
  const [editDate, setEditDate] = useState(false);
  const [errors, setErrors] = useState<AdjustmentErrors>({});
  const [serverError, setServerError] = useState<string | null>(null);

  const unit: InputUnit = currency === 'XOF' ? 'XOF' : form.unit;
  const set = (patch: Partial<AdjustmentForm>) => setForm((current) => ({ ...current, ...patch }));
  const filled: AdjustmentForm = { ...form, unit, targetId: form.target === 'Party' ? (party?.id ?? null) : form.targetId };
  const signed = signedAdjustment(filled);
  const account = form.target === 'TreasuryAccount' ? accounts.data?.find((a) => a.id === form.targetId) : undefined;
  const after = account && account.balance !== null && signed !== null ? (Math.round(account.balance * 100) + Math.round(signed * 100)) / 100 : null;

  function chooseTarget(target: AdjustmentTarget) {
    set({ target, targetId: null });
    setParty(null);
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    const found = validateAdjustment(filled);
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    try {
      await save.mutateAsync(buildAdjustmentRequest(filled, agencyId));
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('cash.adjustment.title', { agency: agencyName })} description={t('cash.adjustment.hint')}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}

        <fieldset className="m-0 flex flex-col gap-1.5 border-0 p-0">
          <legend className="mb-1.5 text-sm font-semibold text-muted">{t('cash.adjustment.target')}</legend>
          <div className="grid grid-cols-2 gap-2">
            {(['TreasuryAccount', 'Party'] as const).map((target) => (
              <label
                key={target}
                className="flex min-h-12 items-center gap-2 rounded-xl border-[1.5px] border-border px-3 has-[:checked]:border-ink has-[:checked]:bg-surface-2"
              >
                <input type="radio" name="target" checked={form.target === target} onChange={() => chooseTarget(target)} className="size-5 accent-[var(--ink)]" />
                <span className="font-semibold">{t(`cash.adjustment.targets.${target}`)}</span>
              </label>
            ))}
          </div>
        </fieldset>

        {form.target === 'TreasuryAccount' ? (
          <label className="flex flex-col gap-1.5">
            <span className="text-sm font-semibold text-muted">{t('cash.adjustment.account')}</span>
            <select
              value={form.targetId ?? ''}
              onChange={(e) => set({ targetId: e.target.value || null })}
              aria-invalid={errors.target ? true : undefined}
              className={selectClass}
            >
              <option value="">{t('cash.form.chooseAccount')}</option>
              {(accounts.data ?? []).map((a) => (
                <option key={a.id} value={a.id}>
                  {a.balance === null ? a.name : `${a.name} · ${formatAmount(a.balance, a.currency)}`}
                </option>
              ))}
            </select>
            {errors.target && <span className="text-sm font-semibold text-red">{t(errors.target)}</span>}
          </label>
        ) : (
          <PartyPicker label={t('cash.adjustment.party')} selected={party} onSelect={setParty} error={errors.target && t(errors.target)} />
        )}

        <fieldset className="m-0 flex flex-col gap-1.5 border-0 p-0">
          <legend className="mb-1.5 text-sm font-semibold text-muted">{t('cash.adjustment.direction')}</legend>
          {(['up', 'down'] as const).map((direction) => (
            <label
              key={direction}
              className="flex min-h-12 items-center gap-2 rounded-xl border-[1.5px] border-border px-3 has-[:checked]:border-ink has-[:checked]:bg-surface-2"
            >
              <input type="radio" name="direction" checked={form.direction === direction} onChange={() => set({ direction })} className="size-5 accent-[var(--ink)]" />
              <span className="font-semibold">{t(`cash.adjustment.directions.${form.target}.${direction}`)}</span>
            </label>
          ))}
        </fieldset>

        <MoneyField
          label={t('cash.adjustment.amount')}
          value={form.amount}
          onChange={(value) => set({ amount: value })}
          unit={unit}
          onUnitChange={(next) => set({ unit: next })}
          currency={currency}
          error={errors.amount && t(errors.amount)}
        />

        {account && after !== null && (
          <p className="m-0 text-[15px]" aria-live="polite">
            {t('cash.adjustment.after', { account: account.name, amount: formatAmount(after, account.currency) })}
          </p>
        )}

        <TextField
          label={t('cash.adjustment.reason')}
          value={form.reason}
          maxLength={CASH_REASON_MAX}
          error={errors.reason && t(errors.reason)}
          onChange={(e) => set({ reason: e.target.value })}
        />

        {editDate ? (
          <TextField
            label={t('cash.form.operationAt')}
            type="datetime-local"
            value={form.operationAt}
            error={errors.operationAt && t(errors.operationAt)}
            onChange={(e) => set({ operationAt: e.target.value })}
          />
        ) : (
          <div className="flex flex-wrap items-center gap-2 text-sm">
            <Icon name="calendar" className="size-5 text-muted" />
            <span className="text-muted">{t('cash.form.now')}</span>
            <button type="button" className="min-h-11 font-semibold underline" onClick={() => setEditDate(true)}>
              {t('cash.form.changeDate')}
            </button>
          </div>
        )}

        <Button type="submit" variant="primary" fullWidth busy={save.isPending} busyLabel={t('cash.form.saving')}>
          {t('cash.adjustment.save')}
        </Button>
      </form>
    </Sheet>
  );
}
