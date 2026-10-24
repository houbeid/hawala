import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { StatementBody, StatementPeriod } from '@/shared/ui/Statement';
import { BalanceAmount } from '../components/BalanceAmount';
import { parseStatementSearch, type StatementSearch } from '../partyRules';
import { usePartyStatement } from '../queries';

// Relevé d'un tiers dans une agence (maquette 08, règle validée) : solde au début, une ligne par écriture avec
// le solde après, solde à la fin ; sans dates, le mois en cours (choisi par le serveur). Permission
// parties.view_balance. Un mouvement s'écrit avec son sens en clair : « + » ajoute à ce qu'il nous doit,
// « − » ajoute à ce qu'on lui doit (légende affichée), le solde après garde ses mots. Mise en page partagée avec
// le relevé d'un compte de trésorerie (shared/ui/Statement). Chaque ligne est titrée par son type d'écriture traduit
// (`parties.entryTypes`, vu du côté du tiers) ; le libellé du serveur (référence d'un transfert) reste dessous, et
// sert de titre pour un type inconnu.

export function PartyStatementPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { partyId } = useParams({ strict: false }) as { partyId: string };
  const search = parseStatementSearch(useSearch({ strict: false }) as Record<string, unknown>);
  const statement = usePartyStatement(partyId, search);

  const setPeriod = (patch: Partial<StatementSearch>) =>
    void navigate({ to: '/parties/$partyId/statement', params: { partyId }, search: { ...search, ...patch }, replace: true });

  const data = statement.data;
  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-4">
      <Link to="/parties/$partyId" params={{ partyId }} className="inline-flex items-center gap-1 self-start font-semibold text-fg underline">
        <Icon name="chevronBack" className="size-5" />
        {t('parties.statement.backToParty')}
      </Link>

      <StatementPeriod from={search.from ?? data?.from} to={search.to ?? data?.to} onChange={setPeriod} />

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

      {data && (
        <StatementBody
          title={data.partyName}
          legend={t('parties.statement.legend')}
          currency={data.currency}
          from={data.from}
          to={data.to}
          openingBalance={data.openingBalance}
          closingBalance={data.closingBalance}
          rows={data.lines.map((line, index) => {
            const title = t(`parties.entryTypes.${line.entryType}`, { defaultValue: line.label });
            return {
              key: `${line.recordedAt}-${index}`,
              operationDate: line.operationDate,
              title,
              subtitle: title === line.label ? null : line.label,
              reason: line.reason,
              isReversal: line.isReversal,
              // Une écriture d'un tiers ne porte qu'un côté ; par sûreté, on montre la différence.
              delta: line.debit - line.credit,
              balanceAfter: line.balanceAfter,
            };
          })}
          deltaClass={(delta) => (delta > 0 ? 'text-red' : delta < 0 ? 'text-green' : '')}
          renderBalance={(balance, size) => <BalanceAmount balance={balance} currency={data.currency} size={size} />}
          stale={statement.isPlaceholderData}
        />
      )}
    </div>
  );
}
