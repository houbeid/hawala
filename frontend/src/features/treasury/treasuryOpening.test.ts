import { openingDifference, parseOpening } from './treasuryOpening';

describe('solde de départ d’un compte de trésorerie', () => {
  it('lit un montant positif, zéro, ou négatif quand « découvert » est coché', () => {
    expect(parseOpening('2 400 000,50', 'MRO', false)).toEqual({ ok: true, value: 2_400_000.5 });
    expect(parseOpening('0', 'MRO', false)).toEqual({ ok: true, value: 0 });
    expect(parseOpening('45 000', 'MRO', true)).toEqual({ ok: true, value: -45_000 });
  });

  it('convertit le MRU en MRO sur le texte et refuse un FCFA avec décimales', () => {
    expect(parseOpening('12,5', 'MRU', false)).toEqual({ ok: true, value: 125 });
    expect(parseOpening('1000,5', 'XOF', false)).toEqual({ ok: false, error: 'tooPrecise' });
    expect(parseOpening('', 'MRO', false)).toEqual({ ok: false, error: 'empty' });
  });

  it('calcule la différence au centime près', () => {
    expect(openingDifference(750_000, 1_000_000)).toBe(-250_000);
    expect(openingDifference(0.3, 0.1)).toBe(0.2);
  });
});
