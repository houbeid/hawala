import type { i18n as I18n } from 'i18next';
import { errorMessage } from '../api/errorMessage';
import { ApiError } from '../api/errors';
import type { QueuedError } from './operationQueue';

/** Motif d'un rejet gardé dans la file, traduit comme une erreur du serveur (jamais le texte du serveur). */
export function queuedErrorMessage(i18n: I18n, error: QueuedError): string {
  return errorMessage(i18n, new ApiError({ status: error.status, code: error.code, details: { ...error.details } }));
}
