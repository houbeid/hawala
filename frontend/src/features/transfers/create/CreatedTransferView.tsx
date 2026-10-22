import { useTranslation } from 'react-i18next';
import { formatAmount, formatRate } from '@/shared/format/money';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { WithdrawalCode } from '../components/WithdrawalCode';
import type { CreatedTransfer } from '../types';

// Succès (maquette 04) : SEUL écran où le code de retrait apparaît, en six grandes cases. Il n'est jamais
// relu ensuite (le serveur ne le garde que haché). « Imprimer » imprime cette carte (choix A du
// 22 octobre 2026) ; le vrai reçu PDF viendra en phase 7.

interface Props {
  created: CreatedTransfer;
  deliver: { amount: number; currency: 'MRO' | 'XOF' };
  paid: { amount: number; currency: 'MRO' | 'XOF' };
  /** Part à crédit et son tiers (null sans crédit). */
  credit: { amount: number; currency: 'MRO' | 'XOF'; name: string } | null;
  sender: string;
  beneficiary: string;
  onDone: () => void;
  onNew: () => void;
}

export function CreatedTransferView({ created, deliver, paid, credit, sender, beneficiary, onDone, onNew }: Props) {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-4">
      <section className="print-area flex flex-col gap-4 rounded-2xl border border-border bg-surface p-4" aria-labelledby="created-title">
        <div className="flex flex-col items-center gap-2 text-center">
          <span className="inline-flex size-16 items-center justify-center rounded-full bg-ink text-on-ink" aria-hidden="true">
            <Icon name="check" className="size-9" />
          </span>
          <h2 id="created-title" className="m-0 text-xl font-bold">
            {created.replayed ? t('transfers.create.done.replayedTitle') : t('transfers.create.done.title')}
          </h2>
          <span className="amount font-mono text-sm font-medium">{created.reference}</span>
        </div>

        {created.withdrawalCode ? (
          <WithdrawalCode code={created.withdrawalCode} />
        ) : (
          <Notice tone="info">{t('transfers.create.done.noCodeReplayed')}</Notice>
        )}

        <dl className="m-0 grid grid-cols-[1fr_auto] gap-2">
          <dt className="text-muted">{t('transfers.toDeliver')}</dt>
          <dd className="m-0 text-end">
            <span className="amount text-lg">{formatAmount(deliver.amount, deliver.currency)}</span>
          </dd>
          <dt className="text-muted">{t('transfers.create.done.paid')}</dt>
          <dd className="m-0 text-end">
            <span className="amount">{formatAmount(paid.amount, paid.currency)}</span>
          </dd>
          {credit && (
            <>
              <dt className="text-muted">{t('transfers.create.recapCredit', { name: credit.name })}</dt>
              <dd className="m-0 text-end">
                <span className="amount">{formatAmount(credit.amount, credit.currency)}</span>
              </dd>
            </>
          )}
          <dt className="text-muted">{t('transfers.create.rate')}</dt>
          <dd className="m-0 text-end">
            <span className="amount text-sm">{formatRate(created.saleRate)}</span>
          </dd>
        </dl>
        <div className="flex flex-wrap items-center gap-1.5">
          <span>{sender}</span>
          <Icon name="arrow" className="size-4 text-muted" />
          <b>{beneficiary}</b>
        </div>
      </section>

      {created.withdrawalCode && <Notice tone="warning">{t('transfers.create.done.codeOnce')}</Notice>}
      <Button fullWidth icon={<Icon name="doc" />} onClick={() => window.print()}>
        {t('transfers.create.done.print')}
      </Button>
      <Button variant="primary" fullWidth onClick={onDone}>
        {t('transfers.create.done.finish')}
      </Button>
      <Button fullWidth icon={<Icon name="plus" />} onClick={onNew}>
        {t('transfers.new')}
      </Button>
    </div>
  );
}
