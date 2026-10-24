import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { useCurrentAccount } from '@/shared/auth/hooks';
import type { Role } from '@/shared/auth/types';
import { formatDayTimeShort } from '@/shared/format/dates';
import { AgencyBadge } from '@/shared/ui/AgencyBadge';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { CreateUserSheet } from '../components/CreateUserSheet';
import { useUsers } from '../queries';
import { parseUserSearch, ROLES, STATUSES, statusMarks } from '../usersRules';
import type { AccountStatus, UserListParams, UserSummary } from '../types';

// Liste des comptes (maquettes 13 / D6 ; users.manage). Filtres dans l'adresse : agence (Superviseur), rôle, statut,
// recherche (nom, email, téléphone) envoyée après 350 ms ; tri par nom (serveur), pas de pagination (quelques dizaines
// de comptes). Chaque ligne écrit l'état en mots : Actif / Bloqué, Mot de passe temporaire, Verrouillé jusqu'à…
// Un non-Superviseur ne reçoit du serveur que les comptes de son agence. Tout exige le réseau.

const SEARCH_DELAY_MS = 350;
const control = 'min-h-12 rounded-xl border-[1.5px] border-border bg-surface px-3 text-[15px] text-fg focus-visible:border-ink focus-visible:outline-none';

function UserRow({ user }: { user: UserSummary }) {
  const { t } = useTranslation();
  const marks = statusMarks(user);
  return (
    <Link to="/users/$userId" params={{ userId: user.id }} className="flex min-h-[72px] items-center gap-3 rounded-2xl border border-border bg-surface p-3.5 text-fg no-underline">
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className="flex flex-wrap items-center gap-2">
          <span className="font-semibold">{user.fullName}</span>
          <span className="rounded-full border-[1.5px] border-border px-2 text-xs font-bold">{t(`roles.${user.role}`)}</span>
        </span>
        <span className="truncate text-sm text-muted" dir="ltr">
          {user.email}
          {user.phone && ` · ${user.phone}`}
        </span>
        <span className="flex flex-wrap gap-x-2 text-sm font-semibold">
          {marks.map((mark) => (
            <span key={mark}>{mark === 'locked' && user.lockedUntil ? t('users.marks.lockedUntil', { time: formatDayTimeShort(user.lockedUntil) }) : t(`users.marks.${mark}`)}</span>
          ))}
        </span>
      </span>
      {user.agency && <AgencyBadge code={user.agency.code} color={user.agency.color} />}
      <Icon name="chevron" className="size-5 text-muted" />
    </Link>
  );
}

export function UsersPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const raw = useSearch({ strict: false }) as Record<string, unknown>;
  const filters = useMemo(() => parseUserSearch(raw), [raw]);
  const account = useCurrentAccount();
  const online = useOnlineStatus();
  const agencies = useAgencies();
  const users = useUsers(filters);
  const [creating, setCreating] = useState(false);
  const isSupervisor = account?.role === 'Supervisor';

  const setFilters = useCallback((next: UserListParams) => void navigate({ to: '/users', search: next as Record<string, unknown>, replace: true }), [navigate]);
  const [search, setSearch] = useState(filters.search ?? '');
  useEffect(() => {
    const trimmed = search.trim();
    if (trimmed === (filters.search ?? '')) return;
    const timer = setTimeout(() => setFilters({ ...filters, search: trimmed || undefined }), SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [search, filters, setFilters]);

  const allAgencies = agencies.data ?? [];
  const creatableAgencies = isSupervisor ? allAgencies : allAgencies.filter((a) => a.id === account?.agency?.id);
  const filtered = Object.keys(filters).length > 0;

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-4">
      {online ? (
        <Button variant="primary" icon={<Icon name="plus" />} className="self-start" onClick={() => setCreating(true)} disabled={creatableAgencies.length === 0}>
          {t('users.create.open')}
        </Button>
      ) : (
        <Notice tone="info">{t('users.offline')}</Notice>
      )}

      <div className="flex flex-col gap-2.5" role="group" aria-label={t('users.filters.label')}>
        <label className="flex min-h-12 items-center gap-2 rounded-xl border-[1.5px] border-border bg-surface px-3 focus-within:border-ink">
          <Icon name="search" className="text-muted" />
          <span className="sr-only">{t('users.filters.search')}</span>
          <input
            type="search"
            value={search}
            placeholder={t('users.filters.searchPlaceholder')}
            onChange={(e) => setSearch(e.target.value)}
            className="min-w-0 flex-1 bg-transparent text-[17px] text-fg outline-none"
          />
        </label>
        <div className="flex flex-wrap gap-2">
          {isSupervisor && (
            <label className="flex flex-col gap-1">
              <span className="text-xs font-semibold text-muted">{t('users.fields.agency')}</span>
              <select value={filters.agencyId ?? ''} onChange={(e) => setFilters({ ...filters, agencyId: e.target.value || undefined })} className={control}>
                <option value="">{t('users.filters.allAgencies')}</option>
                {allAgencies.map((agency) => (
                  <option key={agency.id} value={agency.id}>
                    {agency.code}
                  </option>
                ))}
              </select>
            </label>
          )}
          <label className="flex flex-col gap-1">
            <span className="text-xs font-semibold text-muted">{t('users.fields.role')}</span>
            <select value={filters.role ?? ''} onChange={(e) => setFilters({ ...filters, role: (e.target.value || undefined) as Role | undefined })} className={control}>
              <option value="">{t('users.filters.allRoles')}</option>
              {ROLES.map((role) => (
                <option key={role} value={role}>
                  {t(`roles.${role}`)}
                </option>
              ))}
            </select>
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-xs font-semibold text-muted">{t('users.fields.status')}</span>
            <select
              value={filters.status ?? ''}
              onChange={(e) => setFilters({ ...filters, status: (e.target.value || undefined) as AccountStatus | undefined })}
              className={control}
            >
              <option value="">{t('users.filters.allStatuses')}</option>
              {STATUSES.map((status) => (
                <option key={status} value={status}>
                  {t(`users.statuses.${status}`)}
                </option>
              ))}
            </select>
          </label>
        </div>
      </div>

      {users.isPending && (
        <div role="status" className="flex flex-col gap-2">
          <div className="h-[72px] animate-pulse rounded-2xl bg-surface-2" />
          <span className="sr-only">{t('common.loading')}</span>
        </div>
      )}
      {users.isError && (
        <div className="flex flex-col gap-3">
          <Notice tone="error">{errorMessage(i18n, users.error)}</Notice>
          <Button icon={<Icon name="sync" />} onClick={() => void users.refetch()}>
            {t('common.retry')}
          </Button>
        </div>
      )}
      {users.data &&
        (users.data.length === 0 ? (
          <div className="flex flex-col items-center gap-3 rounded-2xl bg-surface-2 p-6 text-center">
            <p className="m-0 font-semibold">{t('users.empty')}</p>
            {filtered && <Button onClick={() => setFilters({})}>{t('users.filters.reset')}</Button>}
          </div>
        ) : (
          <ul className={`m-0 flex list-none flex-col gap-2 p-0 ${users.isPlaceholderData ? 'opacity-60' : ''}`}>
            {users.data.map((user) => (
              <li key={user.id}>
                <UserRow user={user} />
              </li>
            ))}
          </ul>
        ))}

      {creating && account && <CreateUserSheet actorRole={account.role} agencies={creatableAgencies} onClose={() => setCreating(false)} />}
    </div>
  );
}
