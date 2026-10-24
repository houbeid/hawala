import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { useSessionManager } from '@/shared/auth/hooks';
import { shareOrDownload, type FileOutcome } from '@/shared/files/shareFile';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { getTransferReceipt } from '../api';

// Reçu PDF de l'expéditeur (décisions du 24 octobre 2026) : pas d'imprimante en agence ; le reçu est produit par le
// serveur (bilingue fr + ar) puis partagé par le téléphone (WhatsApp…) ou téléchargé. Avec `code` : le reçu de la
// création ou d'un nouveau code, qui porte le code ; le code ne reste qu'en mémoire, le temps de l'écran.
// Hors ligne : une explication à la place du bouton (le reçu exige le serveur).

interface Props {
  transferId: string;
  reference: string;
  /** Code de retrait à écrire sur le reçu ; absent = duplicata sans code. */
  code?: string;
  variant?: 'primary' | 'secondary';
}

export function ReceiptButton({ transferId, reference, code, variant = 'secondary' }: Props) {
  const { t, i18n } = useTranslation();
  const session = useSessionManager();
  const online = useOnlineStatus();
  const [busy, setBusy] = useState(false);
  const [outcome, setOutcome] = useState<FileOutcome | null>(null);
  const [error, setError] = useState<string | null>(null);

  if (!online) return <Notice tone="info">{t('transfers.receipt.offline')}</Notice>;

  async function send() {
    setBusy(true);
    setError(null);
    setOutcome(null);
    try {
      const pdf = await getTransferReceipt(session.http, transferId, code);
      setOutcome(await shareOrDownload(pdf, `Recu-${reference}.pdf`));
    } catch (failure) {
      setError(errorMessage(i18n, failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-2">
      <Button variant={variant} fullWidth icon={<Icon name="doc" />} busy={busy} busyLabel={t('transfers.receipt.busy')} onClick={() => void send()}>
        {code === undefined ? t('transfers.receipt.duplicate') : t('transfers.receipt.withCode')}
      </Button>
      {code !== undefined && <p className="m-0 text-sm text-muted">{t('transfers.receipt.codeHint')}</p>}
      {outcome === 'shared' && <Notice tone="info">{t('transfers.receipt.shared')}</Notice>}
      {outcome === 'downloaded' && <Notice tone="info">{t('transfers.receipt.downloaded')}</Notice>}
      {error && <Notice tone="error">{error}</Notice>}
    </div>
  );
}
