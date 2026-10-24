import { canDrawHistory, historyDomain, historySeries, showsZero } from './historySeries';

// Série d'une courbe d'historique : ordre chronologique, jours sans valeur retirés, bornes de l'axe.

describe('historySeries', () => {
  const rows = [
    { date: '2026-10-21', value: 300 as number | null },
    { date: '2026-10-12', value: 90 },
    { date: '2026-10-15', value: null },
  ];

  it('range les jours du plus ancien au plus récent et retire ceux sans valeur', () => {
    expect(historySeries(rows, (r) => r.date, (r) => r.value)).toEqual([
      { day: '2026-10-12', value: 90 },
      { day: '2026-10-21', value: 300 },
    ]);
  });

  it('ne trace une courbe qu’à partir de deux jours', () => {
    expect(canDrawHistory([])).toBe(false);
    expect(canDrawHistory([{ day: '2026-10-12', value: 1 }])).toBe(false);
    expect(canDrawHistory(historySeries(rows, (r) => r.date, (r) => r.value))).toBe(true);
  });

  it('laisse de l’air autour des valeurs', () => {
    expect(historyDomain([{ day: 'a', value: 100 }, { day: 'b', value: 200 }])).toEqual([90, 210]);
  });

  it('valeurs toutes égales : l’axe reste lisible', () => {
    expect(historyDomain([{ day: 'a', value: 500 }, { day: 'b', value: 500 }])).toEqual([450, 550]);
    expect(historyDomain([{ day: 'a', value: 0 }, { day: 'b', value: 0 }])).toEqual([-1, 1]);
  });

  it('un résultat qui change de signe garde le zéro dans le graphique et trace sa ligne', () => {
    const points = [{ day: 'a', value: -100 }, { day: 'b', value: 300 }];
    expect(historyDomain(points)).toEqual([-140, 340]);
    expect(showsZero(points)).toBe(true);
    expect(showsZero([{ day: 'a', value: 100 }, { day: 'b', value: 300 }])).toBe(false);
  });
});
