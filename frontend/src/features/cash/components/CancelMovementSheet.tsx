import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { formatAmount } from '@/shared/format/money';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { TextField } from '@/shared/ui/TextField';
import { cancelCashMovement } from '../api';
import { useCashAction } from '../queries';
import { CASH_REASON_MAX, type CashMovement } from '../types';

// Annuler un mouvement (maquette 09 ; Superviseur, cash.manage) : motif obligatoire, contre-passation au jour de
// l'annulation. Le mouvement reste dans la liste, barré, et ne compte plus dans les totaux. Exige le réseau.

export function CancelMovementSheet({ movement, onClose }: { movement: CashMovement; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const cancel = useCashAction((http, reason: string) => cancelCashMovement(http, movement.id, { reason }));
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<string | undefined>();
  const [serverError, setServerError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    if (!reason.trim()) {
      setReasonError(t('cash.cancel.reasonRequired'));
      return;
    }
    setReasonError(undefined);
    try {
      await cancel.mutateAsync(reason.trim());
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  const who = movement.isInternal
    ? `${movement.treasuryAccountName ?? '?'} → ${movement.destinationTreasuryAccountName ?? '?'}`
    : (movement.partyName ?? movement.treasuryAccountName ?? '');
  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('cash.cancel.title')} description={t('cash.cancel.hint')}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}
        <div className="flex items-center justify-between gap-3 rounded-xl bg-surface-2 p-3">
          <span className="flex min-w-0 flex-col">
            <span className="font-semibold">{t(`cash.types.${movement.type}`)}</span>
            <span className="truncate text-sm text-muted">{who}</span>
          </span>
          <span className="amount shrink-0">{formatAmount(movement.amount, movement.currency)}</span>
        </div>
        <TextField
          label={t('cash.cancel.reason')}
          value={reason}
          maxLength={CASH_REASON_MAX}
          error={reasonError}
          onChange={(e) => setReason(e.target.value)}
        />
        <Button type="submit" variant="danger" fullWidth icon={<Icon name="x" />} busy={cancel.isPending} busyLabel={t('cash.cancel.cancelling')}>
          {t('cash.cancel.confirm')}
        </Button>
      </form>
    </Sheet>
  );
}
