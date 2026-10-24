import { isIsoDay } from '@/shared/format/dates';

/** Période du relevé d'un compte, lue dans l'adresse (/treasury/{id}?from=&to=) ; sans dates, le mois en cours (serveur). */
export interface PeriodSearch {
  from?: string;
  to?: string;
}

export function parsePeriodSearch(search: Record<string, unknown>): PeriodSearch {
  const result: PeriodSearch = {};
  if (isIsoDay(search.from)) result.from = search.from;
  if (isIsoDay(search.to)) result.to = search.to;
  return result;
}
