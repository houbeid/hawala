import { canImport, issueView, reportOfError } from './importRules';

const report = { rowCount: 2, validCount: 2, issues: [], totals: { NKC: 1500 } };

describe('rapport d’import des tiers', () => {
  it('lit la colonne d’un problème, avec son agence', () => {
    expect(issueView({ row: 4, column: 'NKC.balance', code: 'Parties.Import.AmountInvalid' })).toEqual({
      row: 4,
      column: { key: 'balance', agencyCode: 'NKC' },
      code: 'Parties.Import.AmountInvalid',
    });
    expect(issueView({ row: 3, column: 'phone', code: 'Parties.Party.PhoneTaken' }).column).toEqual({ key: 'phone', agencyCode: null });
    expect(issueView({ row: 0, column: null, code: 'Parties.Import.Empty' }).column).toBeNull();
  });

  it('retrouve le rapport joint à un refus', () => {
    expect(reportOfError({ report })).toEqual(report);
    expect(reportOfError({ row: 3 })).toBeNull();
  });

  it('propose l’import seulement après une vérification sans problème du même fichier', () => {
    const file = new File(['x'], 'a.xlsx');
    expect(canImport(report, file, file)).toBe(true);
    expect(canImport(report, file, new File(['y'], 'b.xlsx'))).toBe(false);
    expect(canImport({ ...report, issues: [{ row: 2, column: 'type', code: 'x' }] }, file, file)).toBe(false);
    expect(canImport({ ...report, validCount: 0 }, file, file)).toBe(false);
  });
});
