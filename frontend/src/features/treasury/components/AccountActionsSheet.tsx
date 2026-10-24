import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { formatAmount } from '@/shared/format/money';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { TextField } from '@/shared/ui/TextField';
import { deactivateTreasuryAccount, reactivateTreasuryAccount, renameTreasuryAccount } from '../api';
import { useTreasuryAction } from '../queries';
import { TREASURY_NAME_MAX, type TreasuryAccount } from '../types';

// Actions sur un compte (treasury.manage) : renommer, désactiver (confirmation), réactiver. Il n'y a pas de
// suppression. Écart voulu avec la maquette 14 : elle laissait désactiver un compte au solde non nul avec un
// avertissement ; le backend l'interdit (Treasury.Account.BalanceNotZero) : on explique avant d'essayer, et
// le bouton n'est montré qu'à solde nul (ou inconnu : le serveur tranche alors).
// La fenêtre s'ouvre depuis la fiche du compte, qui montre aussi son relevé.

interface Props {
  account: TreasuryAccount;
  onClose: () => void;
}

export function AccountActionsSheet({ account, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const rename = useTreasuryAction((http, newName: string) => renameTreasuryAccount(http, account.id, { name: newName }));
  const deactivate = useTreasuryAction((http) => deactivateTreasuryAccount(http, account.id));
  const reactivate = useTreasuryAction((http) => reactivateTreasuryAccount(http, account.id));
  const [name, setName] = useState(account.name);
  const [nameError, setNameError] = useState<string | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);

  const balanceIsZero = account.balance === null || account.balance === 0;

  async function run(action: () => Promise<unknown>) {
    setServerError(null);
    try {
      await action();
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  function submitRename(event: FormEvent) {
    event.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) {
      setNameError(t('treasury.create.nameRequired'));
      return;
    }
    setNameError(null);
    if (trimmed === account.name) {
      onClose();
      return;
    }
    void run(() => rename.mutateAsync(trimmed));
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={account.name} description={t(`treasury.types.${account.type}`)}>
      {serverError && <Notice tone="error">{serverError}</Notice>}
      {account.balance !== null && (
        <div className="flex items-center justify-between gap-3 rounded-xl bg-surface-2 p-3">
          <span className="text-sm text-muted">{t('treasury.balance')}</span>
          <span className="amount text-lg">{formatAmount(account.balance, account.currency)}</span>
        </div>
      )}

      {confirming ? (
        <div className="flex flex-col gap-3">
          <p className="m-0 text-lg font-bold">{t('treasury.actions.confirmTitle', { name: account.name })}</p>
          <p className="m-0 text-[15px]">{t('treasury.actions.confirmText')}</p>
          <Button
            variant="danger"
            fullWidth
            icon={<Icon name="ban" />}
            busy={deactivate.isPending}
            busyLabel={t('treasury.actions.deactivating')}
            onClick={() => void run(() => deactivate.mutateAsync(undefined))}
          >
            {t('treasury.actions.deactivate')}
          </Button>
          <Button fullWidth onClick={() => setConfirming(false)}>
            {t('treasury.actions.back')}
          </Button>
        </div>
      ) : (
        <>
          <form noValidate onSubmit={submitRename} className="flex flex-col gap-3">
            <TextField
              label={t('treasury.actions.name')}
              value={name}
              maxLength={TREASURY_NAME_MAX}
              autoComplete="off"
              error={nameError ?? undefined}
              onChange={(event) => setName(event.target.value)}
            />
            <Button type="submit" variant="primary" fullWidth icon={<Icon name="pencil" />} busy={rename.isPending} busyLabel={t('treasury.actions.saving')}>
              {t('treasury.actions.rename')}
            </Button>
          </form>

          {account.isActive &&
            (balanceIsZero ? (
              <Button variant="danger" fullWidth icon={<Icon name="ban" />} onClick={() => setConfirming(true)}>
                {t('treasury.actions.deactivate')}
              </Button>
            ) : (
              <Notice tone="info">{t('treasury.actions.balanceNotZero')}</Notice>
            ))}

          {!account.isActive && (
            <Button
              fullWidth
              icon={<Icon name="sync" />}
              busy={reactivate.isPending}
              busyLabel={t('treasury.actions.reactivating')}
              onClick={() => void run(() => reactivate.mutateAsync(undefined))}
            >
              {t('treasury.actions.reactivate')}
            </Button>
          )}
        </>
      )}
    </Sheet>
  );
}
