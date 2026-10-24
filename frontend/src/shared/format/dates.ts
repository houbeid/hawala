// Dates « jour » (aaaa-mm-jj) comme le backend (DateOnly). « Aujourd'hui » est la date UTC, comme côté
// serveur : Nouakchott et Dakar sont en UTC+0, la date du téléphone et celle du serveur coïncident.

const ISO_DAY = /^\d{4}-\d{2}-\d{2}$/;

export function isIsoDay(value: unknown): value is string {
  return typeof value === 'string' && ISO_DAY.test(value) && !Number.isNaN(Date.parse(`${value}T00:00:00Z`));
}

export function todayIso(now: Date = new Date()): string {
  return now.toISOString().slice(0, 10);
}

export function addDays(day: string, days: number): string {
  const date = new Date(`${day}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

/** Écart en jours entre deux dates (b − a). */
export function daysBetween(a: string, b: string): number {
  return Math.round((Date.parse(`${b}T00:00:00Z`) - Date.parse(`${a}T00:00:00Z`)) / 86_400_000);
}

/** Locale d'affichage : chiffres occidentaux même en arabe (règle des maquettes). */
function locale(language: string): string {
  return language === 'ar' ? 'ar-u-nu-latn' : 'fr-FR';
}

/**
 * « mer. 21/10/2026 » (« الأربعاء 21/10/2026 » en arabe), jour lu en UTC. La date est composée à la main :
 * Intl ajoute en arabe des marques de sens invisibles entre les nombres, qui gênent la recherche et la copie.
 */
export function formatDayLong(day: string, language: string): string {
  const weekday = new Intl.DateTimeFormat(locale(language), { weekday: language === 'ar' ? 'long' : 'short', timeZone: 'UTC' }).format(
    new Date(`${day}T00:00:00Z`),
  );
  return `${weekday} ${day.slice(8, 10)}/${day.slice(5, 7)}/${day.slice(0, 4)}`;
}

/** « 21/10 » : colonne d'un historique. */
export function formatDayShort(day: string): string {
  return `${day.slice(8, 10)}/${day.slice(5, 7)}`;
}

/** « 24/10 · 15:20 » : instant ISO lu en UTC (Nouakchott et Dakar sont en UTC+0), chiffres occidentaux. */
export function formatDayTimeShort(iso: string): string {
  const date = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(date.getUTCDate())}/${pad(date.getUTCMonth() + 1)} · ${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}`;
}
