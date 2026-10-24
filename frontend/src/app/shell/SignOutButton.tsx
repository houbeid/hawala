import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useQueuedOperations } from '@/shared/offline/queueContext';
import { Button } from '@/shared/ui/Button';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';

// « Se déconnecter » : s'il reste des opérations saisies hors ligne (en attente, rejetées ou un code pas encore vu),
// on prévient d'abord (décision du 24 octobre 2026) : elles restent sur ce téléphone et partiront à la prochaine
// connexion de ce compte ; la déconnexion ne les efface jamais.

interface Props {
  className: string;
  children: ReactNode;
  onSignOut: () => void;
}

export function SignOutButton({ className, children, onSignOut }: Props) {
  const { t } = useTranslation();
  const waiting = useQueuedOperations().length;
  const [confirming, setConfirming] = useState(false);

  return (
    <>
      <button type="button" className={className} onClick={() => (waiting > 0 ? setConfirming(true) : onSignOut())}>
        {children}
      </button>
      {confirming && (
        <Sheet open onOpenChange={(open) => !open && setConfirming(false)} title={t('queue.signOutTitle')}>
          <div className="flex flex-col gap-3">
            <Notice tone="warning">{t('queue.signOutHint', { count: waiting })}</Notice>
            <Button variant="primary" fullWidth onClick={onSignOut}>
              {t('queue.signOutAnyway')}
            </Button>
            <Button fullWidth onClick={() => setConfirming(false)}>
              {t('queue.stay')}
            </Button>
          </div>
        </Sheet>
      )}
    </>
  );
}
