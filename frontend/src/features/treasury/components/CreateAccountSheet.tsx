import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { Button } from '@/shared/ui/Button';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { TextField } from '@/shared/ui/TextField';
import { createTreasuryAccount } from '../api';
import { useTreasuryAction } from '../queries';
import { TREASURY_ACCOUNT_TYPES, TREASURY_NAME_MAX, type CreateTreasuryAccountRequest, type TreasuryAccountType } from '../types';

// Nouveau compte de trésorerie (treasury.manage, Superviseur). Absent de la maquette 14 (« le brief ne décrit
// pas la création ») : dessiné d'après le backend : nom (≤ 50, unique dans l'agence), type, agence ; la devise
// découle de l'agence et n'est jamais saisie.
// POINT OUVERT : solde de départ d'un compte (écriture d'ouverture contre « Soldes d'ouverture », règle validée) :
// aucune route ne l'enregistre encore pour un compte de trésorerie ; le compte démarre à zéro. Chemin à trancher
// avec Hamade avant la mise en service (phase 8).

const selectClass =
  'min-h-14 rounded-xl border-[1.5px] border-border bg-surface px-3.5 text-[17px] text-fg focus-visible:border-ink focus-visible:outline-none';

interface Props {
  /** Agences où le compte peut être créé : une seule (verrouillée) ou les deux (Superviseur sur « les deux agences »). */
  agencies: { id: string; code: string }[];
  defaultAgencyId: string;
  onClose: () => void;
}

export function CreateAccountSheet({ agencies, defaultAgencyId, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const create = useTreasuryAction((http, request: CreateTreasuryAccountRequest) => createTreasuryAccount(http, request));
  const [name, setName] = useState('');
  const [type, setType] = useState<TreasuryAccountType>('Cash');
  const [agencyId, setAgencyId] = useState(defaultAgencyId);
  const [nameError, setNameError] = useState<string | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    const trimmed = name.trim();
    if (!trimmed) {
      setNameError(t('treasury.create.nameRequired'));
      return;
    }
    setNameError(null);
    try {
      await create.mutateAsync({ name: trimmed, type, agencyId });
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('treasury.create.title')} description={t('treasury.create.hint')}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}
        <TextField
          label={t('treasury.create.name')}
          value={name}
          maxLength={TREASURY_NAME_MAX}
          autoComplete="off"
          error={nameError ?? undefined}
          onChange={(event) => setName(event.target.value)}
        />
        <label className="flex flex-col gap-1.5">
          <span className="text-sm font-semibold text-muted">{t('treasury.create.type')}</span>
          <select value={type} onChange={(event) => setType(event.target.value as TreasuryAccountType)} className={selectClass}>
            {TREASURY_ACCOUNT_TYPES.map((value) => (
              <option key={value} value={value}>
                {t(`treasury.types.${value}`)}
              </option>
            ))}
          </select>
        </label>
        {agencies.length > 1 ? (
          <label className="flex flex-col gap-1.5">
            <span className="text-sm font-semibold text-muted">{t('treasury.create.agency')}</span>
            <select value={agencyId} onChange={(event) => setAgencyId(event.target.value)} className={selectClass}>
              {agencies.map((agency) => (
                <option key={agency.id} value={agency.id}>
                  {t(`agencies.${agency.code}`, { defaultValue: agency.code })}
                </option>
              ))}
            </select>
          </label>
        ) : (
          <p className="m-0 text-sm text-muted">
            {t('treasury.create.agencyFixed', {
              agency: t(`agencies.${agencies[0]?.code ?? ''}`, { defaultValue: agencies[0]?.code ?? '' }),
            })}
          </p>
        )}
        <Notice tone="info">{t('treasury.create.openingBalanceNote')}</Notice>
        <Button type="submit" variant="primary" fullWidth busy={create.isPending} busyLabel={t('treasury.create.creating')}>
          {t('treasury.create.submit')}
        </Button>
      </form>
    </Sheet>
  );
}
