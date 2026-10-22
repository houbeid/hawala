import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useEffect, useMemo, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import type { PartySummary } from '@/features/parties';
import { useDailyRate } from '@/features/rates/queries';
import { useTreasuryAccounts } from '@/features/treasury/queries';
import { useAgencyScope } from '@/shared/agency/agencyScope';
import { errorMessage } from '@/shared/api/errorMessage';
import { isApiError } from '@/shared/api/errors';
import { newIdempotencyKey } from '@/shared/api/idempotency';
import { useCurrentAccount, usePermission, useSessionManager } from '@/shared/auth/hooks';
import { toInputText } from '@/shared/format/decimalInput';
import { formatAmount, formatRate } from '@/shared/format/money';
import { defaultUnit, transferMroPreview } from '@/shared/format/moneyInput';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { createTransfer } from '../api';
import { transferKeys } from '../queries';
import type { CreatedTransfer } from '../types';
import { AmountStep } from './AmountStep';
import {
  amountToPay,
  amountXofValue,
  operationAtIso,
  paidTotal,
  paymentValues,
  rateDay,
  stepForError,
  validateAmountStep,
  validatePaymentStep,
  validatePeopleStep,
  type CreateForm,
  type FieldErrors,
  type Step,
} from './createForm';
import { CreatedTransferView } from './CreatedTransferView';
import { CreditSection } from './CreditSection';
import { PaymentStep } from './PaymentStep';
import { PeopleStep } from './PeopleStep';
import { StepIndicator } from './StepIndicator';

// Création d'un transfert (maquette 04), en 3 étapes puis le succès avec le code de retrait.
// Garanties : la clé Idempotency-Key est créée à l'ouverture du formulaire et réutilisée à chaque nouvel envoi
// (double clic, réseau coupé : jamais deux transferts) ; un refus du serveur ramène à l'étape concernée.
// Hors ligne : création expliquée mais impossible jusqu'à la file d'attente (phase 7).

const EMPTY: Omit<CreateForm, 'sourceAgencyId'> = {
  amount: '',
  // Remplacée dès que l'agence d'envoi est connue : MRO à Nouakchott, FCFA à Dakar.
  amountUnit: 'XOF',
  operationAt: '',
  payments: [],
  senderInfo: '',
  beneficiaryInfo: '',
  note: '',
};

