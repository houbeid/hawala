import type { i18n as I18n } from 'i18next';
import { ClientErrorCodes, isApiError, RATE_LIMITED_CODE } from './errors';

// Message lisible d'une erreur, dans la langue de l'interface. Les codes du serveur ont leur traduction
// sous « errors.<code> » (les points du code deviennent des niveaux : errors.Identity.Auth.LockedOut) ;
// un code sans traduction donne le message générique selon le statut, jamais le texte français du serveur.

export function errorMessage(i18n: I18n, error: unknown): string {
  const t = i18n.t.bind(i18n);
  if (!isApiError(error)) return t('errors.generic');

  if (error.code === RATE_LIMITED_CODE) {
    return error.retryAfterSeconds
      ? t('errors.Api.RateLimitedWait', { seconds: error.retryAfterSeconds })
      : t('errors.Api.RateLimited');
  }
  if (error.code === ClientErrorCodes.Network) return t('errors.network');

  const key = `errors.${error.code}`;
  if (i18n.exists(key)) return t(key, { ...error.details });

  if (error.status === 403) return t('errors.forbidden');
  if (error.status === 404) return t('errors.notFound');
  return t('errors.generic');
}
