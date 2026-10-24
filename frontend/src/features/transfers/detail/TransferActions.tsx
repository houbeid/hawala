import { Link } from '@tanstack/react-router';
import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { isApiError } from '@/shared/api/errors';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { TextField } from '@/shared/ui/TextField';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { cancelTransfer, editTransferTexts, newTransferCode, unlockTransferCode } from '../api';
import { ReceiptButton } from '../components/ReceiptButton';
import { WithdrawalCode } from '../components/WithdrawalCode';
import { useTransferAction } from '../queries';
import type { EditTransferTextsRequest, NewWithdrawalCode, Transfer } from '../types';
import type { DetailActions } from './detailRules';

// Actions de la fiche (maquette 05) : grille de boutons de 56 px ; « Annuler » en contour rouge. Chaque action
// ouvre une fenêtre (Radix) avec son motif. Hors ligne, les actions sont remplacées par une explication : elles
// exigent l'état du serveur à jour (rien ne se met en file d'attente sauf les créations).

const VERSION_CONFLICT = 'Transfers.Transfer.VersionConflict';
const REASON_MAX = 500;

type Open = 'texts' | 'cancel' | 'unlock' | 'newCode' | null;

interface Props {
  transfer: Transfer;
  actions: DetailActions;
  /** Relit la fiche (conflit de version : quelqu'un a modifié le transfert entre-temps). */
  onReload: () => void;
}

export function TransferActions({ transfer, actions, onReload }: Props) {
  const { t } = useTranslation();
  const online = useOnlineStatus();
  const [open, setOpen] = useState<Open>(null);
  const any =
    actions.editTexts ||
    actions.cancel ||
    actions.newCode ||
    actions.unlock ||
    actions.editAmounts ||
    actions.deliver ||
    actions.deliverWithoutCode ||
    actions.receipt;
  if (!any) return null;

  if (!online) {
    return <Notice tone="info">{t('transfers.detail.actions.offline')}</Notice>;
  }

  const close = () => setOpen(null);
  return (
    <section className="flex flex-col gap-2" aria-label={t('transfers.detail.actions.label')}>
      <h2 className="m-0 text-sm font-semibold text-muted">{t('transfers.detail.actions.label')}</h2>
      {/* Action principale (maquette 05) : « Livrer », pleine largeur. */}
      {actions.deliver && (
        <Link
          to="/transfers/$transferId/deliver"
          params={{ transferId: transfer.id }}
          className="inline-flex min-h-14 items-center justify-center gap-2 rounded-[14px] border-2 border-ink bg-ink px-5 text-[17px] font-semibold text-on-ink no-underline"
        >
          <Icon name="check" />
          {t('transfers.detail.actions.deliver')}
        </Link>
      )}
      <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2">
        {actions.deliverWithoutCode && (
          <Link
            to="/transfers/$transferId/deliver-without-code"
            params={{ transferId: transfer.id }}
            className="inline-flex min-h-14 items-center justify-center gap-2 rounded-[14px] border-2 border-ink px-5 text-[17px] font-semibold text-fg no-underline"
          >
            <Icon name="doc" />
            {t('transfers.detail.actions.deliverWithoutCode')}
          </Link>
        )}
        {actions.unlock && (
          <Button icon={<Icon name="unlock" />} onClick={() => setOpen('unlock')}>
            {t('transfers.detail.actions.unlock')}
          </Button>
        )}
        {actions.editTexts && (
          <Button icon={<Icon name="pencil" />} onClick={() => setOpen('texts')}>
            {t('transfers.detail.actions.editTexts')}
          </Button>
        )}
        {actions.editAmounts && (
          <Link
            to="/transfers/$transferId/amounts"
            params={{ transferId: transfer.id }}
            className="inline-flex min-h-14 items-center justify-center gap-2 rounded-[14px] border-2 border-ink px-5 text-[17px] font-semibold text-fg no-underline"
          >
            <Icon name="pencil" />
            {t('transfers.detail.actions.editAmounts')}
          </Link>
        )}
        {actions.newCode && (
          <Button icon={<Icon name="sync" />} onClick={() => setOpen('newCode')}>
            {t('transfers.detail.actions.newCode')}
          </Button>
        )}
        {actions.receipt && <ReceiptButton transferId={transfer.id} reference={transfer.reference} />}
        {actions.cancel && (
          <Button variant="danger" icon={<Icon name="x" />} onClick={() => setOpen('cancel')}>
            {t('transfers.detail.actions.cancel')}
          </Button>
        )}
      </div>

      {open === 'texts' && <EditTextsSheet transfer={transfer} onClose={close} onReload={onReload} />}
      {open === 'cancel' && <CancelSheet transfer={transfer} onClose={close} />}
      {open === 'unlock' && <UnlockSheet transfer={transfer} onClose={close} />}
      {open === 'newCode' && <NewCodeSheet transfer={transfer} onClose={close} />}
    </section>
  );
}

