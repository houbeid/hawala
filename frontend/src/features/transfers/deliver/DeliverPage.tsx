import { Link, useParams } from '@tanstack/react-router';
import { useMemo, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { useTreasuryAccounts } from '@/features/treasury/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { isApiError } from '@/shared/api/errors';
import { toInputText } from '@/shared/format/decimalInput';
import { formatAmount } from '@/shared/format/money';
import { defaultUnit } from '@/shared/format/moneyInput';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { TextField } from '@/shared/ui/TextField';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { deliverTransfer, deliverWithoutCode } from '../api';
import { paymentValues, type FieldErrors } from '../create/createForm';
import { amountToDeliver } from '../display';
import { MAX_CODE_ATTEMPTS, failedCodeAttempts } from '../detail/detailRules';
import { formatDateTime } from '../detail/formatDateTime';
import { useTransfer, useTransferAction, useTransferHistory } from '../queries';
import type { DeliveredTransfer, Transfer } from '../types';
import { overdraftWarnings, validateCode, validateRemise, validateWithoutCode, type DeliveryForm } from './deliverForm';
import { RemiseStep } from './RemiseStep';

// Livraison (maquette 06) en deux temps : 1) le code donné par le bénéficiaire (l'agent ne peut jamais le lire),
// ou, pour le Superviseur, la pièce d'identité et le motif d'une livraison sans code ; 2) la remise répartie sur
// les caisses de l'agence de destination. Écart voulu avec la maquette : le serveur vérifie le code au moment de
// la livraison (pas de route qui le vérifie seul) ; un code faux ramène à l'étape 1 avec les essais restants.
// Hors ligne : impossible (code vérifié et caisse mise à jour en une seule fois par le serveur).

type Mode = 'code' | 'withoutCode';

const WRONG_CODE = 'Transfers.Delivery.WrongCode';
const CODE_LOCKED = 'Transfers.Delivery.CodeLocked';

function Attempts({ remaining }: { remaining: number }) {
  const { t } = useTranslation();
  return (
    <div className="flex items-center gap-2">
      <span className="flex gap-1" aria-hidden="true">
        {Array.from({ length: MAX_CODE_ATTEMPTS }, (_, index) => (
          <span key={index} className={`size-3 rounded-full border-2 border-ink ${index < remaining ? 'bg-ink' : 'bg-transparent'}`} />
        ))}
      </span>
      <span className="text-sm font-semibold">{t('transfers.deliver.attemptsLeft', { count: remaining })}</span>
    </div>
  );
}

function Delivered({ result, transfer }: { result: DeliveredTransfer; transfer: Transfer }) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-4">
      <section className="flex flex-col items-center gap-3 rounded-2xl border border-border bg-surface p-4 text-center" aria-labelledby="delivered-title">
        <span className="inline-flex size-16 items-center justify-center rounded-full bg-ink text-on-ink" aria-hidden="true">
          <Icon name="check" className="size-9" />
        </span>
        <h2 id="delivered-title" className="m-0 text-xl font-bold">
          {t('transfers.deliver.done.title')}
        </h2>
        <span className="amount font-mono text-sm">{result.reference}</span>
        <span>{t('transfers.deliver.done.to', { name: transfer.beneficiaryInfo })}</span>
        <span className="amount text-2xl">{formatAmount(result.amountDelivered, result.currency)}</span>
        <span className="text-sm text-muted">{formatDateTime(result.deliveryOperationAt)}</span>
      </section>
      {result.negativeBalances.map((warning) => (
        <Notice key={warning.treasuryAccountId} tone="warning">
          {t('transfers.deliver.done.negative', { name: warning.name, amount: formatAmount(warning.balance, warning.currency) })}
        </Notice>
      ))}
      <Link
        to="/transfers/$transferId"
        params={{ transferId: transfer.id }}
        className="inline-flex min-h-14 items-center justify-center rounded-[14px] border-2 border-ink bg-ink px-5 text-[17px] font-semibold text-on-ink no-underline"
      >
        {t('transfers.deliver.done.toDetail')}
      </Link>
      <Link
        to="/transfers"
        className="inline-flex min-h-14 items-center justify-center rounded-[14px] border-2 border-ink px-5 text-[17px] font-semibold text-fg no-underline"
      >
        {t('transfers.deliver.done.toList')}
      </Link>
    </div>
  );
}

