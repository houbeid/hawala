import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { Agency } from '@/features/organization/types';
import { errorMessage } from '@/shared/api/errorMessage';
import type { Role } from '@/shared/auth/types';
import { Button } from '@/shared/ui/Button';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { TextField } from '@/shared/ui/TextField';
import { changeUserAgency, changeUserRole, updateUserProfile } from '../api';
import { useUserAction } from '../queries';
import { ROLES } from '../usersRules';
import type { UpdateUserProfileRequest, UserSummary } from '../types';

// Fenêtres de la fiche d'un compte (maquette 13 ; users.manage) : profil (nom, email, téléphone ; sessions gardées),
// rôle et agence (compte de rôle Superviseur ; efface permissions ajustées et plafonds), confirmation d'une action
// grave. Les refus du serveur (dernier Superviseur actif, email déjà pris…) sont traduits dans la fenêtre.

const selectClass =
  'min-h-14 rounded-xl border-[1.5px] border-border bg-surface px-3.5 text-[17px] text-fg focus-visible:border-ink focus-visible:outline-none';

export function ProfileSheet({ user, onClose }: { user: UserSummary; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const save = useUserAction((http, request: UpdateUserProfileRequest) => updateUserProfile(http, user.id, request));
  const [fullName, setFullName] = useState(user.fullName);
  const [email, setEmail] = useState(user.email);
  const [phone, setPhone] = useState(user.phone ?? '');
  const [serverError, setServerError] = useState<string | null>(null);
  const [errors, setErrors] = useState<{ fullName?: string; email?: string }>({});

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
      await save.mutateAsync({ fullName: fullName.trim(), email: email.trim(), phone: phone.trim() || null });
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('users.profile.title')} description={t('users.profile.hint')}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}
        <TextField label={t('users.fields.fullName')} value={fullName} error={errors.fullName} onChange={(e) => setFullName(e.target.value)} />
        <TextField label={t('users.fields.email')} type="email" dir="ltr" value={email} error={errors.email} onChange={(e) => setEmail(e.target.value)} />
        <TextField label={t('users.fields.phoneOptional')} type="tel" dir="ltr" value={phone} onChange={(e) => setPhone(e.target.value)} />
        <Button type="submit" variant="primary" fullWidth busy={save.isPending} busyLabel={t('users.saving')}>
          {t('users.profile.save')}
        </Button>
      </form>
    </Sheet>
  );
}

export function RoleAgencySheet({ user, agencies, onClose }: { user: UserSummary; agencies: Agency[]; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const changeRole = useUserAction((http, request: { role: Role; agencyId: string | null }) => changeUserRole(http, user.id, request));
  const changeAgency = useUserAction((http, agencyId: string) => changeUserAgency(http, user.id, agencyId));
  const [role, setRole] = useState<Role>(user.role);
  const [agencyId, setAgencyId] = useState(user.agency?.id ?? agencies[0]?.id ?? '');
  const [serverError, setServerError] = useState<string | null>(null);
  const busy = changeRole.isPending || changeAgency.isPending;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setServerError(null);
    try {
      if (role !== user.role) await changeRole.mutateAsync({ role, agencyId: role === 'Supervisor' ? null : agencyId });
      else if (role !== 'Supervisor' && agencyId !== user.agency?.id) await changeAgency.mutateAsync(agencyId);
      onClose();
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  }

  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={t('users.role.title')} description={t('users.role.hint')}>
      <form noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-3.5">
        {serverError && <Notice tone="error">{serverError}</Notice>}
        <label className="flex flex-col gap-1.5">
          <span className="text-sm font-semibold text-muted">{t('users.fields.role')}</span>
          <select value={role} onChange={(e) => setRole(e.target.value as Role)} className={selectClass}>
            {ROLES.map((value) => (
              <option key={value} value={value}>
                {t(`roles.${value}`)}
              </option>
            ))}
          </select>
        </label>
        {role !== 'Supervisor' && (
          <label className="flex flex-col gap-1.5">
            <span className="text-sm font-semibold text-muted">{t('users.fields.agency')}</span>
            <select value={agencyId} onChange={(e) => setAgencyId(e.target.value)} className={selectClass}>
              {agencies.map((agency) => (
                <option key={agency.id} value={agency.id}>
                  {t(`agencies.${agency.code}`, { defaultValue: agency.name })}
                </option>
              ))}
            </select>
          </label>
        )}
        <Notice tone="warning">{t('users.role.reset')}</Notice>
        <Button type="submit" variant="primary" fullWidth busy={busy} busyLabel={t('users.saving')}>
          {t('users.role.save')}
        </Button>
      </form>
    </Sheet>
  );
}

interface ConfirmProps {
  title: string;
  text: string;
  confirmLabel: string;
  danger?: boolean;
  busy: boolean;
  error: string | null;
  onConfirm: () => void;
  onClose: () => void;
}

export function ConfirmSheet({ title, text, confirmLabel, danger = false, busy, error, onConfirm, onClose }: ConfirmProps) {
  const { t } = useTranslation();
  return (
    <Sheet open onOpenChange={(value) => !value && onClose()} title={title}>
      <div className="flex flex-col gap-3">
        {error && <Notice tone="error">{error}</Notice>}
        <p className="m-0 text-[15px]">{text}</p>
        <Button variant={danger ? 'danger' : 'primary'} fullWidth busy={busy} busyLabel={t('users.saving')} onClick={onConfirm}>
          {confirmLabel}
        </Button>
        <Button fullWidth onClick={onClose}>
          {t('users.back')}
        </Button>
      </div>
    </Sheet>
  );
}
