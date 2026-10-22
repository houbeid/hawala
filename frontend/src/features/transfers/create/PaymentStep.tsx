import type { ReactNode } from 'react';
import { useFieldArray, useWatch, type UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import type { Currency } from '@/features/organization/types';
import type { TreasuryAccount } from '@/features/treasury/types';
import { formatAmount } from '@/shared/format/money';
import { defaultUnit, parseMoneyInput } from '@/shared/format/moneyInput';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { paidTotal, type CreateForm, type FieldErrors } from './createForm';

// Étape 2 (maquette 04) : répartition du paiement du client sur les comptes ACTIFS de l'agence d'envoi.
// À Nouakchott, chaque ligne a sa bascule MRO / MRU (MRU converti × 10, conversion écrite) ; à Dakar, FCFA.
// Total réparti et reste non payé toujours visibles ; avec le crédit activé, le reste devient la part à crédit
// (section fournie par la page, permission transfers.credit) et la répartition peut être vide (crédit total).

const control =
  'min-h-12 rounded-xl border-[1.5px] border-border bg-surface px-3 text-[16px] text-fg focus-visible:border-ink focus-visible:outline-none';

interface Props {
  form: UseFormReturn<CreateForm>;
  errors: FieldErrors;
  accounts: TreasuryAccount[];
  currency: Currency;
  amountToPay: number;
  creditEnabled: boolean;
  /** Section crédit (absente sans transfers.credit), qui reçoit la part à crédit suivie en direct. */
  credit?: (creditPart: number) => ReactNode;
}

export function PaymentStep({ form, errors, accounts, currency, amountToPay, creditEnabled, credit }: Props) {
  const { t } = useTranslation();
  const { control: formControl, register, setValue } = form;
  const { fields, append, remove } = useFieldArray({ control: formControl, name: 'payments' });
  const lines = useWatch({ control: formControl, name: 'payments' });
  const total = paidTotal(lines);
  const rest = Math.round((amountToPay - total) * 100) / 100;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-1 rounded-2xl border border-border bg-surface p-3.5">
        <span className="text-sm text-muted">{t('transfers.create.totalToPay')}</span>
        <span className="amount text-2xl">{formatAmount(amountToPay, currency)}</span>
      </div>

      <fieldset className="m-0 flex flex-col gap-3 border-0 p-0">
        <legend className="mb-2 text-sm font-semibold text-muted">{t('transfers.create.split')}</legend>
        {accounts.length === 0 && <Notice tone="warning">{t('transfers.create.noAccounts')}</Notice>}
        {fields.map((field, index) => {
          const line = lines[index];
          const parsed = line ? parseMoneyInput(line.amount, line.unit) : null;
          const accountError = errors[`payments.${index}.accountId`];
          const amountError = errors[`payments.${index}.amount`];
          return (
            <div key={field.id} className="flex flex-col gap-2 rounded-2xl border border-border bg-surface p-3">
              <div className="flex items-center gap-2">
                <label className="flex min-w-0 flex-1 flex-col gap-1">
                  <span className="sr-only">{t('transfers.create.account', { n: index + 1 })}</span>
                  <select {...register(`payments.${index}.accountId`)} aria-invalid={accountError ? true : undefined} className={control}>
                    <option value="">{t('transfers.create.chooseAccount')}</option>
                    {accounts.map((account) => (
                      <option key={account.id} value={account.id}>
                        {account.name}
                      </option>
                    ))}
                  </select>
                </label>
                <button
                  type="button"
                  onClick={() => remove(index)}
                  aria-label={t('transfers.create.removeLine', { n: index + 1 })}
                  className="inline-flex size-11 shrink-0 items-center justify-center rounded-xl"
                >
                  <Icon name="x" />
                </button>
              </div>
              {accountError && <span className="text-sm font-semibold text-red">{t(accountError)}</span>}

              <div className="flex items-center gap-2">
                <label className="flex min-w-0 flex-1">
                  <span className="sr-only">{t('transfers.create.lineAmount', { n: index + 1 })}</span>
                  <input
                    {...register(`payments.${index}.amount`)}
                    inputMode="decimal"
                    autoComplete="off"
                    dir="ltr"
                    placeholder="0"
                    aria-invalid={amountError ? true : undefined}
                    className={`${control} w-full min-w-0 text-end font-bold tabular-nums`}
                  />
                </label>
                {currency === 'MRO' ? (
                  <div className="flex shrink-0 overflow-hidden rounded-xl border-[1.5px] border-border" role="group" aria-label={t('transfers.create.unit')}>
                    {(['MRO', 'MRU'] as const).map((unit) => (
                      <button
                        key={unit}
                        type="button"
                        aria-pressed={line?.unit === unit}
                        onClick={() => setValue(`payments.${index}.unit`, unit)}
                        className="min-h-12 min-w-14 px-2 font-semibold aria-pressed:bg-ink aria-pressed:text-on-ink"
                      >
                        {unit}
                      </button>
                    ))}
                  </div>
                ) : (
                  <span className="shrink-0 font-semibold text-muted">FCFA</span>
                )}
              </div>
              {amountError && <span className="text-sm font-semibold text-red">{t(amountError)}</span>}
              {line?.unit === 'MRU' && parsed?.ok && (
                <span className="amount text-sm font-semibold text-muted">= {formatAmount(parsed.value, 'MRO')}</span>
              )}
            </div>
          );
        })}
        <button
          type="button"
          onClick={() => append({ accountId: '', amount: '', unit: defaultUnit(currency) })}
          className="inline-flex min-h-11 items-center gap-2 self-start font-semibold underline"
        >
          <Icon name="plus" />
          {t('transfers.create.addAccount')}
        </button>
      </fieldset>

      <dl className="m-0 grid grid-cols-[1fr_auto] gap-2 rounded-2xl border border-border bg-surface p-3.5">
        <dt className="text-muted">{t('transfers.create.totalSplit')}</dt>
        <dd className="m-0 text-end">
          <span className="amount">{formatAmount(total, currency)}</span>
        </dd>
        <dt className={rest !== 0 && !creditEnabled ? 'font-semibold text-red' : 'text-muted'}>
          {creditEnabled ? t('transfers.create.creditPart') : t('transfers.create.unpaid')}
        </dt>
        <dd className="m-0 text-end">
          <span className={`amount ${rest !== 0 && !creditEnabled ? 'text-red' : ''}`}>{formatAmount(rest, currency)}</span>
        </dd>
      </dl>
      {credit?.(Math.max(0, rest))}
      {errors.payments && <Notice tone="error">{t(errors.payments)}</Notice>}
    </div>
  );
}
