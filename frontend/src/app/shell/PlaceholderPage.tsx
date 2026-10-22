import { useTranslation } from 'react-i18next';
import { Notice } from '@/shared/ui/Notice';

// POINT OUVERT : écran pas encore construit. Chaque route existe déjà (navigation, garde par permission)
// et reçoit son vrai écran à son tour (taux, transferts, tiers, caisse…).
export function PlaceholderPage() {
  const { t } = useTranslation();
  return <Notice tone="info">{t('shell.comingSoon')}</Notice>;
}
