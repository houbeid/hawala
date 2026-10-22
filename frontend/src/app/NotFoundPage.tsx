import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';

export function NotFoundPage() {
  const { t } = useTranslation();
  return (
    <main className="mx-auto flex max-w-md flex-col gap-4 p-4">
      <h1 className="m-0 text-xl font-semibold">{t('common.notFoundTitle')}</h1>
      <Link to="/" className="font-semibold text-fg underline">
        {t('common.backHome')}
      </Link>
    </main>
  );
}
