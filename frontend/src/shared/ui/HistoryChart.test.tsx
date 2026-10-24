import { render, screen, waitFor } from '@testing-library/react';
import { formatAmount } from '../format/money';
import { HistoryChart } from './HistoryChart';

// Courbe d'un historique : résumé lisible par un lecteur d'écran (dates et valeurs de début et de fin), titre visible,
// valeur du dernier jour écrite au bout de la courbe ; rien avec moins de deux jours (la liste suffit).

const points = [
  { day: '2026-10-12', value: 90_000 },
  { day: '2026-10-18', value: 205_000 },
  { day: '2026-10-21', value: 300_000 },
];
const format = (value: number) => formatAmount(value, 'MRO', { signed: true, maxDecimals: 0 });

describe('HistoryChart', () => {
  it('résume la courbe en texte : période, première et dernière valeur', () => {
    render(<HistoryChart title="Résultat net · MRO" points={points} formatValue={format} />);
    expect(
      screen.getByRole('img', { name: `Courbe : Résultat net · MRO, du 12/10 au 21/10, de ${format(90_000)} à ${format(300_000)}.` }),
    ).toBeInTheDocument();
    expect(screen.getByText('Résultat net · MRO')).toBeInTheDocument();
  });

  it('un seul jour : pas de courbe', () => {
    const { container } = render(<HistoryChart title="Budget" points={points.slice(0, 1)} formatValue={format} />);
    expect(container).toBeEmptyDOMElement();
  });

  describe('une fois la largeur connue', () => {
    const original = globalThis.ResizeObserver;
    beforeEach(() => {
      globalThis.ResizeObserver = class {
        private readonly callback: ResizeObserverCallback;
        constructor(callback: ResizeObserverCallback) {
          this.callback = callback;
        }
        observe(target: Element) {
          this.callback([{ target, contentRect: { width: 320 } } as unknown as ResizeObserverEntry], this as unknown as ResizeObserver);
        }
        unobserve() {}
        disconnect() {}
      } as unknown as typeof ResizeObserver;
    });
    afterEach(() => {
      globalThis.ResizeObserver = original;
    });

    it('trace la courbe, écrit la dernière valeur et les dates de début et de fin', async () => {
      const { container } = render(<HistoryChart title="Résultat net · MRO" points={points} formatValue={format} />);
      await waitFor(() => expect(container.querySelector('.recharts-line-curve')).not.toBeNull());
      // Valeur au bout de la courbe (espaces fines du format : comparée telle quelle, sans normalisation).
      expect(container.querySelector('.recharts-label-list text')?.textContent).toBe(format(300_000));
      const ticks = [...container.querySelectorAll('.recharts-xAxis-tick-labels text')].map((tick) => tick.textContent);
      expect(ticks).toEqual(['12/10', '21/10']);
    });
  });
});