export function CreateTransferPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const session = useSessionManager();
  const queryClient = useQueryClient();
  const account = useCurrentAccount();
  const online = useOnlineStatus();
  const { scope } = useAgencyScope();
  const agencies = useAgencies();

  const canChooseSource = account?.agency == null;
  const canCredit = usePermission('transfers.credit');
  const [creditEnabled, setCreditEnabled] = useState(false);
  const [creditParty, setCreditParty] = useState<PartySummary | null>(null);
  const form = useForm<CreateForm>({ defaultValues: { sourceAgencyId: account?.agency?.id ?? '', ...EMPTY } });
  const [step, setStep] = useState<Step>(1);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [serverError, setServerError] = useState<string | null>(null);
  const [idempotencyKey, setIdempotencyKey] = useState(newIdempotencyKey);
  const [created, setCreated] = useState<{ result: CreatedTransfer; form: CreateForm } | null>(null);

  // Superviseur : agence d'envoi = celle choisie dans l'en-tête, sinon la première, dès que la liste arrive.
  const sourceAgencyId = useWatch({ control: form.control, name: 'sourceAgencyId' });
  useEffect(() => {
    if (sourceAgencyId || !agencies.data?.length) return;
    form.setValue('sourceAgencyId', scope.kind === 'all' ? agencies.data[0]!.id : scope.agency.id);
  }, [sourceAgencyId, agencies.data, scope, form]);

  const [amount, amountUnit, operationAt] = useWatch({ control: form.control, name: ['amount', 'amountUnit', 'operationAt'] });
  const source = agencies.data?.find((a) => a.id === sourceAgencyId);
  const destination = agencies.data?.find((a) => a.id !== sourceAgencyId);
  const rate = useDailyRate(rateDay({ operationAt }));
  // FCFA de référence : tapé, ou déduit du MRO tapé (le serveur retrouve exactement ce MRO).
  const amountXof = amountXofValue({ amount, amountUnit }, rate.data?.saleRate ?? null);

  // Saisie dans la devise de l'agence d'envoi (décision du 22 octobre 2026), l'autre reste au choix.
  const sourceCurrency = source?.currency;
  useEffect(() => {
    if (sourceCurrency) form.setValue('amountUnit', sourceCurrency);
  }, [sourceCurrency, form]);
  const amountMro = rate.data && amountXof !== null ? transferMroPreview(amountXof, rate.data.saleRate) : null;
  const toPay = source && amountXof !== null && amountMro !== null ? amountToPay(source.currency, amountXof, amountMro) : null;
  const accounts = useTreasuryAccounts(sourceAgencyId || undefined, { enabled: Boolean(sourceAgencyId) });
  const activeAccounts = useMemo(() => (accounts.data ?? []).filter((a) => a.isActive), [accounts.data]);

  const submit = useMutation({
    mutationFn: (request: Parameters<typeof createTransfer>[1]) => createTransfer(session.http, request, idempotencyKey),
  });

  function goTo(next: Step) {
    setServerError(null);
    setErrors({});
    setStep(next);
  }

  function continueFromAmount() {
    const found = validateAmountStep(form.getValues());
    if (Object.keys(found).length > 0 || !rate.data || toPay === null) {
      setErrors(found);
      return;
    }
    // Première visite du paiement : une ligne sur la première caisse espèces, du montant total.
    if (form.getValues('payments').length === 0 && source) {
      const cash = activeAccounts.find((a) => a.type === 'Cash') ?? activeAccounts[0];
      form.setValue('payments', [{ accountId: cash?.id ?? '', amount: toInputText(toPay), unit: defaultUnit(source.currency) }]);
    }
    goTo(2);
  }

  function continueFromPayment() {
    const found = validatePaymentStep(form.getValues(), toPay ?? 0, creditEnabled);
    if (creditEnabled && !creditParty) found.creditPartyId = 'transfers.create.validation.creditPartyRequired';
    if (Object.keys(found).length > 0) {
      setErrors(found);
      return;
    }
    goTo(3);
  }

  async function create() {
    const values = form.getValues();
    const found = validatePeopleStep(values);
    if (Object.keys(found).length > 0 || !destination || amountXof === null) {
      setErrors(found);
      return;
    }
    setServerError(null);
    try {
      const result = await submit.mutateAsync({
        sourceAgencyId: values.sourceAgencyId || null,
        destinationAgencyId: destination.id,
        amountXof,
        senderInfo: values.senderInfo.trim(),
        beneficiaryInfo: values.beneficiaryInfo.trim(),
        note: values.note.trim() || null,
        operationAt: operationAtIso(values),
        payments: values.payments.map((line, index) => ({
          treasuryAccountId: line.accountId,
          // MRU déjà converti en MRO (× 10 exact) : le serveur reçoit la devise du compte.
          amount: paymentValues(values.payments)[index] ?? 0,
        })),
        creditPartyId: creditEnabled && creditParty ? creditParty.id : null,
      });
      setCreated({ result, form: values });
      void queryClient.invalidateQueries({ queryKey: transferKeys.all });
    } catch (error) {
      setServerError(errorMessage(i18n, error));
      if (isApiError(error) && !error.isNetworkError) setStep(stepForError(error.code));
    }
  }

  function startNew() {
    form.reset({ sourceAgencyId: account?.agency?.id ?? sourceAgencyId, ...EMPTY, amountUnit: source?.currency ?? EMPTY.amountUnit });
    setCreditEnabled(false);
    setCreditParty(null);
    setIdempotencyKey(newIdempotencyKey());
    setCreated(null);
    goTo(1);
  }

  if (created && source && destination) {
    const r = created.result;
    return (
      <div className="mx-auto flex w-full max-w-xl flex-col gap-4">
        <CreatedTransferView
          created={r}
          deliver={destination.currency === 'XOF' ? { amount: r.amountXof, currency: 'XOF' } : { amount: r.amountMro, currency: 'MRO' }}
          paid={source.currency === 'XOF' ? { amount: r.amountXof, currency: 'XOF' } : { amount: r.amountMro, currency: 'MRO' }}
          credit={r.creditAmount > 0 && creditParty ? { amount: r.creditAmount, currency: source.currency, name: creditParty.name } : null}
          sender={created.form.senderInfo}
          beneficiary={created.form.beneficiaryInfo}
          onDone={() => void navigate({ to: '/transfers' })}
          onNew={startNew}
        />
      </div>
    );
  }

  return (
    <form
      noValidate
      className="mx-auto flex w-full max-w-xl flex-col gap-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (step === 1) continueFromAmount();
        else if (step === 2) continueFromPayment();
        else void create();
      }}
    >
      <StepIndicator step={step} />
      {!online && <Notice tone="info">{t('transfers.create.offline')}</Notice>}
      {serverError && <Notice tone="error">{serverError}</Notice>}

      {step === 1 && (
        <AmountStep
          form={form}
          errors={errors}
          agencies={agencies.data ?? []}
          canChooseSource={canChooseSource}
          source={source}
          destination={destination}
          rate={{ status: rate.isPending ? 'loading' : rate.isError ? 'error' : 'ready', data: rate.data ?? null }}
          amountXof={amountXof}
          amountMro={amountMro}
        />
      )}
      {step === 2 && source && toPay !== null && (
        <PaymentStep
          form={form}
          errors={errors}
          accounts={activeAccounts}
          currency={source.currency}
          amountToPay={toPay}
          creditEnabled={creditEnabled}
          credit={
            canCredit
              ? (creditPart) => (
                  <CreditSection
                    enabled={creditEnabled}
                    onToggle={setCreditEnabled}
                    party={creditParty}
                    onParty={setCreditParty}
                    sourceAgencyId={source.id}
                    currency={source.currency}
                    creditPart={creditPart}
                    error={errors.creditPartyId && t(errors.creditPartyId)}
                  />
                )
              : undefined
          }
        />
      )}
      {step === 3 && (
        <>
          <PeopleStep form={form} errors={errors} />
          {source && destination && amountXof !== null && amountMro !== null && rate.data && (
            <section className="flex flex-col gap-2 rounded-2xl border border-border bg-surface p-3.5" aria-label={t('transfers.create.recap')}>
              <h2 className="m-0 text-sm font-semibold text-muted">{t('transfers.create.recap')}</h2>
              <dl className="m-0 grid grid-cols-[1fr_auto] gap-2">
                <dt className="text-muted">{t('transfers.create.recapDeliver', { city: t(`agencies.${destination.code}`) })}</dt>
                <dd className="m-0 text-end">
                  <span className="amount">
                    {destination.currency === 'XOF' ? formatAmount(amountXof, 'XOF') : formatAmount(amountMro, 'MRO')}
                  </span>
                </dd>
                <dt className="text-muted">{t('transfers.create.recapCollect', { city: t(`agencies.${source.code}`) })}</dt>
                <dd className="m-0 text-end">
                  <span className="amount">{source.currency === 'XOF' ? formatAmount(amountXof, 'XOF') : formatAmount(amountMro, 'MRO')}</span>
                </dd>
                <dt className="text-muted">{t('transfers.create.rate')}</dt>
                <dd className="m-0 text-end">
                  <span className="amount text-sm">{formatRate(rate.data.saleRate)}</span>
                </dd>
                <dt className="text-muted">{t('transfers.create.recapPaid')}</dt>
                <dd className="m-0 text-end">
                  <span className="amount">{formatAmount(paidTotal(form.getValues('payments')), source.currency)}</span>
                </dd>
                {creditEnabled && creditParty && (
                  <>
                    <dt className="text-muted">{t('transfers.create.recapCredit', { name: creditParty.name })}</dt>
                    <dd className="m-0 text-end">
                      <span className="amount">
                        {formatAmount(Math.round(((toPay ?? 0) - paidTotal(form.getValues('payments'))) * 100) / 100, source.currency)}
                      </span>
                    </dd>
                  </>
                )}
              </dl>
            </section>
          )}
        </>
      )}

      <div className="flex gap-3">
        {step > 1 && (
          <Button className="flex-1" onClick={() => goTo((step - 1) as Step)}>
            {t('transfers.create.back')}
          </Button>
        )}
        {step < 3 ? (
          <Button type="submit" variant="primary" className="flex-1" disabled={step === 1 && rate.isSuccess && !rate.data}>
            {t('transfers.create.continue')}
          </Button>
        ) : (
          <Button
            type="submit"
            variant="primary"
            className="flex-1"
            icon={<Icon name="check" />}
            busy={submit.isPending}
            busyLabel={t('transfers.create.creating')}
            disabled={!online}
          >
            {t('transfers.create.submit')}
          </Button>
        )}
      </div>
    </form>
  );
}

