import { describe, expect, it } from 'vitest';
import { availableUserActions, creatableRoles, grantedPermissions, parseLimit, parseUserSearch, statusMarks } from './usersRules';
import type { UserSummary } from './types';

function user(overrides: Partial<UserSummary> = {}): UserSummary {
  return {
    id: 'u1',
    fullName: 'Agent Fictif',
    email: 'agent@example.com',
    phone: null,
    role: 'Agent',
    agency: null,
    status: 'Active',
    mustChangePassword: false,
    lockedUntil: null,
    lastLoginAt: null,
    ...overrides,
  };
}

describe('règles des comptes', () => {
  it('lit les filtres de l’adresse et ignore les valeurs inconnues', () => {
    expect(parseUserSearch({ agencyId: 'a', role: 'Cashier', status: 'Blocked', search: ' sidi ' })).toEqual({
      agencyId: 'a',
      role: 'Cashier',
      status: 'Blocked',
      search: 'sidi',
    });
    expect(parseUserSearch({ role: 'Boss', status: 'Gone', search: ' ' })).toEqual({});
  });

  it('un non-Superviseur ne crée pas de Superviseur', () => {
    expect(creatableRoles('Supervisor')).toEqual(['Supervisor', 'Agent', 'Cashier', 'ReadOnly']);
    expect(creatableRoles('Agent')).toEqual(['Agent', 'Cashier', 'ReadOnly']);
  });

  it('personne ne bloque, ne réinitialise ni ne change le rôle de son propre compte', () => {
    const supervisor = { id: 's1', role: 'Supervisor' as const };
    expect(availableUserActions(user(), supervisor)).toEqual({ editProfile: true, block: true, unblock: false, resetPassword: true, roleAndAgency: true });
    expect(availableUserActions(user({ id: 's1', role: 'Supervisor' }), supervisor)).toEqual({
      editProfile: true,
      block: false,
      unblock: false,
      resetPassword: false,
      roleAndAgency: false,
    });
    expect(availableUserActions(user({ status: 'Blocked' }), { id: 'm', role: 'Agent' })).toMatchObject({ block: false, unblock: true, roleAndAgency: false });
  });

  it('écrit l’état : actif ou bloqué, mot de passe temporaire, verrouillage encore en cours', () => {
    const now = new Date('2026-10-24T12:00:00Z');
    expect(statusMarks(user({ mustChangePassword: true, lockedUntil: '2026-10-24T12:10:00Z' }), now)).toEqual(['active', 'temporaryPassword', 'locked']);
    expect(statusMarks(user({ status: 'Blocked', lockedUntil: '2026-10-24T11:00:00Z' }), now)).toEqual(['blocked']);
  });
});

describe('permissions et plafonds d’un compte', () => {
  it('donne la liste complète des permissions accordées après les bascules', () => {
    const states = [
      { permission: 'transfers.deliver', granted: true },
      { permission: 'transfers.cancel', granted: false },
      { permission: 'cash.budget.save', granted: true },
    ];
    expect(grantedPermissions(states, { 'transfers.cancel': true, 'cash.budget.save': false })).toEqual(['transfers.cancel', 'transfers.deliver']);
    expect(grantedPermissions(states, {})).toEqual(['cash.budget.save', 'transfers.deliver']);
  });

  it('lit un plafond facultatif : vide = pas de limite, zéro permis, trop précis refusé', () => {
    expect(parseLimit('', 'MRO')).toEqual({ ok: true, value: null });
    expect(parseLimit('0', 'XOF')).toEqual({ ok: true, value: 0 });
    expect(parseLimit('1 500 000', 'MRO')).toEqual({ ok: true, value: 1_500_000 });
    expect(parseLimit('10,5', 'XOF')).toEqual({ ok: false, error: 'tooPrecise' });
    expect(parseLimit('abc', 'MRO')).toEqual({ ok: false, error: 'invalid' });
  });
});
