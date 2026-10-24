import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { usePermission } from '@/shared/auth/hooks';
import { formatAmount } from '@/shared/format/money';
import { AgencyBadge } from '@/shared/ui/AgencyBadge';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { StatementBody, StatementPeriod } from '@/shared/ui/Statement';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { AccountActionsSheet } from '../components/AccountActionsSheet';
import { useTreasuryAccount, useTreasuryStatement } from '../queries';
import { parsePeriodSearch, type PeriodSearch } from '../treasurySearch';

// Fiche d'un compte de trésorerie (maquette 14, « Fiche d'un compte · mouvements ») : nom, type, agence, état, solde,
// puis son relevé lu au grand livre (décision du 22 octobre 2026 : TOUS les mouvements, transferts compris, pas
// seulement ceux de la Caisse), et les actions avec treasury.manage (renommer, désactiver, réactiver).
// Le relevé exige cash.budget.read, comme les soldes ; sans elle, la fiche reste lisible sans montant.
// Chaque ligne est titrée par son type d'écriture traduit ; le libellé du serveur (référence d'un transfert)
// reste dessous. Un type inconnu affiche le libellé du serveur.

export function TreasuryAccountPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { accountId } = useParams({ strict: false }) as { accountId: string };
  const search = parsePeriodSearch(useSearch({ strict: false }) as Record<string, unknown>);
  const canReadBalances = usePermission('cash.budget.read');
  const canManage = usePermission('treasury.manage');
  const online = useOnlineStatus();
  const account = useTreasuryAccount(accountId);
  const statement = useTreasuryStatement(accountId, search, canReadBalances);
  const agencies = useAgencies();
  const agency = useMemo(() => agencies.data?.find((a) => a.id === account.data?.agencyId), [agencies.data, account.data]);
  const [managing, setManaging] = useState(false);

  if (account.isPending) {
    return (
      <div role="status" className="flex items-center gap-2 text-muted">
        <Icon name="sync" spin />
        {t('common.loading')}
      </div>
    );
  }
  if (account.isError) {
    return (
      <div className="mx-auto flex w-full max-w-xl flex-col gap-3">
        <Notice tone="error">{errorMessage(i18n, account.error)}</Notice>
        <Link to="/treasury" className="font-semibold text-fg underline">
          {t('treasury.detail.backToList')}
        </Link>
      </div>
    );
  }

  const data = account.data;
  const setPeriod = (patch: Partial<PeriodSearch>) =>
    void navigate({ to: '/treasury/$accountId', params: { accountId }, search: { ...search, ...patch }, replace: true });
  const lines = statement.data;

  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-4">
      <Link to="/treasury" className="inline-flex items-center gap-1 self-start font-semibold text-fg underline">
        <Icon name="chevronBack" className="size-5" />
        {t('treasury.detail.backToList')}
      </Link>

      <section className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4">
        <div className="flex items-start gap-3">
          <span className="inline-flex size-11 shrink-0 items-center justify-center rounded-xl bg-surface-2">
            <Icon name={data.type === 'Cash' ? 'cash' : 'bank'} />
          </span>
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <h2 className="m-0 break-words text-xl font-bold">{data.name}</h2>
            <p className="m-0 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted">
              {agency && <AgencyBadge code={agency.code} color={agency.color} label={t(`agencies.${agency.code}`, { defaultValue: agency.name })} />}
              <span>{t(`treasury.types.${data.type}`)}</span>
              <span className="inline-flex items-center gap-1 font-semibold">
                <Icon name={data.isActive ? 'check' : 'ban'} className="size-4" />
                {data.isActive ? t('treasury.active') : t('treasury.inactive')}
              </span>
            </p>
          </div>
        </div>
        {data.balance !== null && (
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <span className="text-sm text-muted">{t('treasury.balance')}</span>
            <span className={`amount text-2xl ${data.balance < 0 ? 'text-red' : ''}`}>{formatAmount(data.balance, data.currency)}</span>
          </div>
        )}
      </section>

      {canManage &&
        (online ? (
          <Button icon={<Icon name="pencil" />} onClick={() => setManaging(true)}>
            {t('treasury.detail.manage')}
          </Button>
        ) : (
          <Notice tone="info">{t('treasury.offline')}</Notice>
        ))}

      {canReadBalances ? (
        <>
          <StatementPeriod from={search.from ?? lines?.from} to={search.to ?? lines?.to} onChange={setPeriod} />
          {statement.isPending && (
            <div role="status" className="flex items-center gap-2 text-muted">
              <Icon name="sync" spin />
              {t('common.loading')}
            </div>
          )}
          {statement.isError && (
            <div className="flex flex-col gap-3">
              <Notice tone="error">{errorMessage(i18n, statement.error)}</Notice>
              <Button icon={<Icon name="sync" />} onClick={() => void statement.refetch()}>
                {t('common.retry')}
              </Button>
            </div>
          )}
          {lines && (
            <StatementBody
              title={t('treasury.detail.statement')}
              legend={t('treasury.detail.legend')}
              currency={lines.currency}
              from={lines.from}
              to={lines.to}
              openingBalance={lines.openingBalance}
              closingBalance={lines.closingBalance}
              rows={lines.lines.map((line, index) => {
                const title = t(`treasury.entryTypes.${line.entryType}`, { defaultValue: line.label });
                return {
                  key: `${line.recordedAt}-${index}`,
                  operationDate: line.operationDate,
                  title,
                  subtitle: title === line.label ? null : line.label,
                  reason: line.reason,
                  isReversal: line.isReversal,
                  delta: line.debit - line.credit,
                  balanceAfter: line.balanceAfter,
                };
              })}
              deltaClass={() => ''}
              renderBalance={(balance, size) => (
                <span className={`amount ${size === 'lg' ? 'text-xl' : ''} ${balance < 0 ? 'text-red' : ''}`}>{formatAmount(balance, lines.currency)}</span>
              )}
              stale={statement.isPlaceholderData}
            />
          )}
        </>
      ) : (
        <Notice tone="info">{t('treasury.detail.noStatement')}</Notice>
      )}

      {managing && <AccountActionsSheet key={data.id} account={data} onClose={() => setManaging(false)} />}
    </div>
  );
}
