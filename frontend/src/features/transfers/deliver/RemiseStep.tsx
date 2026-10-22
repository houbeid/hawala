import { useFieldArray, useWatch, type UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import type { Currency } from '@/features/organization/types';
import type { TreasuryAccount } from '@/features/treasury/types';
import { formatAmount } from '@/shared/format/money';
import { defaultUnit, parseMoneyInput } from '@/shared/format/moneyInput';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import type { FieldErrors } from '../create/createForm';
import { overdraftWarnings, remisedTotal, type DeliveryForm } from './deliverForm';

// Étape « Remise » (maquette 06) : la somme remise au bénéficiaire, prélevée sur les caisses actives de l'agence
// de destination ; solde de chaque caisse (si visible), bascule MRO / MRU à Nouakchott, total remis et reste à
// répartir ; avertissement ambre (jamais bloquant) si une caisse passe en négatif.

const control =
  'min-h-12 rounded-xl border-[1.5px] border-border bg-surface px-3 text-[16px] text-fg focus-visible:border-ink focus-visible:outline-none';

interface Props {
  form: UseFormReturn<DeliveryForm>;
  errors: FieldErrors;
  accounts: TreasuryAccount[];
  currency: Currency;
  amountToDeliver: number;
  city: string;
}

export function RemiseStep({ form, errors, accounts, currency, amountToDeliver, city }: Props) {
  const { t } = useTranslation();
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'payments' });
  const lines = useWatch({ control: form.control, name: 'payments' });
  const total = remisedTotal(lines);
  const rest = Math.round((amountToDeliver - total) * 100) / 100;
  const overdrafts = overdraftWarnings(lines, accounts);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-1 rounded-2xl border border-border bg-surface p-3.5">
        <span className="text-sm text-muted">{t('transfers.deliver.amountToHandOver')}</span>
        <span className="amount text-2xl">{formatAmount(amountToDeliver, currency)}</span>
      </div>

      <fieldset className="m-0 flex flex-col gap-3 border-0 p-0">
        <legend className="mb-2 text-sm font-semibold text-muted">{t('transfers.deliver.takenFrom', { city })}</legend>
        {accounts.length === 0 && <Notice tone="warning">{t('transfers.create.noAccounts')}</Notice>}
        {fields.map((field, index) => {
          const line = lines[index];
          const account = accounts.find((a) => a.id === line?.accountId);
          const parsed = line ? parseMoneyInput(line.amount, line.unit) : null;
          const overdraft = overdrafts.find((o) => o.accountId === line?.accountId);
          const accountError = errors[`payments.${index}.accountId`];
          const amountError = errors[`payments.${index}.amount`];
          return (
            <div key={field.id} className="flex flex-col gap-2 rounded-2xl border border-border bg-surface p-3">
              <div className="flex items-center gap-2">
                <label className="flex min-w-0 flex-1">
                  <span className="sr-only">{t('transfers.create.account', { n: index + 1 })}</span>
                  <select {...form.register(`payments.${index}.accountId`)} aria-invalid={accountError ? true : undefined} className={`${control} w-full`}>
                    <option value="">{t('transfers.create.chooseAccount')}</option>
                    {accounts.map((a) => (
                      <option key={a.id} value={a.id}>
                        {a.name}
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
              {account && account.balance !== null && (
                <span className="text-sm text-muted">
                  {t('transfers.deliver.balance')} <span className="amount font-semibold">{formatAmount(account.balance, account.currency)}</span>
                </span>
              )}
              {accountError && <span className="text-sm font-semibold text-red">{t(accountError)}</span>}

              <div className="flex items-center gap-2">
                <label className="flex min-w-0 flex-1">
                  <span className="sr-only">{t('transfers.create.lineAmount', { n: index + 1 })}</span>
                  <input
                    {...form.register(`payments.${index}.amount`)}
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
                        onClick={() => form.setValue(`payments.${index}.unit`, unit)}
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
              {line?.unit === 'MRU' && parsed?.ok && <span className="amount text-sm font-semibold text-muted">= {formatAmount(parsed.value, 'MRO')}</span>}
              {overdraft && (
                <Notice tone="warning">
                  {t('transfers.deliver.overdraft', { name: overdraft.name, amount: formatAmount(overdraft.after, currency) })}
                </Notice>
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
          {t('transfers.deliver.addAccount')}
        </button>
      </fieldset>

      <dl className="m-0 grid grid-cols-[1fr_auto] gap-2 rounded-2xl border border-border bg-surface p-3.5">
        <dt className="text-muted">{t('transfers.deliver.totalHandedOver')}</dt>
        <dd className="m-0 text-end">
          <span className="amount">{formatAmount(total, currency)}</span>
        </dd>
        <dt className={rest !== 0 ? 'font-semibold text-red' : 'text-muted'}>{t('transfers.deliver.rest')}</dt>
        <dd className="m-0 text-end">
          <span className={`amount ${rest !== 0 ? 'text-red' : ''}`}>{formatAmount(rest, currency)}</span>
        </dd>
      </dl>
      {errors.payments && <Notice tone="error">{t(errors.payments)}</Notice>}
    </div>
  );
}
