import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { Button } from '@/shared/ui/Button';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { TextField } from '@/shared/ui/TextField';
import { updateParty } from '../api';
import { usePartyAction } from '../queries';
import { PARTY_LIMITS, PARTY_TYPES, type PartySummary, type PartyType, type UpdatePartyRequest } from '../types';

// Modifier la fiche (parties.manage, Superviseur) : état complet envoyé (un champ vidé est retiré), « dans le
// bilan » toujours envoyé. Téléphone et NNI restent uniques (le serveur répond PhoneTaken / NniTaken).

const selectClass =
  'min-h-14 rounded-xl border-[1.5px] border-border bg-surface px-3.5 text-[17px] text-fg focus-visible:border-ink focus-visible:outline-none';

export function EditPartySheet({ party, onClose }: { party: PartySummary; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const update = usePartyAction((http, request: UpdatePartyRequest) => updateParty(http, party.id, request));
  const [name, setName] = useState(party.name);
  const [type, setType] = useState<PartyType>(party.type);
  const [phone, setPhone] = useState(party.phone ?? '');
  const [nni, setNni] = useState(party.nni ?? '');
  const [address, setAddress] = useState(party.address ?? '');
  const [note, setNote] = useState(party.note ?? '');
  const [inBalanceSheet, setInBalanceSheet] = useState(party.inBalanceSheet);
  const [nameError, setNameError] = useState<string | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    if (!name.trim()) {
      setNameError(t('parties.create.nameRequired'));
      return;
    }
    setNameError(null);
    const optional = (value: string) => value.trim() || null;
    try {
      await update.mutateAsync({
        name: name.trim(),
        type,
        phone: optional(phone),
        nni: optional(nni),
        address: optional(address),
        note: optional(note),
        inBalanceSheet,
      });
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('parties.manage.editTitle')}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}
        <TextField
          label={t('parties.create.name')}
          value={name}
          maxLength={PARTY_LIMITS.name}
          autoComplete="off"
          error={nameError ?? undefined}
          onChange={(e) => setName(e.target.value)}
        />
        <label className="flex flex-col gap-1.5">
          <span className="text-sm font-semibold text-muted">{t('parties.create.type')}</span>
          <select value={type} onChange={(e) => setType(e.target.value as PartyType)} className={selectClass}>
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
          autoComplete="off"
          onChange={(e) => setPhone(e.target.value)}
        />
        <TextField label={t('parties.manage.nni')} value={nni} maxLength={PARTY_LIMITS.nni} dir="ltr" autoComplete="off" onChange={(e) => setNni(e.target.value)} />
        <TextField label={t('parties.detail.address')} value={address} maxLength={PARTY_LIMITS.address} onChange={(e) => setAddress(e.target.value)} />
        <TextField label={t('parties.detail.note')} value={note} maxLength={PARTY_LIMITS.note} onChange={(e) => setNote(e.target.value)} />
        <label className="flex min-h-12 items-center gap-3 text-[17px]">
          <input type="checkbox" checked={inBalanceSheet} onChange={(e) => setInBalanceSheet(e.target.checked)} className="size-5 accent-[var(--ink)]" />
          {t('parties.manage.inBalanceSheet')}
        </label>
        <Button type="submit" variant="primary" fullWidth busy={update.isPending} busyLabel={t('parties.manage.saving')}>
          {t('parties.manage.save')}
        </Button>
      </form>
    </Sheet>
  );
}
