import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { useSessionManager } from '@/shared/auth/hooks';
import { toInputText } from '@/shared/format/decimalInput';
import { addDays, formatDayLong } from '@/shared/format/dates';
import { formatNumber, MINUS, UNIT_SPACE } from '@/shared/format/money';
import { Button } from '@/shared/ui/Button';
import { Notice } from '@/shared/ui/Notice';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { getDailyRate } from '../api';
import { rateKeys, useSetDailyRate } from '../queries';
import { rateFormSchema, rateValue, type RateForm } from '../schemas';
import type { DailyRate } from '../types';
import { RateField } from './RateField';

// Saisie des taux d'une date par le Superviseur (maquette 07, permission rates.set_daily).
// Le prix de vente s'applique aux transferts créés APRÈS l'enregistrement ; les autres gardent leur taux.
// Décisions du 22 octobre 2026 : prix de vente < prix d'achat = avertissement non bloquant ;
// « Reprendre le taux de la veille » remplit les champs sans rien enregistrer.

function GapSummary({ purchase, sale }: { purchase: number; sale: number }) {
  const { t } = useTranslation();
  const gap = sale - purchase;
  const text = `${formatNumber(gap)}${UNIT_SPACE}MRO`;
  if (gap < 0) {
    return <Notice tone="warning">{t('rates.negativeGap', { gap: `${MINUS}${UNIT_SPACE}${text}` })}</Notice>;
  }
  return (
    <div className="flex flex-col gap-1 rounded-2xl border border-border bg-surface p-3.5">
      <span className="text-sm text-muted">{t('rates.gap')}</span>
      <span className="amount text-xl">
        {text} <span className="text-[0.7em] font-semibold text-muted">{t('rates.per5000')}</span>
      </span>
    </div>
  );
}

export function RateEditor({ day, rate }: { day: string; rate: DailyRate | null }) {
  const { t, i18n } = useTranslation();
  const session = useSessionManager();
  const queryClient = useQueryClient();
  const online = useOnlineStatus();
  const save = useSetDailyRate(day);
  const [message, setMessage] = useState<{ tone: 'info' | 'error' | 'warning'; text: string } | null>(null);
  const [copying, setCopying] = useState(false);

  const {
    register,
    handleSubmit,
    control,
    setValue,
    formState: { errors },
  } = useForm<RateForm>({
    resolver: zodResolver(rateFormSchema),
    defaultValues: {
      purchaseRate: rate?.purchaseRate != null ? toInputText(rate.purchaseRate) : '',
      saleRate: rate ? toInputText(rate.saleRate) : '',
    },
  });
  const purchase = rateValue(useWatch({ control, name: 'purchaseRate' }));
  const sale = rateValue(useWatch({ control, name: 'saleRate' }));

  const onSubmit = handleSubmit(async (form) => {
    setMessage(null);
    const purchaseRate = rateValue(form.purchaseRate);
    const saleRate = rateValue(form.saleRate);
    if (purchaseRate === null || saleRate === null) return;
    try {
      await save.mutateAsync({ purchaseRate, saleRate });
      setMessage({ tone: 'info', text: t('rates.saved', { date: formatDayLong(day, i18n.language) }) });
    } catch (error) {
      setMessage({ tone: 'error', text: errorMessage(i18n, error) });
    }
  });

  async function copyPreviousDay() {
    setMessage(null);
    setCopying(true);
    const previous = addDays(day, -1);
    try {
      const found = await queryClient.fetchQuery({ queryKey: rateKeys.day(previous), queryFn: () => getDailyRate(session.http, previous) });
      if (!found) {
        setMessage({ tone: 'warning', text: t('rates.noPreviousRate') });
        return;
      }
      if (found.purchaseRate !== null) setValue('purchaseRate', toInputText(found.purchaseRate), { shouldValidate: true });
      setValue('saleRate', toInputText(found.saleRate), { shouldValidate: true });
      setMessage({ tone: 'info', text: t('rates.previousCopied') });
    } catch (error) {
      setMessage({ tone: 'error', text: errorMessage(i18n, error) });
    } finally {
      setCopying(false);
    }
  }

  return (
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-3.5">
      {!rate && <Notice tone="warning">{t('rates.notSet')}</Notice>}

      <RateField
        label={t('rates.purchaseLabel')}
        placeholder="0"
        readOnly={!online}
        error={errors.purchaseRate?.message && t(errors.purchaseRate.message)}
        {...register('purchaseRate')}
      />
      <RateField
        label={t('rates.saleLabel')}
        placeholder="0"
        readOnly={!online}
        error={errors.saleRate?.message && t(errors.saleRate.message)}
        {...register('saleRate')}
      />

      {purchase !== null && sale !== null && <GapSummary purchase={purchase} sale={sale} />}
      <Notice tone="info">{t('rates.existingKeepRate')}</Notice>
      {message && <Notice tone={message.tone}>{message.text}</Notice>}

      {!rate && online && (
        <Button fullWidth busy={copying} onClick={() => void copyPreviousDay()}>
          {t('rates.copyPrevious')}
        </Button>
      )}
      {online ? (
        <Button type="submit" variant="primary" fullWidth busy={save.isPending} busyLabel={t('rates.saving')}>
          {t('rates.save')}
        </Button>
      ) : (
        <Notice tone="info">{t('rates.offline')}</Notice>
      )}
    </form>
  );
}
