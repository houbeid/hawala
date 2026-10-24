import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { PartyPicker } from '@/features/parties';
import { errorMessage } from '@/shared/api/errorMessage';
import type { HttpClient } from '@/shared/api/http';
import { formatDayTimeShort } from '@/shared/format/dates';
import { formatAmount } from '@/shared/format/money';
import type { InputUnit } from '@/shared/format/moneyInput';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { MoneyField } from '@/shared/ui/MoneyField';
import { Notice } from '@/shared/ui/Notice';
import { closeUserSession, setPartyScope, setUserLimits, setUserPermissions } from '../api';
import { usePartyScope, useUserAction, useUserLimits, useUserPermissions, useUserSessions } from '../queries';
import { grantedPermissions, parseLimit } from '../usersRules';
import type { AllowedParty } from '../types';

// Onglets de la fiche d'un compte (maquettes 13 / D6, livraison 2). Le serveur reste le seul contrôle :
// - Permissions : chaque ligne dit d'où vient son état par un MOT (« du rôle », « ajoutée », « retirée », « non
//   accordée »), l'interrupteur ne fait que le doubler ; seul un compte de rôle Superviseur modifie ; un Superviseur
//   garde toujours tout (règles validées le 21 octobre 2026) ;
// - Tiers autorisés : « Tous les tiers » ou « Liste limitée » ; un tiers créé par un compte limité y entre de lui-même ;
// - Plafonds : par transfert, par jour, crédit par jour, dans la devise de l'agence du compte ; vide = pas de limite ;
// - Sessions : appareils connectés, fermeture immédiate (l'appareil est déconnecté en direct).

function useSave<T>(action: (http: HttpClient, args: T) => Promise<unknown>) {
  const { i18n } = useTranslation();
  const mutation = useUserAction(action);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  return {
    busy: mutation.isPending,
    error,
    done,
    async run(args: T): Promise<boolean> {
      setError(null);
      setDone(false);
      try {
        await mutation.mutateAsync(args);
        setDone(true);
        return true;
      } catch (e) {
        setError(errorMessage(i18n, e));
        return false;
      }
    },
  };
}

function Loading({ error }: { error?: unknown }) {
  const { t, i18n } = useTranslation();
  if (error) return <Notice tone="error">{errorMessage(i18n, error)}</Notice>;
  return (
    <p role="status" className="m-0 text-muted">
      {t('common.loading')}
    </p>
  );
}

interface PanelProps {
  userId: string;
  /** Le compte affiché est un Superviseur (toutes les permissions, aucun plafond, tous les tiers). */
  targetIsSupervisor: boolean;
  /** Le compte connecté peut modifier (rôle Superviseur et en ligne). */
  editable: boolean;
}

export function PermissionsPanel({ userId, targetIsSupervisor, editable }: PanelProps) {
  const { t } = useTranslation();
  const permissions = useUserPermissions(userId);
  const save = useSave((http, list: string[]) => setUserPermissions(http, userId, list));
  const [changes, setChanges] = useState<Record<string, boolean>>({});
  if (!permissions.data) return <Loading error={permissions.error} />;

  const states = permissions.data.permissions;
  const canEdit = editable && !targetIsSupervisor;
  const dirty = Object.entries(changes).some(([permission, granted]) => states.find((s) => s.permission === permission)?.granted !== granted);

  return (
    <div className="flex flex-col gap-3">
      <p className="m-0 text-sm text-muted">{t('users.permissions.template', { role: t(`roles.${permissions.data.role}`) })}</p>
      {targetIsSupervisor && <Notice tone="info">{t('users.permissions.supervisorHasAll')}</Notice>}
      {save.error && <Notice tone="error">{save.error}</Notice>}
      {save.done && !dirty && <Notice tone="info">{t('users.permissions.saved')}</Notice>}
      <ul className="m-0 flex list-none flex-col rounded-2xl border border-border bg-surface p-0">
        {states.map((state) => {
          const granted = changes[state.permission] ?? state.granted;
          const changed = granted !== state.granted;
          const label = t(`users.permissionNames.${state.permission}`, { defaultValue: state.permission });
          return (
            <li key={state.permission} className="flex min-h-14 items-center gap-3 border-b border-border px-3.5 py-2 last:border-b-0">
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="font-semibold">{label}</span>
                <span className="text-xs font-bold text-muted">
                  {changed ? t('users.permissions.pending') : t(`users.permissions.sources.${state.source}`)}
                </span>
              </span>
              {canEdit ? (
                <input
                  type="checkbox"
                  role="switch"
                  aria-label={label}
                  checked={granted}
                  onChange={(e) => setChanges((current) => ({ ...current, [state.permission]: e.target.checked }))}
                  className="size-6 accent-[var(--ink)]"
                />
              ) : (
                <span className="text-sm font-semibold">{granted ? t('users.permissions.granted') : t('users.permissions.notGranted')}</span>
              )}
            </li>
          );
        })}
      </ul>
      {canEdit && (
        <Button
          variant="primary"
          disabled={!dirty}
          busy={save.busy}
          busyLabel={t('users.saving')}
          onClick={() => void save.run(grantedPermissions(states, changes)).then((ok) => ok && setChanges({}))}
        >
          {t('users.permissions.save')}
        </Button>
      )}
    </div>
  );
}

