import { useTranslation } from 'react-i18next';
import { formatAmount } from '@/shared/format/money';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';

// Transfert saisi sans réseau (décisions du 24 octobre 2026) : gardé sur ce téléphone, envoyé au retour du réseau.
// Le code de retrait n'existe pas encore (le serveur le tire à l'envoi) : il sera montré à l'agent, avec le reçu,
// dans la liste des transferts « en attente ».

interface Props {
  amountXof: number;
  sender: string;
  beneficiary: string;
  onDone: () => void;
  onNew: () => void;
}

export function QueuedTransferView({ amountXof, sender, beneficiary, onDone, onNew }: Props) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-4">
      <section className="flex flex-col items-center gap-2 rounded-2xl border border-border bg-surface p-4 text-center" aria-labelledby="queued-title">
        <span className="inline-flex size-16 items-center justify-center rounded-full bg-surface-2" aria-hidden="true">
          <Icon name="clock" className="size-9" />
        </span>
        <h2 id="queued-title" className="m-0 text-xl font-bold">
          {t('queue.transferSavedTitle')}
        </h2>
        <span className="amount text-lg">{formatAmount(amountXof, 'XOF')}</span>
        <span className="flex flex-wrap items-center justify-center gap-1.5">
          {sender}
          <Icon name="arrow" className="size-4 text-muted" />
          <b>{beneficiary}</b>
        </span>
      </section>
      <Notice tone="warning">{t('queue.transferSavedHint')}</Notice>
      <Button variant="primary" fullWidth onClick={onDone}>
        {t('transfers.create.done.finish')}
      </Button>
      <Button fullWidth icon={<Icon name="plus" />} onClick={onNew}>
        {t('transfers.new')}
      </Button>
    </div>
  );
}
