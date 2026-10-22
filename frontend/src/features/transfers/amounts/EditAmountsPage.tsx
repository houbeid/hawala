import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useMemo, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import type { PartySummary } from '@/features/parties';
import { useTreasuryAccounts } from '@/features/treasury/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { isApiError } from '@/shared/api/errors';
import { toInputText } from '@/shared/format/decimalInput';
import { formatAmount } from '@/shared/format/money';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { RateField } from '@/shared/ui/RateField';
import { TextField } from '@/shared/ui/TextField';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { editTransferAmounts } from '../api';
import { validateAmountStep, validatePaymentStep, type CreateForm, type FieldErrors } from '../create/createForm';
import { CreditSection } from '../create/CreditSection';
import { PaymentStep } from '../create/PaymentStep';
import { useTransfer, useTransferAction } from '../queries';
import type { EditTransferAmountsRequest, Transfer } from '../types';
import { buildRequest, compute, initialForm, saleRateValue } from './editAmounts';

// « Modifier montant ou taux » (maquette 05, Superviseur, transfers.edit_rate) : montant (même saisie que la
// création : MRO par défaut à Nouakchott), prix de vente de CE transfert, date, répartition et crédit complets,
// motif obligatoire. Le serveur contre-passe l'écriture en vigueur puis en passe une nouvelle au jour de la
// modification ; le prix d'achat et les agences ne changent jamais. Page à part : la répartition n'entre pas
// dans une petite fenêtre.

const VERSION_CONFLICT = 'Transfers.Transfer.VersionConflict';

