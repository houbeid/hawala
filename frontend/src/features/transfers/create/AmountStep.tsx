import { useState } from 'react';
import { useWatch, type UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import type { Agency } from '@/features/organization/types';
import type { DailyRate } from '@/features/rates/types';
import { formatAmount, formatNumber, formatRate } from '@/shared/format/money';
import { AgencyBadge } from '@/shared/ui/AgencyBadge';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { TextField } from '@/shared/ui/TextField';
import type { CreateForm, FieldErrors } from './createForm';

// Étape 1 (maquette 04) : agences, montant, taux du jour verrouillé (jamais saisi : le serveur applique le prix
// de vente du jour de l'opération). Saisie (décision du 22 octobre 2026) : MRO par défaut à Nouakchott, FCFA à
// Dakar, bascule pour l'autre devise ; l'autre montant est calculé en direct avec sa formule. Le FCFA entier
// reste la référence envoyée au serveur.
// Écart voulu avec la maquette : la date d'opération est ici, et non à l'étape 3, parce qu'elle décide du taux.

interface Props {
  form: UseFormReturn<CreateForm>;
  errors: FieldErrors;
  agencies: Agency[];
  canChooseSource: boolean;
  source: Agency | undefined;
  destination: Agency | undefined;
  rate: { status: 'loading' | 'error' | 'ready'; data: DailyRate | null };
  amountXof: number | null;
  amountMro: number | null;
}

export function AmountStep({ form, errors, agencies, canChooseSource, source, destination, rate, amountXof, amountMro }: Props) {
  const { t } = useTranslation();
  const { register, setValue } = form;
  const unit = useWatch({ control: form.control, name: 'amountUnit' });
  const [editDate, setEditDate] = useState(Boolean(form.getValues('operationAt')));
  const city = (agency: Agency | undefined) => (agency ? t(`agencies.${agency.code}`, { defaultValue: agency.name }) : '');

  return (
    <div className="flex flex-col gap-4">
      {canChooseSource ? (
        <label className="flex flex-col gap-1.5">
          <span className="text-sm font-semibold text-muted">{t('transfers.create.source')}</span>
          <select
            {...register('sourceAgencyId')}
            className="min-h-14 rounded-xl border-[1.5px] border-border bg-surface px-3.5 text-[17px] text-fg focus-visible:border-ink focus-visible:outline-none"
          >
            {agencies.map((agency) => (
              <option key={agency.id} value={agency.id}>
                {agency.code} · {city(agency)}
              </option>
            ))}
          </select>
        </label>
      ) : (
        <div className="flex flex-col gap-1.5">
          <span className="text-sm font-semibold text-muted">{t('transfers.create.source')}</span>
          <div className="flex min-h-14 items-center gap-2 rounded-xl border-[1.5px] border-border bg-surface-2 px-3.5">
            {source && <AgencyBadge code={source.code} color={source.color} />}
            <span className="flex-1 font-semibold">{city(source)}</span>
            <Icon name="lock" className="text-muted" />
          </div>
          <span className="text-sm text-muted">{t('transfers.create.sourceLocked')}</span>
        </div>
      )}

      <div className="flex flex-col gap-1.5">
        <span className="text-sm font-semibold text-muted">{t('transfers.create.destination')}</span>
        <div className="flex min-h-14 items-center gap-2 rounded-xl border-[1.5px] border-ink bg-surface px-3.5">
          {destination && <AgencyBadge code={destination.code} color={destination.color} />}
          <span className="flex-1 font-semibold">
            {city(destination)} · {destination?.currency === 'XOF' ? 'FCFA' : 'MRO'}
          </span>
          <Icon name="check" />
        </div>
      </div>

      <div className="flex flex-col gap-2">
        <div className="flex items-center gap-2">
          <span className="text-sm font-semibold text-muted">{t('transfers.create.amountUnit')}</span>
          <div className="flex overflow-hidden rounded-xl border-[1.5px] border-border" role="group" aria-label={t('transfers.create.amountUnit')}>
            {(['MRO', 'XOF'] as const).map((value) => (
              <button
                key={value}
                type="button"
                aria-pressed={unit === value}
                onClick={() => setValue('amountUnit', value)}
                className="min-h-11 min-w-16 px-2 font-semibold aria-pressed:bg-ink aria-pressed:text-on-ink"
              >
                {value === 'XOF' ? 'FCFA' : 'MRO'}
              </button>
            ))}
          </div>
        </div>
        <TextField
          label={
            unit === 'MRO'
              ? t('transfers.create.amountMroInput', { city: city(source?.currency === 'MRO' ? source : destination) })
              : t('transfers.create.amountXof', { city: city(destination?.currency === 'XOF' ? destination : source) })
          }
          inputMode="numeric"
          autoComplete="off"
          dir="ltr"
          placeholder="0"
          error={errors.amount && t(errors.amount)}
          {...register('amount')}
        />
      </div>

      {/* L'autre montant, calculé en direct, avec sa formule. */}
      <div className="flex flex-col gap-1 rounded-2xl border border-border bg-surface p-3.5">
        {unit === 'MRO' ? (
          <>
            <span className="text-sm text-muted">
              {t('transfers.create.amountXofResult', { city: city(destination?.currency === 'XOF' ? destination : source) })}
            </span>
            <span className="amount text-2xl">{amountXof === null ? '— FCFA' : formatAmount(amountXof, 'XOF')}</span>
            {rate.data && amountMro !== null && (
              <span className="amount text-xs font-normal text-muted">
                {t('transfers.create.formulaFromMro', { mro: formatNumber(amountMro, 0), rate: formatNumber(rate.data.saleRate) })}
              </span>
            )}
          </>
        ) : (
          <>
            <span className="text-sm text-muted">
              {t('transfers.create.amountMro', { city: city(destination?.currency === 'MRO' ? destination : source) })}
            </span>
            <span className="amount text-2xl">{amountMro === null ? '— MRO' : formatAmount(amountMro, 'MRO')}</span>
            {rate.data && amountXof !== null && (
              <span className="amount text-xs font-normal text-muted">
                {t('transfers.create.formula', { xof: formatNumber(amountXof, 0), rate: formatNumber(rate.data.saleRate) })}
              </span>
            )}
          </>
        )}
      </div>

      <div className="flex flex-col gap-1.5">
        <span className="text-sm font-semibold text-muted">{t('transfers.create.rate')}</span>
        {rate.status === 'ready' && rate.data ? (
          <div className="flex min-h-14 items-center gap-2 rounded-xl border-[1.5px] border-border bg-surface-2 px-3.5">
            <span className="amount flex-1">{formatRate(rate.data.saleRate)}</span>
            <Icon name="lock" className="text-muted" />
          </div>
        ) : rate.status === 'loading' ? (
          <span className="text-muted">{t('common.loading')}</span>
        ) : rate.status === 'ready' ? (
          <Notice tone="error">{t('transfers.create.rateNotSet')}</Notice>
        ) : (
          <Notice tone="error">{t('errors.generic')}</Notice>
        )}
        <span className="text-sm text-muted">{t('transfers.create.rateLocked')}</span>
      </div>

      {editDate ? (
        <TextField
          label={t('transfers.create.operationAt')}
          type="datetime-local"
          error={errors.operationAt && t(errors.operationAt)}
          {...register('operationAt')}
        />
      ) : (
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <Icon name="calendar" className="size-5 text-muted" />
          <span className="text-muted">{t('transfers.create.operationNow')}</span>
          <button type="button" className="min-h-11 font-semibold underline" onClick={() => setEditDate(true)}>
            {t('transfers.create.changeDate')}
          </button>
        </div>
      )}
    </div>
  );
}
