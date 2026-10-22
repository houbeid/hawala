import { useTranslation } from 'react-i18next';
import { useSessionManager } from '@/shared/auth/hooks';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { AuthLayout } from '../components/AuthLayout';

/** Pendant la reprise de session au chargement (/auth/refresh puis /auth/me). */
export function SessionLoadingScreen() {
  const { t } = useTranslation();
  return (
    <div role="status" className="flex min-h-dvh flex-col items-center justify-center gap-3 text-muted">
      <Icon name="sync" spin className="size-8" />
      <span>{t('auth.loading')}</span>
    </div>
  );
}

/** Le serveur n'a pas répondu au chargement : la session existe peut-être encore, on propose de réessayer. */
export function ServerUnreachableScreen() {
  const { t } = useTranslation();
  const session = useSessionManager();
  return (
    <AuthLayout icon="wifiOff" title={t('auth.unreachable.title')}>
      <Notice tone="warning">{t('auth.unreachable.message')}</Notice>
      <Button variant="primary" fullWidth icon={<Icon name="sync" />} onClick={() => void session.retry()}>
        {t('auth.unreachable.retry')}
      </Button>
    </AuthLayout>
  );
}
