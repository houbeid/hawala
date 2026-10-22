import { isIsoDay } from '@/shared/format/dates';
import { TRANSFER_DIRECTIONS, TRANSFER_STATUSES, type TransferDirection, type TransferFilters, type TransferStatus } from './types';

/**
 * Filtres de la liste lus dans l'adresse (/transfers?status=NotDelivered&page=2…). Une valeur inconnue
 * est ignorée plutôt que de casser la page ; « aujourd'hui » et la page 1 ne s'écrivent pas.
 */
export function parseTransferSearch(search: Record<string, unknown>): TransferFilters {
  const result: TransferFilters = {};
  if (isIsoDay(search.from)) result.from = search.from;
  if (isIsoDay(search.to)) result.to = search.to;
  if (TRANSFER_STATUSES.includes(search.status as TransferStatus)) result.status = search.status as TransferStatus;
  if (TRANSFER_DIRECTIONS.includes(search.direction as TransferDirection)) result.direction = search.direction as TransferDirection;
  if (typeof search.search === 'string' && search.search.trim()) result.search = search.search.trim();
  const page = Number(search.page);
  if (Number.isInteger(page) && page > 1) result.page = page;
  return result;
}
