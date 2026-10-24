import { addDays, compactDay, daysBetween, formatDayLong, formatDayShort, formatDayTimeShort, isIsoDay, todayIso } from './dates';

describe('dates « jour »', () => {
  it('donne la date UTC du jour', () => {
    expect(todayIso(new Date('2026-10-21T23:59:00Z'))).toBe('2026-10-21');
  });

  it('ajoute des jours en passant les mois et les années', () => {
    expect(addDays('2026-10-31', 1)).toBe('2026-11-01');
    expect(addDays('2026-01-01', -1)).toBe('2025-12-31');
    expect(daysBetween('2026-10-21', '2026-10-22')).toBe(1);
  });

  it('reconnaît une date aaaa-mm-jj valable', () => {
    expect(isIsoDay('2026-10-21')).toBe(true);
    expect(isIsoDay('2026-13-01')).toBe(false);
    expect(isIsoDay('21/10/2026')).toBe(false);
  });

  it('écrit la date avec des chiffres occidentaux, même en arabe', () => {
    expect(formatDayLong('2026-10-21', 'fr')).toMatch(/21\/10\/2026/);
    expect(formatDayLong('2026-10-21', 'ar')).toMatch(/21\/10\/2026/);
    expect(formatDayShort('2026-10-21')).toBe('21/10');
    expect(compactDay('2026-10-21')).toBe('20261021');
  });
});

describe('formatDayTimeShort', () => {
  it('écrit jour, mois et heure en UTC', () => {
    expect(formatDayTimeShort('2026-10-24T15:20:59Z')).toBe('24/10 · 15:20');
    expect(formatDayTimeShort('2026-01-05T03:07:00+00:00')).toBe('05/01 · 03:07');
  });
});
