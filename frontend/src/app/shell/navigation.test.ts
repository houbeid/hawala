import { agentAccount, cashierAccount, readOnlyAccount, supervisorAccount } from '@/test/fixtures';
import { bottomBarItems, homePathFor, moreMenuItems, sidebarItems } from './navigation';

const ids = (items: { id: string }[]) => items.map((item) => item.id);

describe('barre du bas selon le rôle (maquette 02)', () => {
  it('Agent : Transferts, Nouveau, Tiers', () => {
    expect(ids(bottomBarItems('Agent', agentAccount.permissions))).toEqual(['transfers', 'newTransfer', 'parties']);
  });

  it('Caissier : Transferts, Caisse, Budget', () => {
    expect(ids(bottomBarItems('Cashier', cashierAccount.permissions))).toEqual(['transfers', 'cash', 'budget']);
  });

  it("Superviseur : Tableau de bord (sa page d'ouverture), Transferts, Tiers", () => {
    expect(ids(bottomBarItems('Supervisor', supervisorAccount.permissions))).toEqual(['dashboard', 'transfers', 'parties']);
  });

  it('Consultation : Transferts, Tiers', () => {
    expect(ids(bottomBarItems('ReadOnly', readOnlyAccount.permissions))).toEqual(['transfers', 'parties']);
  });

  it("une permission retirée au compte fait disparaître l'entrée, sans la griser", () => {
    const withoutCreate = agentAccount.permissions.filter((p) => p !== 'transfers.create');
    expect(ids(bottomBarItems('Agent', withoutCreate))).toEqual(['transfers', 'parties']);
  });
});

describe('« Plus » et barre latérale', () => {
  it("met dans « Plus » ce qui est permis et absent de la barre, sans « Nouveau »", () => {
    expect(ids(moreMenuItems('Agent', agentAccount.permissions))).toEqual(['cash', 'budget', 'rates', 'treasury', 'sessions']);
  });

  it('ne propose ni bénéfice, ni bilan, ni comptes, ni audit sans leur permission', () => {
    const more = ids(moreMenuItems('Agent', agentAccount.permissions));
    for (const id of ['profit', 'balance', 'dashboard', 'users', 'audit']) expect(more).not.toContain(id);
  });

  it('montre tout au Superviseur sur ordinateur, sauf « Nouveau » (bouton de la liste)', () => {
    expect(ids(sidebarItems(supervisorAccount.permissions))).toEqual([
      'dashboard',
      'transfers',
      'parties',
      'cash',
      'budget',
      'rates',
      'profit',
      'balance',
      'users',
      'treasury',
      'audit',
      'sessions',
    ]);
  });
});

describe("page d'ouverture (décision du 22 octobre 2026)", () => {
  it('Tableau de bord pour le Superviseur, Transferts pour les autres', () => {
    expect(homePathFor('Supervisor')).toBe('/dashboard');
    expect(homePathFor('Agent')).toBe('/transfers');
    expect(homePathFor('Cashier')).toBe('/transfers');
    expect(homePathFor('ReadOnly')).toBe('/transfers');
  });
});