function EditAmountsForm({ transfer }: { transfer: Transfer }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const online = useOnlineStatus();
  const form = useForm<CreateForm>({ defaultValues: initialForm(transfer) });
  const [rateText, setRateText] = useState(toInputText(transfer.saleRate));
  const [reason, setReason] = useState('');
  const [creditEnabled, setCreditEnabled] = useState(transfer.credit !== null);
  // Tiers du crédit actuel : seuls l'id et le nom sont connus ici (son plafond se relit en le choisissant à nouveau).
  const [creditParty, setCreditParty] = useState<PartySummary | null>(
    transfer.credit
      ? {
          id: transfer.credit.partyId,
          name: transfer.credit.partyName ?? '',
          type: 'Client',
          accountCode: null,
          phone: null,
          nni: null,
          address: null,
          note: null,
          inBalanceSheet: true,
          isArchived: false,
          balances: null,
        }
      : null,
  );
  const [errors, setErrors] = useState<FieldErrors>({});
  const [serverError, setServerError] = useState<{ text: string; conflict: boolean } | null>(null);

  const [amount, amountUnit] = useWatch({ control: form.control, name: ['amount', 'amountUnit'] });
  const saleRate = saleRateValue(rateText);
  const { amountXof, amountMro, toPay } = compute({ amount, amountUnit }, saleRate, transfer);
  const accounts = useTreasuryAccounts(transfer.sourceAgencyId);
  const activeAccounts = useMemo(() => (accounts.data ?? []).filter((a) => a.isActive), [accounts.data]);
  const save = useTransferAction((http, request: EditTransferAmountsRequest) => editTransferAmounts(http, transfer.id, request));
  const deliverCurrency = transfer.destinationCurrency;

  async function submit() {
    setServerError(null);
    const values = form.getValues();
    const found: FieldErrors = { ...validateAmountStep(values) };
    if (saleRate === null) found.saleRate = 'transfers.amounts.rateInvalid';
    if (toPay !== null) Object.assign(found, validatePaymentStep(values, toPay, creditEnabled));
    if (creditEnabled && !creditParty) found.creditPartyId = 'transfers.create.validation.creditPartyRequired';
    if (!reason.trim()) found.reason = 'transfers.detail.reasonRequired';
    setErrors(found);
    if (Object.keys(found).length > 0 || amountXof === null || saleRate === null) return;

    try {
      await save.mutateAsync(
        buildRequest(transfer, values, {
          amountXof,
          saleRate,
          creditPartyId: creditEnabled && creditParty ? creditParty.id : null,
          reason: reason.trim(),
        }),
      );
      void navigate({ to: '/transfers/$transferId', params: { transferId: transfer.id } });
    } catch (error) {
      setServerError({ text: errorMessage(i18n, error), conflict: isApiError(error) && error.code === VERSION_CONFLICT });
    }
  }

  const before = deliverCurrency === 'XOF' ? formatAmount(transfer.amountXof, 'XOF') : formatAmount(transfer.amountMro, 'MRO');
  const after =
    deliverCurrency === 'XOF'
      ? amountXof === null
        ? '—'
        : formatAmount(amountXof, 'XOF')
      : amountMro === null
        ? '—'
        : formatAmount(amountMro, 'MRO');

  return (
    <form
      noValidate
      className="flex flex-col gap-4"
      onSubmit={(event) => {
        event.preventDefault();
        void submit();
      }}
    >
      {serverError && <Notice tone="error">{serverError.text}</Notice>}
      {serverError?.conflict && (
        <Link
          to="/transfers/$transferId"
          params={{ transferId: transfer.id }}
          className="inline-flex min-h-11 items-center gap-2 font-semibold text-fg underline"
        >
          <Icon name="sync" />
          {t('transfers.detail.reload')}
        </Link>
      )}

      <div className="flex flex-col gap-2">
        <div className="flex items-center gap-2">
          <span className="text-sm font-semibold text-muted">{t('transfers.create.amountUnit')}</span>
          <div className="flex overflow-hidden rounded-xl border-[1.5px] border-border" role="group" aria-label={t('transfers.create.amountUnit')}>
            {(['MRO', 'XOF'] as const).map((value) => (
              <button
                key={value}
                type="button"
                aria-pressed={amountUnit === value}
                onClick={() => form.setValue('amountUnit', value)}
                className="min-h-11 min-w-16 px-2 font-semibold aria-pressed:bg-ink aria-pressed:text-on-ink"
              >
                {value === 'XOF' ? 'FCFA' : 'MRO'}
              </button>
            ))}
          </div>
        </div>
        <TextField
          label={amountUnit === 'MRO' ? t('transfers.amounts.amountMro') : t('transfers.amounts.amountXof')}
          inputMode="numeric"
          autoComplete="off"
          dir="ltr"
          error={errors.amount && t(errors.amount)}
          {...form.register('amount')}
        />
      </div>

      <RateField
        label={t('transfers.amounts.saleRate')}
        value={rateText}
        onChange={(event) => setRateText(event.target.value)}
        error={errors.saleRate && t(errors.saleRate)}
      />

      <section className="grid grid-cols-2 gap-3 rounded-2xl border border-border bg-surface p-3.5" aria-label={t('transfers.amounts.compare')}>
        <div className="flex flex-col gap-0.5">
          <span className="text-sm text-muted">{t('transfers.history.before')}</span>
          <span className="amount text-lg">{before}</span>
        </div>
        <div className="flex flex-col gap-0.5">
          <span className="text-sm text-muted">{t('transfers.history.after')}</span>
          <span className="amount text-lg">{after}</span>
        </div>
      </section>

      <TextField label={t('transfers.create.operationAt')} type="datetime-local" error={errors.operationAt && t(errors.operationAt)} {...form.register('operationAt')} />
      <p className="m-0 -mt-2 text-sm text-muted">{t('transfers.amounts.dateHint')}</p>

      {/* Comptes chargés d'abord : un <select> enregistré sans ses options perdrait le compte déjà choisi. */}
      {!accounts.isSuccess && <span className="text-muted">{t('common.loading')}</span>}
      {toPay !== null && accounts.isSuccess && (
        <PaymentStep
          form={form}
          errors={errors}
          accounts={activeAccounts}
          currency={transfer.sourceCurrency}
          amountToPay={toPay}
          creditEnabled={creditEnabled}
          credit={(creditPart) => (
            <CreditSection
              enabled={creditEnabled}
              onToggle={setCreditEnabled}
              party={creditParty}
              onParty={setCreditParty}
              sourceAgencyId={transfer.sourceAgencyId}
              currency={transfer.sourceCurrency}
              creditPart={creditPart}
              error={errors.creditPartyId && t(errors.creditPartyId)}
            />
          )}
        />
      )}

      <TextField
        label={t('transfers.detail.reasonRequiredLabel')}
        value={reason}
        maxLength={500}
        error={errors.reason && t(errors.reason)}
        onChange={(event) => setReason(event.target.value)}
      />
      <Notice tone="info">{t('transfers.amounts.ledgerHint')}</Notice>

      {online ? (
        <Button type="submit" variant="primary" fullWidth busy={save.isPending} busyLabel={t('transfers.detail.saving')}>
          {t('transfers.amounts.save')}
        </Button>
      ) : (
        <Notice tone="info">{t('transfers.detail.actions.offline')}</Notice>
      )}
    </form>
  );
}

export function EditAmountsPage() {
  const { t, i18n } = useTranslation();
  const { transferId } = useParams({ strict: false }) as { transferId: string };
  const transfer = useTransfer(transferId);

  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-4">
      <Link
        to="/transfers/$transferId"
        params={{ transferId }}
        className="inline-flex min-h-11 items-center gap-1 self-start font-semibold text-fg no-underline"
      >
        <Icon name="chevronBack" />
        {t('transfers.amounts.back')}
      </Link>
      {transfer.isPending && <span className="text-muted">{t('common.loading')}</span>}
      {transfer.isError && <Notice tone="error">{errorMessage(i18n, transfer.error)}</Notice>}
      {transfer.data &&
        (transfer.data.status === 'NotDelivered' ? (
          <>
            <span className="amount font-mono text-sm">{transfer.data.reference}</span>
            <EditAmountsForm key={transfer.data.version} transfer={transfer.data} />
          </>
        ) : (
          <Notice tone="warning">{t('errors.Transfers.Transfer.NotEditable')}</Notice>
        ))}
    </div>
  );
}