function useServerError() {
  const { i18n } = useTranslation();
  const [message, setMessage] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  return {
    message,
    conflict,
    clear: () => {
      setMessage(null);
      setConflict(false);
    },
    show: (error: unknown) => {
      setConflict(isApiError(error) && error.code === VERSION_CONFLICT);
      setMessage(errorMessage(i18n, error));
    },
  };
}

/** Expéditeur, bénéficiaire, note : état complet, version lue sur la fiche, motif facultatif. */
function EditTextsSheet({ transfer, onClose, onReload }: { transfer: Transfer; onClose: () => void; onReload: () => void }) {
  const { t } = useTranslation();
  const edit = useTransferAction((http, request: EditTransferTextsRequest) => editTransferTexts(http, transfer.id, request));
  const server = useServerError();
  const [sender, setSender] = useState(transfer.senderInfo);
  const [beneficiary, setBeneficiary] = useState(transfer.beneficiaryInfo);
  const [note, setNote] = useState(transfer.note ?? '');
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<{ sender?: string; beneficiary?: string }>({});

  async function submit(event: FormEvent) {
    event.preventDefault();
    server.clear();
    const found = {
      sender: sender.trim() ? undefined : t('transfers.create.validation.senderRequired'),
      beneficiary: beneficiary.trim() ? undefined : t('transfers.create.validation.beneficiaryRequired'),
    };
    setErrors(found);
    if (found.sender || found.beneficiary) return;
    try {
      await edit.mutateAsync({
        version: transfer.version,
        senderInfo: sender.trim(),
        beneficiaryInfo: beneficiary.trim(),
        note: note.trim() || null,
        reason: reason.trim() || null,
      });
      onClose();
    } catch (error) {
      server.show(error);
    }
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('transfers.detail.actions.editTexts')}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {server.message && <Notice tone="error">{server.message}</Notice>}
        {server.conflict && (
          <Button
            icon={<Icon name="sync" />}
            onClick={() => {
              onReload();
              onClose();
            }}
          >
            {t('transfers.detail.reload')}
          </Button>
        )}
        <TextField label={t('transfers.create.sender')} value={sender} maxLength={200} error={errors.sender} onChange={(e) => setSender(e.target.value)} />
        <TextField
          label={t('transfers.create.beneficiary')}
          value={beneficiary}
          maxLength={200}
          error={errors.beneficiary}
          onChange={(e) => setBeneficiary(e.target.value)}
        />
        <TextField label={t('transfers.create.note')} value={note} maxLength={500} onChange={(e) => setNote(e.target.value)} />
        <TextField label={t('transfers.detail.reasonOptional')} value={reason} maxLength={REASON_MAX} onChange={(e) => setReason(e.target.value)} />
        <Button type="submit" variant="primary" fullWidth busy={edit.isPending} busyLabel={t('transfers.detail.saving')}>
          {t('transfers.detail.save')}
        </Button>
      </form>
    </Sheet>
  );
}