function DeliverFlow({ transfer, mode }: { transfer: Transfer; mode: Mode }) {
  const { t, i18n } = useTranslation();
  const online = useOnlineStatus();
  const history = useTransferHistory(transfer.id, mode === 'code');
  const agencies = useAgencies();
  const destination = agencies.data?.find((a) => a.id === transfer.destinationAgencyId);
  const accounts = useTreasuryAccounts(transfer.destinationAgencyId);
  const activeAccounts = useMemo(() => (accounts.data ?? []).filter((a) => a.isActive), [accounts.data]);
  const deliver = amountToDeliver(transfer);
  const form = useForm<DeliveryForm>({ defaultValues: { code: '', idDocument: '', reason: '', note: '', payments: [] } });

  const [step, setStep] = useState<1 | 2>(1);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [serverError, setServerError] = useState<string | null>(null);
  const [remaining, setRemaining] = useState<number | null>(null);
  const [locked, setLocked] = useState(transfer.isCodeLocked && mode === 'code');
  const [result, setResult] = useState<DeliveredTransfer | null>(null);
  const remainingAttempts = remaining ?? MAX_CODE_ATTEMPTS - failedCodeAttempts(history.data);
  const lines = useWatch({ control: form.control, name: 'payments' });
  const willOverdraw = overdraftWarnings(lines, activeAccounts).length > 0;

  const withCode = useTransferAction((http, request: Parameters<typeof deliverTransfer>[2]) => deliverTransfer(http, transfer.id, request));
  const withoutCode = useTransferAction((http, request: Parameters<typeof deliverWithoutCode>[2]) =>
    deliverWithoutCode(http, transfer.id, request),
  );
  const pending = withCode.isPending || withoutCode.isPending;

  function toRemise() {
    setServerError(null);
    const values = form.getValues();
    const found: FieldErrors = mode === 'code' ? {} : validateWithoutCode(values);
    if (mode === 'code') {
      const codeError = validateCode(values.code);
      if (codeError) found.code = codeError;
    }
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    // Première visite : une ligne sur la première caisse espèces, de toute la somme à remettre.
    if (values.payments.length === 0) {
      const cash = activeAccounts.find((a) => a.type === 'Cash') ?? activeAccounts[0];
      form.setValue('payments', [{ accountId: cash?.id ?? '', amount: toInputText(deliver.amount), unit: defaultUnit(deliver.currency) }]);
    }
    setStep(2);
  }

  async function confirm() {
    setServerError(null);
    const values = form.getValues();
    const found = validateRemise(values.payments, deliver.amount);
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    const payments = values.payments.map((line, index) => ({
      treasuryAccountId: line.accountId,
      // MRU déjà converti en MRO (× 10 exact) : le serveur reçoit la devise de la caisse.
      amount: paymentValues(values.payments)[index] ?? 0,
    }));
    const note = values.note.trim() || null;
    try {
      const delivered =
        mode === 'code'
          ? await withCode.mutateAsync({ code: values.code.trim(), operationAt: null, payments, note })
          : await withoutCode.mutateAsync({
              idDocument: values.idDocument.trim(),
              reason: values.reason.trim(),
              operationAt: null,
              payments,
              note,
            });
      setResult(delivered);
    } catch (error) {
      if (isApiError(error) && (error.code === WRONG_CODE || error.code === CODE_LOCKED)) {
        const left = typeof error.details.remainingAttempts === 'number' ? error.details.remainingAttempts : 0;
        setRemaining(left);
        setLocked(error.code === CODE_LOCKED);
        form.setValue('code', '');
        setStep(1);
        if (error.code === WRONG_CODE) setServerError(t('transfers.deliver.wrongCode', { count: left }));
        return;
      }
      setServerError(
        isApiError(error) && error.isNetworkError ? t('transfers.deliver.notRecorded') : errorMessage(i18n, error),
      );
    }
  }

  if (result) return <Delivered result={result} transfer={transfer} />;

  const city = destination ? t(`agencies.${destination.code}`, { defaultValue: destination.name }) : '';
  return (
    <div className="flex flex-col gap-4">
      <ol className="m-0 flex list-none gap-2 p-0" aria-label={t('transfers.create.steps.label')}>
        {([1, 2] as const).map((s) => (
          <li
            key={s}
            aria-current={s === step ? 'step' : undefined}
            className={`flex-1 border-b-[3px] pb-1.5 text-sm ${s === step ? 'border-accent font-bold text-fg' : 'border-border text-muted'}`}
          >
            {s} · {s === 1 ? (mode === 'code' ? t('transfers.deliver.steps.code') : t('transfers.deliver.steps.identity')) : t('transfers.deliver.steps.remise')}
          </li>
        ))}
      </ol>

      <section className="flex flex-col gap-1 rounded-2xl border border-border bg-surface p-3.5">
        <span className="amount font-mono text-sm">{transfer.reference}</span>
        <span className="text-sm text-muted">{t('transfers.toDeliver')}</span>
        <span className="amount text-2xl">{formatAmount(deliver.amount, deliver.currency)}</span>
        <span className="flex flex-wrap items-center gap-1.5">
          {transfer.senderInfo}
          <Icon name="arrow" className="size-4 text-muted" />
          <b>{transfer.beneficiaryInfo}</b>
        </span>
      </section>

      {!online && <Notice tone="info">{t('transfers.deliver.offline')}</Notice>}
      {serverError && <Notice tone="error">{serverError}</Notice>}

      {step === 1 && mode === 'code' && (
        locked ? (
          <Notice tone="error">{t('transfers.deliver.locked')}</Notice>
        ) : (
          <div className="flex flex-col gap-3">
            <TextField
              label={t('transfers.deliver.code')}
              inputMode="numeric"
              autoComplete="off"
              maxLength={6}
              dir="ltr"
              error={errors.code && t(errors.code)}
              {...form.register('code')}
            />
            <Attempts remaining={remainingAttempts} />
            <Notice tone="info">{t('transfers.deliver.askBeneficiary')}</Notice>
          </div>
        )
      )}

      {step === 1 && mode === 'withoutCode' && (
        <div className="flex flex-col gap-3">
          <Notice tone="warning">{t('transfers.deliver.withoutCodeWarning')}</Notice>
          <TextField
            label={t('transfers.deliver.idDocument')}
            placeholder={t('transfers.deliver.idDocumentPlaceholder')}
            maxLength={200}
            autoComplete="off"
            error={errors.idDocument && t(errors.idDocument)}
            {...form.register('idDocument')}
          />
          <TextField
            label={t('transfers.detail.reasonRequiredLabel')}
            maxLength={500}
            error={errors.reason && t(errors.reason)}
            {...form.register('reason')}
          />
        </div>
      )}

      {step === 2 && accounts.isSuccess && (
        <>
          <RemiseStep form={form} errors={errors} accounts={activeAccounts} currency={deliver.currency} amountToDeliver={deliver.amount} city={city} />
          <TextField label={t('transfers.create.note')} maxLength={500} {...form.register('note')} />
        </>
      )}

      <div className="flex gap-3">
        {step === 2 && (
          <Button className="flex-1" onClick={() => setStep(1)}>
            {t('transfers.create.back')}
          </Button>
        )}
        {step === 1 && !locked && (
          <Button variant="primary" className="flex-1" disabled={!online} onClick={toRemise}>
            {t('transfers.create.continue')}
          </Button>
        )}
        {step === 2 && (
          <Button
            variant="primary"
            className="flex-1"
            icon={<Icon name="check" />}
            busy={pending}
            busyLabel={t('transfers.deliver.delivering')}
            disabled={!online}
            onClick={() => void confirm()}
          >
            {willOverdraw ? t('transfers.deliver.confirmAnyway') : t('transfers.deliver.confirm')}
          </Button>
        )}
      </div>
    </div>
  );
}

function DeliverScreen({ mode }: { mode: Mode }) {
  const { t, i18n } = useTranslation();
  const { transferId } = useParams({ strict: false }) as { transferId: string };
  const transfer = useTransfer(transferId);

  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-4">
      <Link to="/transfers/$transferId" params={{ transferId }} className="inline-flex min-h-11 items-center gap-1 self-start font-semibold text-fg no-underline">
        <Icon name="chevronBack" />
        {t('transfers.amounts.back')}
      </Link>
      {transfer.isPending && <span className="text-muted">{t('common.loading')}</span>}
      {transfer.isError && <Notice tone="error">{errorMessage(i18n, transfer.error)}</Notice>}
      {transfer.data &&
        (transfer.data.status === 'NotDelivered' ? (
          <DeliverFlow transfer={transfer.data} mode={mode} />
        ) : (
          <Notice tone="warning">{t(transfer.data.status === 'Delivered' ? 'errors.Transfers.Delivery.AlreadyDelivered' : 'errors.Transfers.Delivery.Cancelled')}</Notice>
        ))}
    </div>
  );
}

export function DeliverPage() {
  return <DeliverScreen mode="code" />;
}

export function DeliverWithoutCodePage() {
  return <DeliverScreen mode="withoutCode" />;
}
