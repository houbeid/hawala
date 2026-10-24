import { describe, expect, it } from 'vitest';
import { changeView, parseAuditSearch, valueKey } from './auditText';

describe('texte lisible de l’audit', () => {
  it('lit les filtres de l’adresse', () => {
    expect(parseAuditSearch({ from: '2026-10-20', to: '2026-10-24', action: 'Transfers', page: '3' })).toEqual({
      from: '2026-10-20',
      to: '2026-10-24',
      action: 'Transfers',
      page: 3,
    });
    expect(parseAuditSearch({ action: 'Hack', page: '1' })).toEqual({});
  });

  it('une permission accordée ou retirée se lit comme une phrase', () => {
    expect(changeView('Identity.Account.PermissionsChanged', { field: 'transfers.cancel', before: 'granted', after: 'not_granted' })).toEqual({
      kind: 'permission',
      permission: 'transfers.cancel',
      granted: false,
    });
    expect(changeView('Parties.Party.Updated', { field: 'name', before: 'A', after: 'B' })).toEqual({ kind: 'field', field: 'name', before: 'A', after: 'B' });
  });

  it('traduit les valeurs masquées et booléennes, garde les autres telles quelles', () => {
    expect(valueKey('changed')).toEqual({ key: 'audit.values.changed' });
    expect(valueKey('false')).toEqual({ key: 'audit.values.false' });
    expect(valueKey(null)).toEqual({ key: 'audit.values.none' });
    expect(valueKey('1500000')).toEqual({ text: '1500000' });
  });
});
