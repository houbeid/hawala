import { parseMoneyInput, sumAmounts, transferMroPreview, xofFromMro } from './moneyInput';

describe('parseMoneyInput', () => {
  it('lit un FCFA entier et refuse les décimales', () => {
    expect(parseMoneyInput('1 250 000', 'XOF')).toEqual({ ok: true, value: 1250000, text: '1250000' });
    expect(parseMoneyInput('1250000,5', 'XOF')).toEqual({ ok: false, error: 'tooPrecise' });
  });

  it('lit un MRO à 2 décimales au plus', () => {
    expect(parseMoneyInput('912 500', 'MRO')).toEqual({ ok: true, value: 912500, text: '912500' });
    expect(parseMoneyInput('123,45', 'MRO')).toEqual({ ok: true, value: 123.45, text: '123.45' });
    expect(parseMoneyInput('123,456', 'MRO')).toEqual({ ok: false, error: 'tooPrecise' });
  });

  it('convertit le MRU en MRO × 10, exactement', () => {
    expect(parseMoneyInput('30 000', 'MRU')).toEqual({ ok: true, value: 300000, text: '300000' });
    expect(parseMoneyInput('12,5', 'MRU')).toEqual({ ok: true, value: 125, text: '125' });
    expect(parseMoneyInput('12,345', 'MRU')).toEqual({ ok: true, value: 123.45, text: '123.45' });
    expect(parseMoneyInput('0,1', 'MRU')).toEqual({ ok: true, value: 1, text: '1' });
    expect(parseMoneyInput('12,3456', 'MRU')).toEqual({ ok: false, error: 'tooPrecise' });
  });
});

describe('transferMroPreview (comme ExchangeRate.ToTransferMro)', () => {
  it('calcule FCFA × taux ÷ 5 000 arrondi à l’unité', () => {
    expect(transferMroPreview(1250000, 3650)).toBe(912500);
    // Exemple de l'app actuelle : 13 927 577 FCFA à 3 590 = 10 000 000,29 → 10 000 000 MRO.
    expect(transferMroPreview(13927577, 3590)).toBe(10000000);
  });

  it('arrondit 0,5 en s’éloignant de zéro, avec un taux à décimales', () => {
    // 5 000 × 3 604,77 ÷ 5 000 = 3 604,77 → 3 605 ; 2 500 × 3 601 ÷ 5 000 = 1 800,5 → 1 801.
    expect(transferMroPreview(5000, 3604.77)).toBe(3605);
    expect(transferMroPreview(2500, 3601)).toBe(1801);
  });
});

describe('xofFromMro (saisie en MRO à Nouakchott)', () => {
  it('calcule le FCFA arrondi au plus proche', () => {
    // 912 500 × 5 000 ÷ 3 700 = 1 233 108,1 → 1 233 108.
    expect(xofFromMro(912500, 3700)).toBe(1233108);
    expect(xofFromMro(912500, 3650)).toBe(1250000);
  });

  it('retombe toujours sur le MRO tapé une fois recalculé par le serveur', () => {
    const rates = [3400, 3590, 3600, 3604.77, 3629.88, 3650, 3700, 3799.99, 4999.99];
    const amounts = [1, 7, 99, 1000, 12345, 912500, 1000001, 9999999, 123456789];
    for (const rate of rates) {
      for (const mro of amounts) {
        expect(transferMroPreview(xofFromMro(mro, rate), rate)).toBe(mro);
      }
    }
  });
});

describe('sumAmounts', () => {
  it('additionne sans erreur de virgule flottante', () => {
    expect(sumAmounts([0.1, 0.2])).toBe(0.3);
    expect(sumAmounts([300000, 612500])).toBe(912500);
  });
});
