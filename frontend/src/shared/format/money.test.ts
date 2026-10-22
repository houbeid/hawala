import { formatAmount, formatNumber, formatPercent, formatRate, unitOf } from './money';

// Rend visibles les espaces spéciales dans les attentes : « _ » = espace fine (groupes),
// « ~ » = espace insécable (unité, signe), « − » = vrai signe moins.
const show = (text: string) => text.replace(/\u202F/g, '_').replace(/\u00A0/g, '~');

describe('formatNumber', () => {
  it('groupe les chiffres par trois avec une espace fine', () => {
    expect(show(formatNumber(1850000))).toBe('1_850_000');
    expect(show(formatNumber(999))).toBe('999');
  });

  it('écrit les décimales avec une virgule, toujours deux quand il y en a', () => {
    expect(show(formatNumber(123.45))).toBe('123,45');
    expect(show(formatNumber(123.4))).toBe('123,40');
    expect(show(formatNumber(3604.77))).toBe('3_604,77');
  });

  it("n'écrit pas de décimales nulles", () => {
    expect(formatNumber(3650)).toBe('3' + '\u202F' + '650');
    expect(formatNumber(10.001)).toBe('10');
  });

  it('arrondit 0,5 en s’éloignant de zéro, comme le backend', () => {
    expect(formatNumber(2.5, 0)).toBe('3');
    expect(formatNumber(536892.5, 0)).toBe('536' + '\u202F' + '893');
    expect(formatNumber(0.125, 2)).toBe('0,13');
  });

  it('garde les chiffres occidentaux', () => {
    expect(formatNumber(1234567.89)).toMatch(/^[0-9\u202F,]+$/);
  });
});

describe('formatAmount', () => {
  it('écrit toujours l’unité, FCFA pour le XOF', () => {
    expect(show(formatAmount(1850000, 'MRO'))).toBe('1_850_000~MRO');
    expect(show(formatAmount(62300000, 'XOF'))).toBe('62_300_000~FCFA');
  });

  it('écrit le FCFA sans décimales', () => {
    expect(show(formatAmount(1500.6, 'XOF'))).toBe('1_501~FCFA');
  });

  it('accepte les décimales en MRO hors transferts', () => {
    expect(show(formatAmount(125.5, 'MRO'))).toBe('125,50~MRO');
  });

  it('met un vrai signe moins devant un montant négatif', () => {
    expect(show(formatAmount(-1200000, 'MRO'))).toBe('−~1_200_000~MRO');
  });

  it('met un « + » seulement sur demande', () => {
    expect(show(formatAmount(300000, 'MRO'))).toBe('300_000~MRO');
    expect(show(formatAmount(300000, 'MRO', { signed: true }))).toBe('+~300_000~MRO');
  });

  it('ne signe jamais zéro, même arrondi', () => {
    expect(show(formatAmount(0, 'MRO', { signed: true }))).toBe('0~MRO');
    expect(show(formatAmount(-0.001, 'MRO', { signed: true }))).toBe('0~MRO');
  });

  it('arrondit un total affiché quand on le demande (bénéfice en pleine précision)', () => {
    expect(show(formatAmount(536892.9999999999, 'MRO', { maxDecimals: 0, signed: true }))).toBe('+~536_893~MRO');
  });
});

describe('formatRate', () => {
  it('suit le format « 5 000 FCFA = ? MRO »', () => {
    expect(show(formatRate(3604.77))).toBe('5_000~FCFA = 3_604,77~MRO');
    expect(show(formatRate(3650))).toBe('5_000~FCFA = 3_650~MRO');
  });
});

describe('formatPercent', () => {
  it('signe le pourcentage par défaut', () => {
    expect(show(formatPercent(8.5714))).toBe('+~8,57~%');
    expect(show(formatPercent(-0.16))).toBe('−~0,16~%');
    expect(show(formatPercent(0))).toBe('0~%');
  });
});

describe('unitOf', () => {
  it('associe chaque devise à son unité affichée', () => {
    expect(unitOf('MRO')).toBe('MRO');
    expect(unitOf('XOF')).toBe('FCFA');
  });
});
