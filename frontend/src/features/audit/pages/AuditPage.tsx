import { useNavigate, useSearch } from '@tanstack/react-router';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { scopedAgencyId, useAgencyScope } from '@/shared/agency/agencyScope';
import { addDays, formatDayTimeShort, isIsoDay, todayIso } from '@/shared/format/dates';
import { AgencyBadge } from '@/shared/ui/AgencyBadge';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { changeView, parseAuditSearch, valueKey } from '../auditText';
import { useAudit } from '../queries';
import { AUDIT_FAMILIES, AUDIT_PAGE_SIZE, type AuditLine, type AuditParams } from '../types';

// Journal d'audit (audit.view, Superviseur ; règles du 22 octobre 2026) : seulement les actions (réglages et
// corrections), aujourd'hui par défaut, plus récent d'abord, 50 par page. Chaque ligne se lit comme une phrase
// construite ici, en français ou en arabe, à partir du code de l'action (« X a bloqué le compte Y ») ; les
// changements avant / après suivent (« a retiré « Annuler un transfert » » pour une permission). Jamais de valeur
// sensible : le serveur n'écrit ni mot de passe, ni téléphone, ni NNI en clair (« renseigné » / « changé »).
// L'agence est celle de l'en-tête (« les deux agences » = tout) ; le serveur applique le périmètre.

const control = 'min-h-12 rounded-xl border-[1.5px] border-border bg-surface px-3 text-[15px] text-fg focus-visible:border-ink focus-visible:outline-none';

function Value({ value }: { value: string | null }) {
  const { t } = useTranslation();
  const v = valueKey(value);
  return <span className="break-words font-semibold">{'key' in v ? t(v.key) : v.text}</span>;
}

function Line({ line, agencyCodes }: { line: AuditLine; agencyCodes: Map<string, { code: string; color: string }> }) {
  const { t } = useTranslation();
  const actor = line.actor?.name ?? t('audit.system');
  const entity = line.entityName ?? t(`audit.entities.${line.entityType}`, { defaultValue: line.entityType });
  const sentence = t(`audit.actions.${line.action}`, { actor, entity, defaultValue: `${actor} · ${line.action} · ${entity}` });
  const agency = line.agencyId ? agencyCodes.get(line.agencyId) : undefined;
  return (
    <li className="flex flex-col gap-1.5 rounded-2xl border border-border bg-surface p-3.5">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm tabular-nums text-muted">{formatDayTimeShort(line.occurredAt)}</span>
        {agency && <AgencyBadge code={agency.code} color={agency.color} />}
      </div>
      <p className="m-0 font-semibold">{sentence}</p>
      {line.reason && <p className="m-0 text-sm">{t('audit.reason', { reason: line.reason })}</p>}
      {line.changes.length > 0 && (
        <ul className="m-0 flex list-none flex-col gap-0.5 p-0 text-sm" aria-label={t('audit.changes')}>
          {line.changes.map((change) => {
            const view = changeView(line.action, change);
            if (view.kind === 'permission') {
              const name = t(`users.permissionNames.${view.permission}`, { defaultValue: view.permission });
              return <li key={change.field}>{view.granted ? t('audit.permissionGranted', { name }) : t('audit.permissionRemoved', { name })}</li>;
            }
            return (
              <li key={change.field} className="flex flex-wrap gap-x-1.5">
                <span className="text-muted">{t(`audit.fields.${view.field}`, { defaultValue: view.field })} :</span>
                <Value value={view.before} />
                <span aria-hidden="true">→</span>
                <span className="sr-only">{t('audit.becomes')}</span>
                <Value value={view.after} />
              </li>
            );
          })}
        </ul>
      )}
      {(line.ipAddress || line.device) && (
        <details className="text-sm text-muted">
          <summary className="cursor-pointer">{t('audit.device')}</summary>
          <span className="block break-words" dir="ltr">
            {[line.ipAddress, line.device].filter(Boolean).join(' · ')}
          </span>
        </details>
      )}
    </li>
  );
}

