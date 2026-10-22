import { Link, useParams } from '@tanstack/react-router';
import { useId, useMemo, useState, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { useCurrentAccount } from '@/shared/auth/hooks';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { CodeLockedChip, StatusChip } from '../components/StatusChip';
import { useTransfer, useTransferHistory } from '../queries';
import { availableActions, creatorName } from './detailRules';
import { TransferActions } from './TransferActions';
import { TransferDetails } from './TransferDetails';
import { TransferHistory } from './TransferHistory';

// Fiche d'un transfert (maquette 05) : statut en tête, onglets « Détails » et « Historique », actions selon les
// droits. Hors périmètre, le serveur répond 404 (« introuvable ») : on ne dit jamais qu'il existe ailleurs.

type Tab = 'details' | 'history';

export function TransferDetailPage() {
  const { t, i18n } = useTranslation();
  const { transferId } = useParams({ strict: false }) as { transferId: string };
  const account = useCurrentAccount();
  const transfer = useTransfer(transferId);
  const history = useTransferHistory(transferId);
  const agencies = useAgencies();
  const byId = useMemo(() => new Map((agencies.data ?? []).map((a) => [a.id, a])), [agencies.data]);
  const [tab, setTab] = useState<Tab>('details');
  const tabsId = useId();

  if (transfer.isPending) {
    return (
      <div role="status" className="flex items-center gap-2 text-muted">
        <Icon name="sync" spin />
        {t('common.loading')}
      </div>
    );
  }
  if (transfer.isError) {
    return (
      <div className="mx-auto flex w-full max-w-xl flex-col gap-3">
        <Notice tone="error">{errorMessage(i18n, transfer.error)}</Notice>
        <Link to="/transfers" className="font-semibold text-fg underline">
          {t('transfers.detail.backToList')}
        </Link>
      </div>
    );
  }

  const data = transfer.data;
  const actions = account ? availableActions(data, account) : { editTexts: false, cancel: false, newCode: false, unlock: false, editAmounts: false, deliver: false, deliverWithoutCode: false };
  const tabs: Tab[] = ['details', 'history'];
  const onTabKey = (event: KeyboardEvent) => {
    if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') setTab(tab === 'details' ? 'history' : 'details');
  };

  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <Link to="/transfers" className="inline-flex min-h-11 items-center gap-1 font-semibold text-fg no-underline">
          <Icon name="chevronBack" />
          {t('transfers.detail.backToList')}
        </Link>
        <span className="flex-1" />
        {data.isCodeLocked && data.status === 'NotDelivered' && <CodeLockedChip />}
        <StatusChip status={data.status} />
      </div>

      <div role="tablist" aria-label={t('transfers.detail.tabs')} className="flex gap-2" onKeyDown={onTabKey}>
        {tabs.map((value) => (
          <button
            key={value}
            type="button"
            role="tab"
            id={`${tabsId}-${value}`}
            aria-selected={tab === value}
            aria-controls={`${tabsId}-${value}-panel`}
            tabIndex={tab === value ? 0 : -1}
            onClick={() => setTab(value)}
            className="min-h-11 flex-1 border-b-[3px] border-border font-semibold text-muted aria-selected:border-accent aria-selected:text-fg"
          >
            {t(`transfers.detail.tab.${value}`)}
          </button>
        ))}
      </div>

      <div role="tabpanel" id={`${tabsId}-${tab}-panel`} aria-labelledby={`${tabsId}-${tab}`}>
        {tab === 'details' ? (
          <TransferDetails transfer={data} agencies={byId} createdBy={creatorName(history.data)} />
        ) : history.isError ? (
          <div className="flex flex-col gap-3">
            <Notice tone="error">{errorMessage(i18n, history.error)}</Notice>
            <Button icon={<Icon name="sync" />} onClick={() => void history.refetch()}>
              {t('common.retry')}
            </Button>
          </div>
        ) : history.data ? (
          <TransferHistory events={history.data} transfer={data} />
        ) : (
          <span className="text-muted">{t('common.loading')}</span>
        )}
      </div>

      <TransferActions
        transfer={data}
        actions={actions}
        onReload={() => {
          void transfer.refetch();
          void history.refetch();
        }}
      />
    </div>
  );
}
