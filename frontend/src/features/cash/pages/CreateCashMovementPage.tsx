import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { useMemo, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { PartyPicker, type PartySummary } from '@/features/parties';
import { useTreasuryAccounts } from '@/features/treasury/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { newIdempotencyKey } from '@/shared/api/idempotency';
import { useAgencyScope, type AgencyRef } from '@/shared/agency/agencyScope';
import { formatAmount } from '@/shared/format/money';
import { parseMoneyInput, type InputUnit } from '@/shared/format/moneyInput';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { MoneyField } from '@/shared/ui/MoneyField';
import { Notice } from '@/shared/ui/Notice';
import { TextField } from '@/shared/ui/TextField';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { createCashMovement } from '../api';
import { buildCashRequest, leavesAccount, needsParty, overdraftAfter, validateCashForm, type CashForm, type CashFormErrors } from '../cashRules';
import { useCashAction } from '../queries';
import { CASH_MOVEMENT_TYPES, CASH_NOTE_MAX, type CashMovementType, type CreateCashMovementRequest } from '../types';

// Nouveau mouvement de caisse (maquette 09, « + » : cinq opérations). Le type fixe le signe. Encaissement et
// décaissement : tiers obligatoire. Approvisionnement et retrait du patron : sans tiers, mais sur un compte de
// trésorerie (exigé par le backend ; écart avec la maquette, qui ne dessinait que le montant). Virement interne :
// d'un compte vers un autre de la même agence. Comptes actifs de l'agence seulement ; MRO / MRU à Nouakchott.
// Clé Idempotency-Key créée à l'ouverture et réutilisée après une coupure : un double envoi ne crée qu'un mouvement.
// Un compte qui passerait en négatif : avertissement, sans blocage. Hors ligne : création désactivée (file
// d'attente en phase 7).

const selectClass =
  'min-h-14 rounded-xl border-[1.5px] border-border bg-surface px-3.5 text-[17px] text-fg focus-visible:border-ink focus-visible:outline-none aria-invalid:border-red';

export function CreateCashMovementPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const search = useSearch({ strict: false }) as { type?: CashMovementType; agencyId?: string };
  const { scope } = useAgencyScope();
  const online = useOnlineStatus();

  const choices: AgencyRef[] = scope.kind === 'all' ? scope.agencies : [];
  const [agencyId, setAgencyId] = useState(
    scope.kind === 'all' ? (choices.find((a) => a.id === search.agencyId) ?? choices[0])?.id : scope.agency.id,
  );
  const agency = scope.kind === 'all' ? choices.find((a) => a.id === agencyId) : scope.agency;
  const currency = agency?.currency ?? null;

  const [form, setForm] = useState<CashForm>({
    type: search.type ?? 'Receipt',
    accountId: '',
    destinationId: '',
    partyId: null,
    amount: '',
    unit: 'MRO',
    note: '',
    operationAt: '',
  });
  const [party, setParty] = useState<PartySummary | null>(null);
  const [editDate, setEditDate] = useState(false);
  const [errors, setErrors] = useState<CashFormErrors>({});
  const [serverError, setServerError] = useState<string | null>(null);
  const [idempotencyKey, setIdempotencyKey] = useState(newIdempotencyKey);

  const accounts = useTreasuryAccounts(agency?.id, { enabled: Boolean(agency) });
  const active = useMemo(() => accounts.data ?? [], [accounts.data]);
  const unit: InputUnit = currency === 'XOF' ? 'XOF' : form.unit;
  const create = useCashAction((http, request: CreateCashMovementRequest) => createCashMovement(http, request, idempotencyKey));

  const set = (patch: Partial<CashForm>) => setForm((current) => ({ ...current, ...patch }));
  const amount = parseMoneyInput(form.amount, unit);
  const source = active.find((a) => a.id === form.accountId);
  const overdraft = overdraftAfter(form.type, source?.balance ?? null, amount.ok ? amount.value : null);
  const destinations = useMemo(() => active.filter((a) => a.id !== form.accountId), [active, form.accountId]);

  /** Autre agence (Superviseur) : ses comptes et sa devise ; le choix de compte repart de zéro. */
  function changeAgency(id: string) {
    setAgencyId(id);
    set({ accountId: '', destinationId: '', unit: 'MRO' });
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    const filled = { ...form, unit, partyId: party?.id ?? null };
    const found = validateCashForm(filled);
    setErrors(found);
    if (Object.keys(found).length > 0 || !agency) return;
    try {
      await create.mutateAsync(buildCashRequest(filled, scope.kind === 'locked' ? null : agency.id));
      setIdempotencyKey(newIdempotencyKey());
      void navigate({ to: '/cash', search: scope.kind === 'all' ? { agencyId: agency.id } : {} });
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  if (!online) {
    return (
      <div className="mx-auto flex w-full max-w-xl flex-col gap-3">
        <Notice tone="info">{t('cash.offline')}</Notice>
        <Link to="/cash" className="font-semibold text-fg underline">
          {t('cash.form.back')}
        </Link>
      </div>
    );
  }

  return (
    <form noValidate onSubmit={(event) => void submit(event)} className="mx-auto flex w-full max-w-xl flex-col gap-4">
      <Link to="/cash" className="inline-flex items-center gap-1 self-start font-semibold text-fg underline">
        <Icon name="chevronBack" className="size-5" />
        {t('cash.form.back')}
      </Link>

      {serverError && <Notice tone="error">{serverError}</Notice>}

      <fieldset className="m-0 flex flex-col gap-2 border-0 p-0">
        <legend className="mb-2 text-sm font-semibold text-muted">{t('cash.form.type')}</legend>
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
          {CASH_MOVEMENT_TYPES.map((type) => (
            <label
              key={type}
              className="flex min-h-14 items-center gap-3 rounded-xl border-[1.5px] border-border bg-surface px-3.5 has-[:checked]:border-ink has-[:checked]:bg-surface-2"
            >
              <input
                type="radio"
                name="type"
                checked={form.type === type}
                onChange={() => set({ type, destinationId: '' })}
                className="size-5 accent-[var(--ink)]"
              />
              <span className="flex flex-col">
                <span className="font-semibold">{t(`cash.types.${type}`)}</span>
                <span className="text-sm text-muted">{t(`cash.form.typeHint.${type}`)}</span>
              </span>
            </label>
          ))}
        </div>
      </fieldset>

      {choices.length > 1 ? (
        <label className="flex flex-col gap-1.5">
          <span className="text-sm font-semibold text-muted">{t('cash.form.agency')}</span>
          <select value={agencyId ?? ''} onChange={(e) => changeAgency(e.target.value)} className={selectClass}>
            {choices.map((choice) => (
              <option key={choice.id} value={choice.id}>
                {t(`agencies.${choice.code}`, { defaultValue: choice.name })}
              </option>
            ))}
          </select>
        </label>
      ) : null}

      <label className="flex flex-col gap-1.5">
        <span className="text-sm font-semibold text-muted">
          {form.type === 'InternalTransfer' ? t('cash.form.fromAccount') : t('cash.form.account')}
        </span>
        <select
          value={form.accountId}
          onChange={(e) => set({ accountId: e.target.value })}
          aria-invalid={errors.account ? true : undefined}
          className={selectClass}
        >
          <option value="">{t('cash.form.chooseAccount')}</option>
          {active.map((account) => (
            <option key={account.id} value={account.id}>
              {account.balance === null ? account.name : `${account.name} · ${formatAmount(account.balance, account.currency)}`}
            </option>
          ))}
        </select>
        {errors.account && <span className="text-sm font-semibold text-red">{t(errors.account)}</span>}
      </label>

      {form.type === 'InternalTransfer' && (
        <label className="flex flex-col gap-1.5">
          <span className="text-sm font-semibold text-muted">{t('cash.form.toAccount')}</span>
          <select
            value={form.destinationId}
            onChange={(e) => set({ destinationId: e.target.value })}
            aria-invalid={errors.destination ? true : undefined}
            className={selectClass}
          >
            <option value="">{t('cash.form.chooseAccount')}</option>
            {destinations.map((account) => (
              <option key={account.id} value={account.id}>
                {account.name}
              </option>
            ))}
          </select>
          {errors.destination && <span className="text-sm font-semibold text-red">{t(errors.destination)}</span>}
        </label>
      )}

      {needsParty(form.type) && (
        <PartyPicker label={t('cash.form.party')} selected={party} onSelect={setParty} error={errors.party && t(errors.party)} />
      )}

      {currency && (
        <MoneyField
          label={t('cash.form.amount')}
          value={form.amount}
          onChange={(value) => set({ amount: value })}
          unit={unit}
          onUnitChange={(next) => set({ unit: next })}
          currency={currency}
          error={errors.amount && t(errors.amount)}
        />
      )}

      {overdraft !== null && source && (
        <Notice tone="warning">{t('cash.form.overdraft', { account: source.name, amount: formatAmount(overdraft, source.currency) })}</Notice>
      )}

      <TextField label={t('cash.form.note')} value={form.note} maxLength={CASH_NOTE_MAX} onChange={(e) => set({ note: e.target.value })} />

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

      <p className="m-0 text-sm text-muted">{t(`cash.form.effect.${leavesAccount(form.type) ? (form.type === 'InternalTransfer' ? 'internal' : 'out') : 'in'}`)}</p>

      <Button type="submit" variant="primary" fullWidth busy={create.isPending} busyLabel={t('cash.form.saving')}>
        {t('cash.form.save')}
      </Button>
    </form>
  );
}