/** Annulation (Superviseur) : motif obligatoire, contre-passation au jour de l'annulation (maintenant). */
function CancelSheet({ transfer, onClose }: { transfer: Transfer; onClose: () => void }) {
  const { t } = useTranslation();
  const cancel = useTransferAction((http, reasonText: string) => cancelTransfer(http, transfer.id, { reason: reasonText, operationAt: null }));
  const server = useServerError();
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<string | undefined>();

  async function submit(event: FormEvent) {
    event.preventDefault();
    server.clear();
    if (!reason.trim()) {
      setReasonError(t('transfers.detail.reasonRequired'));
      return;
    }
    setReasonError(undefined);
    try {
      await cancel.mutateAsync(reason.trim());
      onClose();
    } catch (error) {
      server.show(error);
    }
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('transfers.detail.cancelTitle')}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        <Notice tone="warning">{t('transfers.detail.cancelWarning')}</Notice>
        {transfer.credit && <Notice tone="info">{t('transfers.detail.cancelCredit', { name: transfer.credit.partyName ?? '' })}</Notice>}
        {server.message && <Notice tone="error">{server.message}</Notice>}
        <TextField
          label={t('transfers.detail.reasonRequiredLabel')}
          value={reason}
          maxLength={REASON_MAX}
          error={reasonError}
          onChange={(e) => setReason(e.target.value)}
        />
        <Button type="submit" variant="danger" fullWidth icon={<Icon name="x" />} busy={cancel.isPending} busyLabel={t('transfers.detail.saving')}>
          {t('transfers.detail.actions.cancel')}
        </Button>
        <Button fullWidth onClick={onClose}>
          {t('transfers.detail.keep')}
        </Button>
      </form>
    </Sheet>
  );
}

/** Déblocage du code (Superviseur) : le compteur repart à zéro ; motif facultatif. */
function UnlockSheet({ transfer, onClose }: { transfer: Transfer; onClose: () => void }) {
  const { t } = useTranslation();
  const unlock = useTransferAction((http, reasonText: string | null) => unlockTransferCode(http, transfer.id, reasonText));
  const server = useServerError();
  const [reason, setReason] = useState('');

  async function submit(event: FormEvent) {
    event.preventDefault();
    server.clear();
    try {
      await unlock.mutateAsync(reason.trim() || null);
      onClose();
    } catch (error) {
      server.show(error);
    }
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('transfers.detail.actions.unlock')} description={t('transfers.detail.unlockHint')}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {server.message && <Notice tone="error">{server.message}</Notice>}
        <TextField label={t('transfers.detail.reasonOptional')} value={reason} maxLength={REASON_MAX} onChange={(e) => setReason(e.target.value)} />
        <Button type="submit" variant="primary" fullWidth busy={unlock.isPending} busyLabel={t('transfers.detail.saving')}>
          {t('transfers.detail.actions.unlock')}
        </Button>
      </form>
    </Sheet>
  );
}

/** Nouveau code : l'ancien ne marche plus ; il ne débloque pas un transfert bloqué. Affiché une seule fois. */
function NewCodeSheet({ transfer, onClose }: { transfer: Transfer; onClose: () => void }) {
  const { t } = useTranslation();
  const renew = useTransferAction((http) => newTransferCode(http, transfer.id));
  const server = useServerError();
  const [result, setResult] = useState<NewWithdrawalCode | null>(null);

  async function confirm() {
    server.clear();
    try {
      setResult(await renew.mutateAsync(undefined));
    } catch (error) {
      server.show(error);
    }
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('transfers.detail.actions.newCode')}>
      <div className="flex flex-col gap-3.5">
        {server.message && <Notice tone="error">{server.message}</Notice>}
        {result ? (
          <>
            <div className="print-area flex flex-col items-center gap-3">
              <span className="amount font-mono text-sm">{result.reference}</span>
              <WithdrawalCode code={result.withdrawalCode} />
            </div>
            <Notice tone="warning">{t('transfers.detail.newCodeOnce')}</Notice>
            <ReceiptButton transferId={transfer.id} reference={result.reference} code={result.withdrawalCode} />
            <Button variant="primary" fullWidth onClick={onClose}>
              {t('transfers.create.done.finish')}
            </Button>
          </>
        ) : (
          <>
            <Notice tone="info">{transfer.isCodeLocked ? t('transfers.detail.newCodeLocked') : t('transfers.detail.newCodeHint')}</Notice>
            <Button variant="primary" fullWidth busy={renew.isPending} busyLabel={t('transfers.detail.saving')} onClick={() => void confirm()}>
              {t('transfers.detail.newCodeConfirm')}
            </Button>
          </>
        )}
      </div>
    </Sheet>
  );
}
