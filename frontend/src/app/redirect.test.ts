import { safeRedirect } from './redirect';

describe('safeRedirect', () => {
  it('garde un chemin interne avec ses paramètres', () => {
    expect(safeRedirect('/transfers?status=NotDelivered')).toBe('/transfers?status=NotDelivered');
  });

  it("refuse toute autre origine et revient à l'accueil", () => {
    expect(safeRedirect('https://exemple.test/piege')).toBe('/');
    expect(safeRedirect('//exemple.test')).toBe('/');
    expect(safeRedirect('/\\exemple.test')).toBe('/');
    expect(safeRedirect('transfers')).toBe('/');
    expect(safeRedirect(undefined)).toBe('/');
  });

  it('ne renvoie jamais vers les écrans de connexion', () => {
    expect(safeRedirect('/login')).toBe('/');
    expect(safeRedirect('/change-password?x=1')).toBe('/');
  });
});
