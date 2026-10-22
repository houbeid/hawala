import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { Button } from '@/shared/ui/Button';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { TextField } from '@/shared/ui/TextField';
import { useCreateParty } from '../queries';
import { PARTY_TYPES, type PartyType } from '../types';

// « Créer un tiers » depuis le crédit d'un transfert (choix A du 22 octobre 2026) : le minimum utile
// (nom ≤ 30 obligatoire, type, téléphone international facultatif). Le reste de la fiche se complète
// plus tard par le Superviseur. Permission parties.create (Superviseur et Agent).

const NAME_MAX = 30;

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Nom tapé dans la recherche, repris comme point de départ. */
  initialName: string;
  onCreated: (party: { id: string; name: string; type: PartyType; phone: string | null }) => void;
}

export function CreatePartyDialog({ open, onOpenChange, initialName, onCreated }: Props) {
  const { t, i18n } = useTranslation();
  const create = useCreateParty();
  const [name, setName] = useState(initialName.slice(0, NAME_MAX));
  const [type, setType] = useState<PartyType>('Client');
  const [phone, setPhone] = useState('');
  const [nameError, setNameError] = useState<string | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);

  async function submit() {
    setServerError(null);
    const trimmed = name.trim();
    if (!trimmed) {
      setNameError(t('parties.create.nameRequired'));
      return;
    }
    setNameError(null);
    try {
      const { id } = await create.mutateAsync({ name: trimmed, type, phone: phone.trim() || null, nni: null, address: null, note: null });
      onCreated({ id, name: trimmed, type, phone: phone.trim() || null });
      onOpenChange(false);
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  return (
    <Sheet open={open} onOpenChange={onOpenChange} title={t('parties.create.title')} description={t('parties.create.hint')}>
      {/* Pas de <form> ici : la fenêtre est ouverte depuis le formulaire du transfert (formulaires imbriqués interdits). */}
      <div className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}
        <TextField
          label={t('parties.create.name')}
          value={name}
          maxLength={NAME_MAX}
          autoComplete="off"
          error={nameError ?? undefined}
          onChange={(event) => setName(event.target.value)}
        />
        <label className="flex flex-col gap-1.5">
          <span className="text-sm font-semibold text-muted">{t('parties.create.type')}</span>
          <select
            value={type}
            onChange={(event) => setType(event.target.value as PartyType)}
            className="min-h-14 rounded-xl border-[1.5px] border-border bg-surface px-3.5 text-[17px] text-fg focus-visible:border-ink focus-visible:outline-none"
          >
            {PARTY_TYPES.map((value) => (
              <option key={value} value={value}>
                {t(`parties.types.${value}`)}
              </option>
            ))}
          </select>
        </label>
        <TextField
          label={t('parties.create.phone')}
          value={phone}
          type="tel"
          inputMode="tel"
          dir="ltr"
          placeholder="+222 …"
          autoComplete="off"
          onChange={(event) => setPhone(event.target.value)}
        />
        <Button variant="primary" fullWidth busy={create.isPending} busyLabel={t('parties.create.creating')} onClick={() => void submit()}>
          {t('parties.create.submit')}
        </Button>
      </div>
    </Sheet>
  );
}
