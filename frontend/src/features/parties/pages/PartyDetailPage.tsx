import { Link, useParams } from '@tanstack/react-router';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { usePermission } from '@/shared/auth/hooks';
import { AgencyBadge } from '@/shared/ui/AgencyBadge';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { ArchiveSheet } from '../components/ArchiveSheet';
import { BalanceAmount } from '../components/BalanceAmount';
import { CreditLimitInfo } from '../components/CreditLimitInfo';
import { CreditLimitSheet } from '../components/CreditLimitSheet';
import { EditPartySheet } from '../components/EditPartySheet';
import { OpeningBalanceSheet } from '../components/OpeningBalanceSheet';
import { useParty } from '../queries';

// Fiche d'un tiers (maquettes 08 et D3) : identité, puis une carte par agence visible avec son solde (dans sa
// devise), son plafond de crédit et le lien vers son relevé. Sans parties.view_balance, le serveur ne renvoie
// aucun solde : seules les informations de la fiche s'affichent. Hors de la liste d'un compte limité : 404.
// Actions du Superviseur (parties.manage) : modifier la fiche, archiver / désarchiver, et dans chaque agence le
// solde initial et le plafond de crédit. Un tiers archivé ne se modifie plus (le serveur répond Parties.Party.Archived) :
// seul « Désarchiver » reste. Hors ligne, les actions sont remplacées par une explication.

type Open = { kind: 'edit' } | { kind: 'archive' } | { kind: 'opening' | 'limit'; agencyId: string } | null;

function Field({ label, value, ltr = false }: { label: string; value: string | null; ltr?: boolean }) {
  if (!value) return null;
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-sm text-muted">{label}</dt>
      <dd className="m-0 break-words" dir={ltr ? 'ltr' : undefined}>
        {value}
      </dd>
    </div>
  );
}

