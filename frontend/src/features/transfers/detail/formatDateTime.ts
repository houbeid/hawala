/**
 * « 21/10/2026 · 14:32 » (heure UTC, comme les jours du serveur : Nouakchott et Dakar sont en UTC+0).
 * Chiffres occidentaux dans les deux langues.
 */
export function formatDateTime(iso: string): string {
  const date = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(date.getUTCDate())}/${pad(date.getUTCMonth() + 1)}/${date.getUTCFullYear()} · ${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}`;
}
