import { describe, expect, it } from 'vitest';
import { activeFilterCount, balanceState, creditUsage, initials, parseAmountOrZero, parsePartySearch } from './partyRules';

describe('balanceState', () => {
  it('positif = rouge, négatif = vert, zéro = ni l’un ni l’autre', () => {
    expect(balanceState(1)).toBe('red');
    expect(balanceState(-0.01)).toBe('green');
    expect(balanceState(0)).toBe('zero');
  });
});

describe('creditUsage', () => {
  it('distingue « pas de plafond » et « aucun crédit »', () => {
    expect(creditUsage(500, null)).toEqual({ kind: 'noLimit' });
    expect(creditUsage(500, 0)).toEqual({ kind: 'noCredit' });
  });

  it('calcule ce qui reste, sans erreur de virgule', () => {
    expect(creditUsage(1_200_000.1, 1_500_000)).toEqual({ kind: 'limit', limit: 1_500_000, used: 1_200_000.1, remaining: 299_999.9, percent: 80 });
  });

  it('un tiers vert n’utilise rien de son plafond', () => {
    expect(creditUsage(-50_000, 100_000)).toMatchObject({ used: 0, remaining: 100_000, percent: 0 });
  });

  it('un plafond dépassé donne un reste négatif et une barre pleine', () => {
    expect(creditUsage(130_000, 100_000)).toMatchObject({ remaining: -30_000, percent: 100 });
  });
});

describe('initials', () => {
  it('prend la première lettre des deux premiers mots', () => {
    expect(initials('Boutique El Wafa')).toBe('BE');
    expect(initials('  sahel ')).toBe('S');
    expect(initials('مؤسسة نداي')).toBe('من');
  });
});

describe('parsePartySearch', () => {
  it('garde les valeurs connues et ignore les autres', () => {
    expect(parsePartySearch({ search: ' wafa ', type: 'Client', balanceSheet: 'Out', balance: 'Red', archived: 'true' })).toEqual({
      search: 'wafa',
      type: 'Client',
      balanceSheet: 'Out',
      balance: 'Red',
      archived: true,
    });
    expect(parsePartySearch({ type: 'Bank', balanceSheet: 'All', balance: 'Blue', archived: 'no', search: '  ' })).toEqual({});
  });

  it('compte les filtres actifs, hors recherche', () => {
    expect(activeFilterCount({ search: 'x', type: 'Staff', balance: 'Green' })).toBe(2);
  });
});

describe('parseAmountOrZero', () => {
  it('accepte zéro, refuse le reste comme toute saisie', () => {
    expect(parseAmountOrZero('0', 'MRO')).toEqual({ ok: true, value: 0, text: '0' });
    expect(parseAmountOrZero('0,00', 'XOF')).toEqual({ ok: true, value: 0, text: '0' });
    expect(parseAmountOrZero('12,345', 'MRU')).toMatchObject({ ok: true, value: 123.45 });
    expect(parseAmountOrZero('10,5', 'XOF')).toEqual({ ok: false, error: 'tooPrecise' });
    expect(parseAmountOrZero('', 'MRO')).toEqual({ ok: false, error: 'empty' });
  });
});