export function PartyDetailPage() {
  const { t, i18n } = useTranslation();
  const { partyId } = useParams({ strict: false }) as { partyId: string };
  const party = useParty(partyId);
  const agencies = useAgencies();
  const byId = useMemo(() => new Map((agencies.data ?? []).map((a) => [a.id, a])), [agencies.data]);
  const canManage = usePermission('parties.manage');
  const online = useOnlineStatus();
  const [open, setOpen] = useState<Open>(null);

  if (party.isPending) {
    return (
      <div role="status" className="flex items-center gap-2 text-muted">
        <Icon name="sync" spin />
        {t('common.loading')}
      </div>
    );
  }
  if (party.isError) {
    return (
      <div className="mx-auto flex w-full max-w-xl flex-col gap-3">
        <Notice tone="error">{errorMessage(i18n, party.error)}</Notice>
        <Link to="/parties" className="font-semibold text-fg underline">
          {t('parties.detail.backToList')}
        </Link>
      </div>
    );
  }

  const data = party.data;
  const manageable = canManage && online;
  const editable = manageable && !data.isArchived;
  const close = () => setOpen(null);
  const agencyName = (agencyId: string) => {
    const agency = byId.get(agencyId);
    return agency ? t(`agencies.${agency.code}`, { defaultValue: agency.name }) : '';
  };
  const openedBalance = open && (open.kind === 'opening' || open.kind === 'limit') ? data.balances?.find((b) => b.agencyId === open.agencyId) : undefined;
  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-4">
      <section className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4">
        <div className="flex flex-col gap-1">
          <h2 className="m-0 text-xl font-bold">{data.name}</h2>
          <p className="m-0 flex flex-wrap gap-x-2 text-sm text-muted">
            <span>
              {t(`parties.types.${data.type}`)}
              {data.accountCode && ` · ${t('parties.detail.accountCode', { code: data.accountCode })}`}
            </span>
            <span className="font-semibold">{data.inBalanceSheet ? t('parties.inBalanceSheet') : t('parties.outOfBalanceSheet')}</span>
            {data.isArchived && <span className="font-semibold">{t('parties.archived')}</span>}
          </p>
        </div>
        <dl className="m-0 flex flex-col gap-2.5">
          <Field label={t('parties.detail.phone')} value={data.phone} ltr />
          <Field label={t('parties.detail.nni')} value={data.nni} ltr />
          <Field label={t('parties.detail.address')} value={data.address} />
          <Field label={t('parties.detail.note')} value={data.note} />
        </dl>
      </section>

      {canManage && !online && <Notice tone="info">{t('parties.manage.offline')}</Notice>}
      {manageable && (
        <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2" role="group" aria-label={t('parties.manage.label')}>
          {!data.isArchived && (
            <Button icon={<Icon name="pencil" />} onClick={() => setOpen({ kind: 'edit' })}>
              {t('parties.manage.edit')}
            </Button>
          )}
          {data.isArchived ? (
            <Button variant="primary" icon={<Icon name="sync" />} onClick={() => setOpen({ kind: 'archive' })}>
              {t('parties.manage.unarchive')}
            </Button>
          ) : (
            <Button variant="danger" icon={<Icon name="ban" />} onClick={() => setOpen({ kind: 'archive' })}>
              {t('parties.manage.archive')}
            </Button>
          )}
        </div>
      )}

      {data.balances && data.balances.length > 0 && (
        <section className="flex flex-col gap-2">
          <h2 className="m-0 text-sm font-semibold text-muted">{t('parties.detail.balances')}</h2>
          <div className={`grid gap-3 ${data.balances.length > 1 ? 'sm:grid-cols-2' : ''}`}>
            {data.balances.map((balance) => {
              const agency = byId.get(balance.agencyId);
              return (
                <article key={balance.agencyId} className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    {agency ? (
                      <AgencyBadge code={agency.code} color={agency.color} label={t(`agencies.${agency.code}`, { defaultValue: agency.name })} />
                    ) : (
                      <span />
                    )}
                    <BalanceAmount balance={balance.balance} currency={balance.currency} size="lg" />
                  </div>
                  <CreditLimitInfo balance={balance.balance} creditLimit={balance.creditLimit} currency={balance.currency} />
                  {editable && (
                    <div className="grid grid-cols-2 gap-2">
                      <button
                        type="button"
                        onClick={() => setOpen({ kind: 'opening', agencyId: balance.agencyId })}
                        aria-label={t('parties.manage.openingFor', { agency: agencyName(balance.agencyId) })}
                        className="min-h-12 rounded-[14px] border-[1.5px] border-border px-3 font-semibold"
                      >
                        {t('parties.manage.opening')}
                      </button>
                      <button
                        type="button"
                        onClick={() => setOpen({ kind: 'limit', agencyId: balance.agencyId })}
                        aria-label={t('parties.manage.limitFor', { agency: agencyName(balance.agencyId) })}
                        className="min-h-12 rounded-[14px] border-[1.5px] border-border px-3 font-semibold"
                      >
                        {t('parties.manage.limit')}
                      </button>
                    </div>
                  )}
                  <Link
                    to="/parties/$partyId/statement"
                    params={{ partyId: data.id }}
                    search={{ agencyId: balance.agencyId }}
                    className="inline-flex min-h-12 items-center justify-center gap-2 rounded-[14px] border-2 border-ink px-4 font-semibold text-fg no-underline"
                  >
                    <Icon name="doc" />
                    {t('parties.detail.statement')}
                  </Link>
                </article>
              );
            })}
          </div>
        </section>
      )}

      {open?.kind === 'edit' && <EditPartySheet party={data} onClose={close} />}
      {open?.kind === 'archive' && <ArchiveSheet party={data} onClose={close} />}
      {open?.kind === 'opening' && openedBalance && (
        <OpeningBalanceSheet partyId={data.id} balance={openedBalance} agencyName={agencyName(openedBalance.agencyId)} onClose={close} />
      )}
      {open?.kind === 'limit' && openedBalance && (
        <CreditLimitSheet partyId={data.id} balance={openedBalance} agencyName={agencyName(openedBalance.agencyId)} onClose={close} />
      )}
    </div>
  );
}