export function AuditPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const filters = parseAuditSearch(useSearch({ strict: false }) as Record<string, unknown>);
  const { scope } = useAgencyScope();
  const agencies = useAgencies();
  const agencyCodes = useMemo(() => new Map((agencies.data ?? []).map((a) => [a.id, { code: a.code, color: a.color }])), [agencies.data]);
  const audit = useAudit({ ...filters, agencyId: scopedAgencyId(scope) });

  const today = todayIso();
  const from = filters.from ?? today;
  const to = filters.to ?? today;
  const set = (patch: Partial<AuditParams>) => {
    const next = { ...filters, ...patch, page: patch.page };
    if (next.from === today) delete next.from;
    if (next.to === today) delete next.to;
    void navigate({ to: '/audit', search: next as Record<string, unknown>, replace: true });
  };

  const data = audit.data;
  const page = filters.page ?? 1;
  const lastPage = data ? Math.max(1, Math.ceil(data.totalCount / AUDIT_PAGE_SIZE)) : 1;

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-4">
      <div className="flex flex-wrap items-end gap-2" role="group" aria-label={t('audit.filters')}>
        <div className="flex flex-wrap gap-2" role="group" aria-label={t('audit.period')}>
          {[
            { id: 'today', from: today },
            { id: 'last7', from: addDays(today, -6) },
            { id: 'last30', from: addDays(today, -29) },
          ].map((preset) => (
            <button
              key={preset.id}
              type="button"
              aria-pressed={from === preset.from && to === today}
              onClick={() => set({ from: preset.from, to: today })}
              className="min-h-11 rounded-full border-[1.5px] border-border bg-surface px-3.5 text-[15px] font-semibold aria-pressed:border-ink aria-pressed:bg-ink aria-pressed:text-on-ink"
            >
              {t(`audit.presets.${preset.id}`)}
            </button>
          ))}
        </div>
        <label className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-muted">{t('audit.from')}</span>
          <input type="date" value={from} max={to} onChange={(e) => isIsoDay(e.target.value) && set({ from: e.target.value })} className={control} />
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-muted">{t('audit.to')}</span>
          <input type="date" value={to} min={from} max={today} onChange={(e) => isIsoDay(e.target.value) && set({ to: e.target.value })} className={control} />
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-muted">{t('audit.family')}</span>
          <select value={filters.action ?? ''} onChange={(e) => set({ action: e.target.value || undefined })} className={control}>
            <option value="">{t('audit.allFamilies')}</option>
            {AUDIT_FAMILIES.map((family) => (
              <option key={family} value={family}>
                {t(`audit.families.${family}`)}
              </option>
            ))}
          </select>
        </label>
      </div>

      {audit.isPending && (
        <div role="status" className="flex flex-col gap-2">
          <div className="h-24 animate-pulse rounded-2xl bg-surface-2" />
          <span className="sr-only">{t('common.loading')}</span>
        </div>
      )}
      {audit.isError && (
        <div className="flex flex-col gap-3">
          <Notice tone="error">{errorMessage(i18n, audit.error)}</Notice>
          <Button icon={<Icon name="sync" />} onClick={() => void audit.refetch()}>
            {t('common.retry')}
          </Button>
        </div>
      )}
      {data && (
        <>
          <p className="m-0 text-sm text-muted" aria-live="polite">
            {t('audit.count', { count: data.totalCount })}
          </p>
          {data.items.length === 0 ? (
            <p className="m-0 rounded-2xl bg-surface-2 p-6 text-center font-semibold">{t('audit.empty')}</p>
          ) : (
            <ul className={`m-0 flex list-none flex-col gap-2 p-0 ${audit.isPlaceholderData ? 'opacity-60' : ''}`}>
              {data.items.map((line) => (
                <Line key={line.id} line={line} agencyCodes={agencyCodes} />
              ))}
            </ul>
          )}
          {lastPage > 1 && (
            <nav className="flex items-center justify-between gap-2" aria-label={t('audit.pagination')}>
              <Button disabled={page <= 1} onClick={() => set({ page: page - 1 > 1 ? page - 1 : undefined })}>
                {t('cash.previous')}
              </Button>
              <span className="text-sm text-muted">{t('cash.page', { page, last: lastPage })}</span>
              <Button disabled={page >= lastPage} onClick={() => set({ page: page + 1 })}>
                {t('cash.next')}
              </Button>
            </nav>
          )}
        </>
      )}
    </div>
  );
}
