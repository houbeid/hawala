import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { archiveParty, unarchiveParty } from '../api';
import { usePartyAction } from '../queries';
import type { PartySummary } from '../types';

// Archiver (pas de suppression) : seulement à solde nul dans les deux agences ; ferme les comptes du tiers au grand
// livre (plus de crédit ni de mouvement de caisse). Désarchiver les rouvre. parties.manage (Superviseur).
// Les soldes connus non nuls remplacent le bouton par une explication ; le serveur tranche de toute façon.

export function ArchiveSheet({ party, onClose }: { party: PartySummary; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const archive = usePartyAction((http) => archiveParty(http, party.id));
  const unarchive = usePartyAction((http) => unarchiveParty(http, party.id));
  const [serverError, setServerError] = useState<string | null>(null);
  const blocked = !party.isArchived && (party.balances ?? []).some((b) => b.balance !== 0);

  async function run(action: () => Promise<unknown>) {
    setServerError(null);
    try {
      await action();
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  const title = party.isArchived ? t('parties.manage.unarchiveTitle', { name: party.name }) : t('parties.manage.archiveTitle', { name: party.name });
  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={title}>
      <div className="flex flex-col gap-3">
        {serverError && <Notice tone="error">{serverError}</Notice>}
        {party.isArchived ? (
          <>
            <p className="m-0 text-[15px]">{t('parties.manage.unarchiveText')}</p>
            <Button
              variant="primary"
              fullWidth
              icon={<Icon name="sync" />}
              busy={unarchive.isPending}
              busyLabel={t('parties.manage.saving')}
              onClick={() => void run(() => unarchive.mutateAsync(undefined))}
            >
              {t('parties.manage.unarchive')}
            </Button>
          </>
        ) : blocked ? (
          <Notice tone="info">{t('parties.manage.archiveBlocked')}</Notice>
        ) : (
          <>
            <p className="m-0 text-[15px]">{t('parties.manage.archiveText')}</p>
            <Button
              variant="danger"
              fullWidth
              icon={<Icon name="ban" />}
              busy={archive.isPending}
              busyLabel={t('parties.manage.saving')}
              onClick={() => void run(() => archive.mutateAsync(undefined))}
            >
              {t('parties.manage.archive')}
            </Button>
          </>
        )}
        <Button fullWidth onClick={onClose}>
          {t('parties.manage.back')}
        </Button>
      </div>
    </Sheet>
  );
}
