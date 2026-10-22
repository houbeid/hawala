import { checkPassword, passwordIsValid } from './passwordRules';

describe('règles du mot de passe (identiques au backend)', () => {
  it('exige 8 caractères et un chiffre', () => {
    expect(checkPassword('abcdefg')).toEqual({ minLength: false, hasDigit: false });
    expect(checkPassword('abcdefgh')).toEqual({ minLength: true, hasDigit: false });
    expect(checkPassword('abcdefg1')).toEqual({ minLength: true, hasDigit: true });
  });

  it('accepte un mot de passe qui respecte les deux règles', () => {
    expect(passwordIsValid('nouveaupass1')).toBe(true);
    expect(passwordIsValid('court1')).toBe(false);
  });
});
