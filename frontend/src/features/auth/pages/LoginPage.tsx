import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { useSession, useSessionManager } from '@/shared/auth/hooks';
import { Button } from '@/shared/ui/Button';
import { Notice } from '@/shared/ui/Notice';
import { PasswordField, TextField } from '@/shared/ui/TextField';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { AuthLayout } from '../components/AuthLayout';
import { loginSchema, type LoginForm } from '../schemas';

// Maquette 01 : un seul champ « email ou téléphone », un seul message d'erreur (le serveur ne dit jamais
// si le compte existe). Après la connexion, le routeur emmène vers la page demandée ou le changement
// de mot de passe : cet écran ne navigue pas lui-même.

export function LoginPage() {
  const { t, i18n } = useTranslation();
  const session = useSessionManager();
  const state = useSession();
  const online = useOnlineStatus();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginForm>({ resolver: zodResolver(loginSchema), defaultValues: { login: '', password: '' } });

  const onSubmit = handleSubmit(async (form) => {
    setServerError(null);
    try {
      await session.login({ login: form.login.trim(), password: form.password });
    } catch (error) {
      setServerError(errorMessage(i18n, error));
    }
  });

  const endedReason = state.status === 'anonymous' ? state.reason : null;

  return (
    <AuthLayout title={t('auth.login.title')}>
      {endedReason && !serverError && <Notice tone="info">{t(`auth.ended.${endedReason}`)}</Notice>}
      {!online && <Notice tone="info">{t('auth.login.offlineHelp')}</Notice>}
      {serverError && <Notice tone="error">{serverError}</Notice>}

      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-3.5">
        <TextField
          label={t('auth.login.loginLabel')}
          placeholder={t('auth.login.loginPlaceholder')}
          autoComplete="username"
          inputMode="email"
          dir="ltr"
          error={errors.login?.message && t(errors.login.message)}
          {...register('login')}
        />
        <PasswordField
          label={t('auth.login.passwordLabel')}
          autoComplete="current-password"
          error={errors.password?.message && t(errors.password.message)}
          {...register('password')}
        />
        <Button type="submit" variant="primary" fullWidth busy={isSubmitting} busyLabel={t('auth.login.submitting')} disabled={!online}>
          {t('auth.login.submit')}
        </Button>
      </form>
      <p className="m-0 text-sm text-muted">{t('auth.login.forgotten')}</p>
    </AuthLayout>
  );
}
