import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { AgencyRef } from '@/shared/agency/agencyScope';
import { errorMessage } from '@/shared/api/errorMessage';
import { Button } from '@/shared/ui/Button';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { setPartyHomeAgency } from '../api';
import { usePartyAction } from '../queries';
import type { PartySummary } from '../types';
import { HomeAgencyChoice } from './HomeAgencyChoice';

// Le Superviseur change l'agence de rattachement d'un tiers (parties.manage) ; même valeur = rien n'est envoyé.

interface Props {
  party: PartySummary;
  agencies: readonly AgencyRef[];
  onClose: () => void;
}

export function HomeAgencySheet({ party, agencies, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const save = usePartyAction((http, agencyId: string | null) => setPartyHomeAgency(http, party.id, agencyId));
  const [value, setValue] = useState<string | null>(party.homeAgencyId);
  const [serverError, setServerError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    if (value === party.homeAgencyId) {
      onClose();
      return;
    }
    try {
      await save.mutateAsync(value);
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()} title={t('parties.home.title', { name: party.name })}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}
        <HomeAgencyChoice agencies={agencies} value={value} onChange={setValue} />
        <Button type="submit" variant="primary" fullWidth busy={save.isPending} busyLabel={t('parties.manage.saving')}>
          {t('parties.home.save')}
        </Button>
      </form>
    </Sheet>
  );
}
