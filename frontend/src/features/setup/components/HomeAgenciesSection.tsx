import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { useSessionManager } from '@/shared/auth/hooks';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { assignHomeAgencies } from '../api';

// Agences des tiers (décision du patron, 2 novembre 2026) : un agent ne voit que les tiers de son agence et ceux des deux.
// Les tiers importés avant cette règle sont rattachés aux deux ; ce bouton les rattache d'après leurs soldes (un solde
// dans une seule agence = cette agence). Les autres restent aux deux ; le Superviseur corrige sur la fiche du tiers.

export function HomeAgenciesSection() {
  const { t, i18n } = useTranslation();
  const session = useSessionManager();
  const queryClient = useQueryClient();
  const [confirming, setConfirming] = useState(false);
  const assign = useMutation({
    mutationFn: () => assignHomeAgencies(session.http),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['parties'] }),
  });

  async function confirm() {
    setConfirming(false);
    try {
      await assign.mutateAsync();
    } catch {
      // Le message est affiché à partir de assign.error.
    }
  }

  return (
    <section className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4" aria-label={t('setup.homeAgencies.title')}>
      <h2 className="m-0 text-lg font-bold">{t('setup.homeAgencies.title')}</h2>
      <p className="m-0">{t('setup.homeAgencies.hint')}</p>
      {assign.data && (
        <Notice tone="info">{t('setup.homeAgencies.done', { assigned: assign.data.assigned, keptBoth: assign.data.keptBoth })}</Notice>
      )}
      {assign.isError && <Notice tone="error">{errorMessage(i18n, assign.error)}</Notice>}
      <Button
        icon={<Icon name="globe" />}
        className="self-start"
        busy={assign.isPending}
        busyLabel={t('setup.homeAgencies.busy')}
        onClick={() => setConfirming(true)}
      >
        {t('setup.homeAgencies.run')}
      </Button>
      {confirming && (
        <Sheet open onOpenChange={(open) => !open && setConfirming(false)} title={t('setup.homeAgencies.confirmTitle')}>
          <div className="flex flex-col gap-3.5">
            <p className="m-0">{t('setup.homeAgencies.confirmText')}</p>
            <Button variant="primary" fullWidth onClick={() => void confirm()}>
              {t('setup.homeAgencies.confirm')}
            </Button>
            <Button fullWidth onClick={() => setConfirming(false)}>
              {t('setup.homeAgencies.back')}
            </Button>
          </div>
        </Sheet>
      )}
    </section>
  );
}
