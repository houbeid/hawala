// Série d'une courbe d'historique (budgets ou bilans enregistrés) : une valeur par jour, dans une seule devise.
// Les jours sans valeur (bilan enregistré sans capital initial) sont retirés, jamais tracés à zéro.

export interface HistoryPoint {
  /** Jour aaaa-mm-jj. */
  day: string;
  value: number;
}

/** Points du plus ancien au plus récent ; une valeur absente (null) est retirée. */
export function historySeries<T>(items: readonly T[], day: (item: T) => string, value: (item: T) => number | null): HistoryPoint[] {
  return items
    .map((item) => ({ day: day(item), value: value(item) }))
    .filter((point): point is HistoryPoint => point.value !== null)
    .sort((a, b) => a.day.localeCompare(b.day));
}

/** Une courbe n'a de sens qu'à partir de deux jours ; avec un seul, la liste suffit. */
export function canDrawHistory(points: readonly HistoryPoint[]): boolean {
  return points.length >= 2;
}

/** Bornes de l'axe vertical : un peu d'air autour des valeurs, zéro inclus si la série change de signe. */
export function historyDomain(points: readonly HistoryPoint[]): [number, number] {
  const values = points.map((p) => p.value);
  let min = Math.min(...values);
  let max = Math.max(...values);
  if (min < 0 && max > 0) {
    min = Math.min(min, 0);
    max = Math.max(max, 0);
  }
  const pad = (max - min) * 0.1 || Math.abs(max) * 0.1 || 1;
  return [min - pad, max + pad];
}

/** La ligne du zéro n'est tracée que si elle tombe dans le graphique. */
export function showsZero(points: readonly HistoryPoint[]): boolean {
  return points.some((p) => p.value < 0);
}
