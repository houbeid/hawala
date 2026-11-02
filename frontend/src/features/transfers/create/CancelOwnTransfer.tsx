import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { TextField } from '@/shared/ui/TextField';
import { cancelOwnTransfer } from '../api';
import { useTransferAction } from '../queries';

// Annulation par l'auteur sur l'écran du code (décision du patron, 2 novembre 2026) : motif facultatif, une
// confirmation. Le serveur refuse si le transfert a déjà été touché ou s'il est trop tard (le Superviseur annule).

const REASON_MAX = 500;

interface Props {
  transferId: string;
  /** Vrai si une part du transfert est à crédit : la dette du tiers sera effacée aussi. */
  hasCredit: boolean;
  onCancelled: () => void;
}

export function CancelOwnTransfer({ transferId, hasCredit, onCancelled }: Props) {
  const { t, i18n } = useTranslation();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [failure, setFailure] = useState<string | null>(null);
  const cancel = useTransferAction((http, reasonText: string | null) => cancelOwnTransfer(http, transferId, reasonText));

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);
    try {
      await cancel.mutateAsync(reason.trim() || null);
      setOpen(false);
      onCancelled();
    } catch (error) {
      setFailure(errorMessage(i18n, error));
    }
  }

  return (
    <>
      <Button variant="danger" fullWidth icon={<Icon name="x" />} onClick={() => setOpen(true)}>
        {t('transfers.create.done.cancel')}
      </Button>
      {open && (
        <Sheet open onOpenChange={(value) => !value && setOpen(false)} title={t('transfers.create.done.cancelTitle')}>
          <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
            <Notice tone="warning">{t('transfers.create.done.cancelWarning')}</Notice>
            {hasCredit && <Notice tone="info">{t('transfers.create.done.cancelCredit')}</Notice>}
            {failure && <Notice tone="error">{failure}</Notice>}
            <TextField
              label={t('transfers.create.done.cancelReason')}
              value={reason}
              maxLength={REASON_MAX}
              onChange={(e) => setReason(e.target.value)}
            />
            <Button
              type="submit"
              variant="danger"
              fullWidth
              icon={<Icon name="x" />}
              busy={cancel.isPending}
              busyLabel={t('transfers.detail.saving')}
            >
              {t('transfers.create.done.cancelConfirm')}
            </Button>
            <Button fullWidth onClick={() => setOpen(false)}>
              {t('transfers.create.done.keep')}
            </Button>
          </form>
        </Sheet>
      )}
    </>
  );
}
