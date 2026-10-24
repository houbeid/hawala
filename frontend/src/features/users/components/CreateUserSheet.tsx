import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { Agency } from '@/features/organization/types';
import { errorMessage } from '@/shared/api/errorMessage';
import type { Role } from '@/shared/auth/types';
import { Button } from '@/shared/ui/Button';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { TextField } from '@/shared/ui/TextField';
import { createUser } from '../api';
import { useUserAction } from '../queries';
import { creatableRoles } from '../usersRules';
import type { CreateUserRequest, CreatedUser } from '../types';
import { TemporaryPasswordView } from './TemporaryPasswordView';

// Nouveau compte (maquette 13 ; users.manage) : nom, email (sert à se connecter), téléphone facultatif avec
// l'indicatif, rôle, agence (obligatoire sauf Superviseur, qui n'en a pas). Un non-Superviseur crée seulement dans son
// agence et sans le rôle Superviseur. Après la création, le mot de passe temporaire s'affiche une seule fois ; la
// fenêtre ne se ferme qu'avec « J'ai transmis le mot de passe ».

const selectClass =
  'min-h-14 rounded-xl border-[1.5px] border-border bg-surface px-3.5 text-[17px] text-fg focus-visible:border-ink focus-visible:outline-none';

interface Props {
  actorRole: Role;
  /** Agences proposées : les deux pour un Superviseur, la sienne sinon. */
  agencies: Agency[];
  onClose: () => void;
}

export function CreateUserSheet({ actorRole, agencies, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const create = useUserAction((http, request: CreateUserRequest) => createUser(http, request));
  const roles = creatableRoles(actorRole);
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [phone, setPhone] = useState('');
  const [role, setRole] = useState<Role>('Agent');
  const [agencyId, setAgencyId] = useState(agencies[0]?.id ?? '');
  const [errors, setErrors] = useState<{ fullName?: string; email?: string }>({});
  const [serverError, setServerError] = useState<string | null>(null);
  const [created, setCreated] = useState<CreatedUser | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    const found = {
      fullName: fullName.trim() ? undefined : t('users.create.nameRequired'),
      email: email.trim() ? undefined : t('users.create.emailRequired'),
    };
    setErrors(found);
    if (found.fullName || found.email) return;
    try {
      setCreated(
        await create.mutateAsync({
          fullName: fullName.trim(),
          email: email.trim(),
          phone: phone.trim() || null,
          role,
          agencyId: role === 'Supervisor' ? null : agencyId,
        }),
      );
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  return (
    <Sheet
      open
      onOpenChange={(value) => !value && !created && onClose()}
      title={created ? t('users.create.done') : t('users.create.title')}
      description={created ? undefined : t('users.create.hint')}
    >
      {created ? (
        <TemporaryPasswordView password={created.temporaryPassword} owner={`${fullName.trim()} · ${t(`roles.${role}`)}`} onDone={onClose} />
      ) : (
        <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
          {serverError && <Notice tone="error">{serverError}</Notice>}
          <TextField label={t('users.fields.fullName')} value={fullName} autoComplete="off" error={errors.fullName} onChange={(e) => setFullName(e.target.value)} />
          <TextField
            label={t('users.fields.email')}
            type="email"
            dir="ltr"
            value={email}
            autoComplete="off"
            error={errors.email}
            onChange={(e) => setEmail(e.target.value)}
          />
          <TextField label={t('users.fields.phone')} type="tel" inputMode="tel" dir="ltr" placeholder="+222 …" value={phone} onChange={(e) => setPhone(e.target.value)} />
          <label className="flex flex-col gap-1.5">
            <span className="text-sm font-semibold text-muted">{t('users.fields.role')}</span>
            <select value={role} onChange={(e) => setRole(e.target.value as Role)} className={selectClass}>
              {roles.map((value) => (
                <option key={value} value={value}>
                  {t(`roles.${value}`)}
                </option>
              ))}
            </select>
          </label>
          {role !== 'Supervisor' && (
            <label className="flex flex-col gap-1.5">
              <span className="text-sm font-semibold text-muted">{t('users.fields.agency')}</span>
              <select value={agencyId} onChange={(e) => setAgencyId(e.target.value)} disabled={agencies.length < 2} className={selectClass}>
                {agencies.map((agency) => (
                  <option key={agency.id} value={agency.id}>
                    {t(`agencies.${agency.code}`, { defaultValue: agency.name })}
                  </option>
                ))}
              </select>
            </label>
          )}
          <Notice tone="info">{t('users.create.passwordNote')}</Notice>
          <Button type="submit" variant="primary" fullWidth busy={create.isPending} busyLabel={t('users.saving')}>
            {t('users.create.submit')}
          </Button>
        </form>
      )}
    </Sheet>
  );
}
