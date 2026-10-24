import { Link, useParams } from '@tanstack/react-router';
import { useId, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { useCurrentAccount } from '@/shared/auth/hooks';
import { formatDayTimeShort } from '@/shared/format/dates';
import { AgencyBadge } from '@/shared/ui/AgencyBadge';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { blockUser, resetUserPassword, unblockUser } from '../api';
import { LimitsPanel, PartyScopePanel, PermissionsPanel, SessionsPanel } from '../components/AccountPanels';
import { TemporaryPasswordView } from '../components/TemporaryPasswordView';
import { ConfirmSheet, ProfileSheet, RoleAgencySheet } from '../components/UserActionSheets';
import { useUser, useUserAction } from '../queries';
import { availableUserActions, statusMarks } from '../usersRules';

// Fiche d'un compte (maquette 13, « Actions sur le compte » ; users.manage) : nom, rôle, agence, état en mots, email,
// téléphone, dernière connexion, puis les actions permises (absentes sinon) : modifier le profil, bloquer (coupe les
// sessions), débloquer et réinitialiser (nouveau mot de passe temporaire affiché une seule fois), rôle et agence
// (compte de rôle Superviseur). Le mot de passe temporaire reste affiché sur la fiche jusqu'à « J'ai transmis ».
// Hors périmètre : 404 « introuvable ». Hors ligne : actions remplacées par une explication.
// Onglets (livraison 2) : Permissions, Tiers autorisés (compte de rôle Superviseur seulement), Plafonds, Sessions ;
// modifier exige un compte de rôle Superviseur (sessions : tout gestionnaire) et le réseau.

type Open = 'profile' | 'role' | 'block' | 'unblock' | 'reset' | null;
type Tab = 'permissions' | 'partyScope' | 'limits' | 'sessions';

export function UserDetailPage() {
  const { t, i18n } = useTranslation();
  const { userId } = useParams({ strict: false }) as { userId: string };
  const account = useCurrentAccount();
  const online = useOnlineStatus();
  const user = useUser(userId);
  const agencies = useAgencies();
  const block = useUserAction((http) => blockUser(http, userId));
  const unblock = useUserAction((http) => unblockUser(http, userId));
  const reset = useUserAction((http) => resetUserPassword(http, userId));
  const [open, setOpen] = useState<Open>(null);
  const [password, setPassword] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [tab, setTab] = useState<Tab>('permissions');
  const tabsId = useId();

  if (user.isPending) {
    return (
      <div role="status" className="flex items-center gap-2 text-muted">
        <Icon name="sync" spin />
        {t('common.loading')}
      </div>
    );
  }
  if (user.isError) {
    return (
      <div className="mx-auto flex w-full max-w-xl flex-col gap-3">
        <Notice tone="error">{errorMessage(i18n, user.error)}</Notice>
        <Link to="/users" className="font-semibold text-fg underline">
          {t('users.backToList')}
        </Link>
      </div>
    );
  }

  const data = user.data;
  const isSupervisor = account?.role === 'Supervisor';
  const actions = account ? availableUserActions(data, account) : null;
  const close = () => {
    setOpen(null);
    setActionError(null);
  };
  async function run(action: () => Promise<{ temporaryPassword: string } | void>) {
    setActionError(null);
    try {
      const result = await action();
      setOpen(null);
      if (result && 'temporaryPassword' in result) setPassword(result.temporaryPassword);
    } catch (error) {
      setActionError(errorMessage(i18n, error));
    }
  }

  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-4">
      <Link to="/users" className="inline-flex items-center gap-1 self-start font-semibold text-fg underline">
        <Icon name="chevronBack" className="size-5" />
        {t('users.backToList')}
      </Link>

      <section className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="m-0 flex-1 text-xl font-bold">{data.fullName}</h2>
          {data.agency && <AgencyBadge code={data.agency.code} color={data.agency.color} />}
        </div>
        <p className="m-0 flex flex-wrap gap-x-2 text-sm font-semibold">
          <span>{t(`roles.${data.role}`)}</span>
          {statusMarks(data).map((mark) => (
            <span key={mark}>· {mark === 'locked' && data.lockedUntil ? t('users.marks.lockedUntil', { time: formatDayTimeShort(data.lockedUntil) }) : t(`users.marks.${mark}`)}</span>
          ))}
        </p>
        <dl className="m-0 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-[15px]">
          <dt className="text-muted">{t('users.fields.email')}</dt>
          <dd className="m-0 break-all" dir="ltr">
            {data.email}
          </dd>
          <dt className="text-muted">{t('users.fields.phone')}</dt>
          <dd className="m-0" dir="ltr">
            {data.phone ?? '—'}
          </dd>
          <dt className="text-muted">{t('users.fields.lastLogin')}</dt>
          <dd className="m-0">{data.lastLoginAt ? formatDayTimeShort(data.lastLoginAt) : t('users.never')}</dd>
        </dl>
      </section>

      {password && (
        <section className="rounded-2xl border border-border bg-surface p-4">
          <TemporaryPasswordView password={password} owner={data.fullName} onDone={() => setPassword(null)} />
        </section>
      )}

      {actions &&
        (online ? (
          <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2" role="group" aria-label={t('users.actions')}>
            {actions.editProfile && (
              <Button icon={<Icon name="pencil" />} onClick={() => setOpen('profile')}>
                {t('users.profile.open')}
              </Button>
            )}
            {actions.roleAndAgency && (
              <Button icon={<Icon name="users" />} onClick={() => setOpen('role')}>
                {t('users.role.open')}
              </Button>
            )}
            {actions.resetPassword && (
              <Button icon={<Icon name="lock" />} onClick={() => setOpen('reset')}>
                {t('users.reset.open')}
              </Button>
            )}
            {actions.unblock && (
              <Button variant="primary" icon={<Icon name="unlock" />} onClick={() => setOpen('unblock')}>
                {t('users.unblock.open')}
              </Button>
            )}
            {actions.block && (
              <Button variant="danger" icon={<Icon name="ban" />} onClick={() => setOpen('block')}>
                {t('users.block.open')}
              </Button>
            )}
          </div>
        ) : (
          <Notice tone="info">{t('users.offline')}</Notice>
        ))}

      <TabsSection
        tabsId={tabsId}
        tab={tab}
        onTab={setTab}
        tabs={account?.role === 'Supervisor' ? ['permissions', 'partyScope', 'limits', 'sessions'] : ['permissions', 'limits', 'sessions']}
      >
        {tab === 'permissions' && <PermissionsPanel userId={data.id} targetIsSupervisor={data.role === 'Supervisor'} editable={isSupervisor && online && data.id !== account?.id} />}
        {tab === 'partyScope' && <PartyScopePanel userId={data.id} targetIsSupervisor={data.role === 'Supervisor'} editable={isSupervisor && online} />}
        {tab === 'limits' && <LimitsPanel userId={data.id} targetIsSupervisor={data.role === 'Supervisor'} editable={isSupervisor && online} />}
        {tab === 'sessions' && <SessionsPanel userId={data.id} editable={online} />}
      </TabsSection>

      {open === 'profile' && <ProfileSheet user={data} onClose={close} />}
      {open === 'role' && agencies.data && <RoleAgencySheet user={data} agencies={agencies.data} onClose={close} />}
      {open === 'block' && (
        <ConfirmSheet
          title={t('users.block.title', { name: data.fullName })}
          text={t('users.block.text')}
          confirmLabel={t('users.block.confirm')}
          danger
          busy={block.isPending}
          error={actionError}
          onConfirm={() => void run(() => block.mutateAsync(undefined))}
          onClose={close}
        />
      )}
      {open === 'unblock' && (
        <ConfirmSheet
          title={t('users.unblock.title', { name: data.fullName })}
          text={t('users.unblock.text')}
          confirmLabel={t('users.unblock.confirm')}
          busy={unblock.isPending}
          error={actionError}
          onConfirm={() => void run(() => unblock.mutateAsync(undefined))}
          onClose={close}
        />
      )}
      {open === 'reset' && (
        <ConfirmSheet
          title={t('users.reset.title', { name: data.fullName })}
          text={t('users.reset.text')}
          confirmLabel={t('users.reset.confirm')}
          busy={reset.isPending}
          error={actionError}
          onConfirm={() => void run(() => reset.mutateAsync(undefined))}
          onClose={close}
        />
      )}
    </div>
  );
}

