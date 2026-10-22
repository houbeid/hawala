import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { useSessionManager } from '@/shared/auth/hooks';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { PasswordField } from '@/shared/ui/TextField';
import { AuthLayout } from '../components/AuthLayout';
import { PasswordRules } from '../components/PasswordRules';
import { changePasswordSchema, type ChangePasswordForm } from '../schemas';

// Maquette 01, première connexion : le mot de passe temporaire doit être changé avant toute autre action
// (le serveur refuse le reste). Le changement ferme les autres sessions du compte.

export function ChangePasswordPage() {
  const { t, i18n } = useTranslation();
  const session = useSessionManager();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    control,
    formState: { errors, isSubmitting },
  } = useForm<ChangePasswordForm>({
    resolver: zodResolver(changePasswordSchema),
    defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
  });
  const newPassword = useWatch({ control, name: 'newPassword' });

  const onSubmit = handleSubmit(async (form) => {
    setServerError(null);
    try {
      await session.changePassword({ currentPassword: form.currentPassword, newPassword: form.newPassword });
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  });

  return (
    <AuthLayout icon="lock" title={t('auth.changePassword.title')}>
      <Notice tone="info">{t('auth.changePassword.why')}</Notice>
      {serverError && <Notice tone="error">{serverError}</Notice>}

      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-3.5">
        <PasswordField
          label={t('auth.changePassword.currentLabel')}
          autoComplete="current-password"
          error={errors.currentPassword?.message && t(errors.currentPassword.message)}
          {...register('currentPassword')}
        />
        <PasswordField
          label={t('auth.changePassword.newLabel')}
          autoComplete="new-password"
          error={errors.newPassword?.message && t(errors.newPassword.message)}
          {...register('newPassword')}
        />
        <PasswordRules password={newPassword} />
        <PasswordField
          label={t('auth.changePassword.confirmLabel')}
          autoComplete="new-password"
          error={errors.confirmPassword?.message && t(errors.confirmPassword.message)}
          {...register('confirmPassword')}
        />
        <Button type="submit" variant="primary" fullWidth busy={isSubmitting} busyLabel={t('auth.changePassword.submitting')}>
          {t('auth.changePassword.submit')}
        </Button>
      </form>
      <Button fullWidth icon={<Icon name="signOut" />} onClick={() => void session.logout()}>
        {t('auth.signOut')}
      </Button>
      <p className="m-0 text-sm text-muted">{t('auth.changePassword.otherSessions')}</p>
    </AuthLayout>
  );
}
