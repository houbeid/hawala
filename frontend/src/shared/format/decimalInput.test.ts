import { parseDecimalInput, toInputText } from './decimalInput';

describe('parseDecimalInput (2 décimales au plus)', () => {
  it('lit la virgule ou le point, et les espaces de groupe', () => {
    expect(parseDecimalInput('3604,77', 2)).toEqual({ ok: true, text: '3604.77', value: 3604.77 });
    expect(parseDecimalInput('3604.77', 2)).toEqual({ ok: true, text: '3604.77', value: 3604.77 });
    expect(parseDecimalInput(' 3 650 ', 2)).toEqual({ ok: true, text: '3650', value: 3650 });
    expect(parseDecimalInput('3\u202F650,5', 2)).toEqual({ ok: true, text: '3650.5', value: 3650.5 });
  });

  it('ignore les zéros sans valeur', () => {
    expect(parseDecimalInput('03600,50', 2)).toEqual({ ok: true, text: '3600.5', value: 3600.5 });
    expect(parseDecimalInput('3600,000', 2)).toEqual({ ok: true, text: '3600', value: 3600 });
  });

  it('refuse trop de décimales sans arrondir', () => {
    expect(parseDecimalInput('3600,555', 2)).toEqual({ ok: false, error: 'tooPrecise' });
  });

  it('refuse le vide, le texte, les signes et zéro', () => {
    expect(parseDecimalInput('', 2)).toEqual({ ok: false, error: 'empty' });
    expect(parseDecimalInput('abc', 2)).toEqual({ ok: false, error: 'invalid' });
    expect(parseDecimalInput('-3600', 2)).toEqual({ ok: false, error: 'invalid' });
    expect(parseDecimalInput('3,600,5', 2)).toEqual({ ok: false, error: 'invalid' });
    expect(parseDecimalInput('0', 2)).toEqual({ ok: false, error: 'notPositive' });
  });

  it('remet une valeur dans un champ avec la virgule', () => {
    expect(toInputText(3604.77)).toBe('3604,77');
    expect(toInputText(3650)).toBe('3650');
  });
});