/** Onglets de la fiche (role="tablist", flèches gauche / droite). */
function TabsSection({ tabsId, tab, onTab, tabs, children }: { tabsId: string; tab: Tab; onTab: (tab: Tab) => void; tabs: Tab[]; children: ReactNode }) {
  const { t } = useTranslation();
  const onKey = (event: KeyboardEvent) => {
    const index = tabs.indexOf(tab);
    if (event.key === 'ArrowRight') onTab(tabs[(index + 1) % tabs.length]!);
    if (event.key === 'ArrowLeft') onTab(tabs[(index - 1 + tabs.length) % tabs.length]!);
  };
  return (
    <section className="flex flex-col gap-3">
      <div role="tablist" aria-label={t('users.tabs.label')} className="flex overflow-x-auto border-b border-border" onKeyDown={onKey}>
        {tabs.map((value) => (
          <button
            key={value}
            type="button"
            role="tab"
            id={`${tabsId}-${value}`}
            aria-selected={tab === value}
            aria-controls={`${tabsId}-panel`}
            tabIndex={tab === value ? 0 : -1}
            onClick={() => onTab(value)}
            className="min-h-12 whitespace-nowrap px-4 font-semibold text-muted aria-selected:text-fg aria-selected:shadow-[inset_0_-3px_0_var(--accent)]"
          >
            {t(`users.tabs.${value}`)}
          </button>
        ))}
      </div>
      <div role="tabpanel" id={`${tabsId}-panel`} aria-labelledby={`${tabsId}-${tab}`}>
        {children}
      </div>
    </section>
  );
}