export function PartyScopePanel({ userId, targetIsSupervisor, editable }: PanelProps) {
  const { t } = useTranslation();
  const scope = usePartyScope(userId);
  const save = useSave((http, request: { limited: boolean; partyIds: string[] }) => setPartyScope(http, userId, request));
  const [draft, setDraft] = useState<{ limited: boolean; parties: AllowedParty[] } | null>(null);
  if (targetIsSupervisor) return <Notice tone="info">{t('users.partyScope.supervisorSeesAll')}</Notice>;
  if (!scope.data) return <Loading error={scope.error} />;

  const current = draft ?? { limited: scope.data.limited, parties: scope.data.parties };
  const set = (next: Partial<typeof current>) => setDraft({ ...current, ...next });

  return (
    <div className="flex flex-col gap-3">
      {save.error && <Notice tone="error">{save.error}</Notice>}
      {save.done && !draft && <Notice tone="info">{t('users.partyScope.saved')}</Notice>}
      <fieldset className="m-0 grid grid-cols-1 gap-2 border-0 p-0 sm:grid-cols-2" disabled={!editable}>
        <legend className="sr-only">{t('users.tabs.partyScope')}</legend>
        {[false, true].map((limited) => (
          <label
            key={String(limited)}
            className="flex min-h-12 items-center gap-2 rounded-xl border-[1.5px] border-border px-3 has-[:checked]:border-ink has-[:checked]:bg-surface-2"
          >
            <input type="radio" name="limited" checked={current.limited === limited} onChange={() => set({ limited })} className="size-5 accent-[var(--ink)]" />
            <span className="font-semibold">{limited ? t('users.partyScope.limited') : t('users.partyScope.all')}</span>
          </label>
        ))}
      </fieldset>
      {current.limited && (
        <>
          <p className="m-0 text-sm text-muted">{t('users.partyScope.count', { count: current.parties.length })}</p>
          <ul className="m-0 flex list-none flex-col rounded-2xl border border-border bg-surface p-0" aria-label={t('users.partyScope.allowed')}>
            {current.parties.map((party) => (
              <li key={party.id} className="flex min-h-12 items-center gap-3 border-b border-border px-3.5 last:border-b-0">
                <span className="flex-1 font-semibold">
                  {party.name}
                  {party.isArchived && <span className="ms-2 text-sm text-muted">{t('parties.archived')}</span>}
                </span>
                {editable && (
                  <button
                    type="button"
                    aria-label={t('users.partyScope.remove', { name: party.name })}
                    onClick={() => set({ parties: current.parties.filter((p) => p.id !== party.id) })}
                    className="inline-flex size-11 items-center justify-center rounded-xl"
                  >
                    <Icon name="x" />
                  </button>
                )}
              </li>
            ))}
          </ul>
          {editable && (
            <PartyPicker
              label={t('users.partyScope.add')}
              selected={null}
              onSelect={(party) => {
                if (party && !current.parties.some((p) => p.id === party.id)) {
                  set({ parties: [...current.parties, { id: party.id, name: party.name, isArchived: party.isArchived }] });
                }
              }}
            />
          )}
        </>
      )}
      {editable && (
        <Button
          variant="primary"
          disabled={!draft}
          busy={save.busy}
          busyLabel={t('users.saving')}
          onClick={() =>
            void save.run({ limited: current.limited, partyIds: current.limited ? current.parties.map((p) => p.id) : [] }).then((ok) => ok && setDraft(null))
          }
        >
          {t('users.partyScope.save')}
        </Button>
      )}
    </div>
  );
}

const LIMIT_FIELDS = ['perTransfer', 'perDay', 'creditPerDay'] as const;
type LimitField = (typeof LIMIT_FIELDS)[number];

export function LimitsPanel({ userId, targetIsSupervisor, editable }: PanelProps) {
  const { t } = useTranslation();
  const limits = useUserLimits(userId);
  const save = useSave((http, request: Record<LimitField, number | null>) => setUserLimits(http, userId, request));
  const [values, setValues] = useState<Record<LimitField, string> | null>(null);
  const [units, setUnits] = useState<Record<LimitField, InputUnit>>({ perTransfer: 'MRO', perDay: 'MRO', creditPerDay: 'MRO' });
  const [errors, setErrors] = useState<Partial<Record<LimitField, string>>>({});
  if (targetIsSupervisor) return <Notice tone="info">{t('users.limits.supervisorHasNone')}</Notice>;
  if (!limits.data) return <Loading error={limits.error} />;

  const currency = limits.data.currency;
  if (!currency) return <Notice tone="info">{t('users.limits.noAgency')}</Notice>;
  const text = (field: LimitField) => (limits.data[field] === null ? '' : String(limits.data[field]).replace('.', ','));
  const current = values ?? { perTransfer: text('perTransfer'), perDay: text('perDay'), creditPerDay: text('creditPerDay') };

  async function submit() {
    const parsed = Object.fromEntries(LIMIT_FIELDS.map((f) => [f, parseLimit(current[f], currency === 'XOF' ? 'XOF' : units[f])])) as Record<
      LimitField,
      ReturnType<typeof parseLimit>
    >;
    const found: Partial<Record<LimitField, string>> = {};
    for (const f of LIMIT_FIELDS) {
      const p = parsed[f];
      if (!p.ok) found[f] = p.error === 'tooPrecise' ? t(`cash.form.errors.tooPrecise${currency === 'XOF' ? 'XOF' : units[f]}`) : t('cash.form.errors.amount.invalid');
    }
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    const request = Object.fromEntries(LIMIT_FIELDS.map((f) => [f, (parsed[f] as { ok: true; value: number | null }).value])) as Record<LimitField, number | null>;
    if (await save.run(request)) setValues(null);
  }

  if (!editable) {
    return (
      <dl className="m-0 flex flex-col gap-2 rounded-2xl border border-border bg-surface p-3.5">
        {LIMIT_FIELDS.map((f) => (
          <div key={f} className="flex flex-wrap justify-between gap-2">
            <dt className="text-sm text-muted">{t(`users.limits.${f}`)}</dt>
            <dd className="amount m-0">{limits.data[f] === null ? t('users.limits.none') : formatAmount(limits.data[f]!, currency)}</dd>
          </div>
        ))}
      </dl>
    );
  }

  return (
    <div className="flex flex-col gap-3">
      <p className="m-0 text-sm text-muted">{t('users.limits.hint')}</p>
      {save.error && <Notice tone="error">{save.error}</Notice>}
      {save.done && !values && <Notice tone="info">{t('users.limits.saved')}</Notice>}
      {LIMIT_FIELDS.map((f) => (
        <MoneyField
          key={f}
          label={t(`users.limits.${f}`)}
          value={current[f]}
          onChange={(value) => setValues({ ...current, [f]: value })}
          unit={currency === 'XOF' ? 'XOF' : units[f]}
          onUnitChange={(unit) => setUnits((u) => ({ ...u, [f]: unit }))}
          currency={currency}
          error={errors[f]}
        />
      ))}
      <Button variant="primary" disabled={!values} busy={save.busy} busyLabel={t('users.saving')} onClick={() => void submit()}>
        {t('users.limits.save')}
      </Button>
    </div>
  );
}

export function SessionsPanel({ userId, editable }: Omit<PanelProps, 'targetIsSupervisor'>) {
  const { t } = useTranslation();
  const sessions = useUserSessions(userId);
  const close = useSave((http, sessionId: string) => closeUserSession(http, userId, sessionId));
  if (!sessions.data) return <Loading error={sessions.error} />;
  if (sessions.data.length === 0) return <p className="m-0 text-muted">{t('users.sessions.none')}</p>;

  return (
    <div className="flex flex-col gap-3">
      {close.error && <Notice tone="error">{close.error}</Notice>}
      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {sessions.data.map((s) => (
          <li key={s.id} className="flex items-start gap-3 rounded-2xl border border-border bg-surface p-3.5">
            <Icon name="devices" className="mt-0.5 text-muted" />
            <span className="flex min-w-0 flex-1 flex-col gap-0.5 text-sm">
              <span className="break-words font-semibold">
                {s.device ?? t('users.sessions.unknownDevice')}
                {s.current && ` · ${t('users.sessions.current')}`}
              </span>
              {s.ipAddress && (
                <span className="text-muted" dir="ltr">
                  {s.ipAddress}
                </span>
              )}
              <span className="text-muted">{t('users.sessions.times', { started: formatDayTimeShort(s.startedAt), used: formatDayTimeShort(s.lastUsedAt) })}</span>
            </span>
            {editable && (
              <Button variant="danger" onClick={() => void close.run(s.id)} disabled={close.busy}>
                {t('users.sessions.close')}
              </Button>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}
