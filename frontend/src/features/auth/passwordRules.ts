// Règles du mot de passe, identiques au backend (ASP.NET Identity) : 8 caractères minimum dont au moins
// un chiffre. L'interface les montre en direct ; le serveur reste juge (Identity.Password.TooWeak).

export const PASSWORD_MIN_LENGTH = 8;

export interface PasswordRuleCheck {
  minLength: boolean;
  hasDigit: boolean;
}

export function checkPassword(password: string): PasswordRuleCheck {
  return {
    minLength: password.length >= PASSWORD_MIN_LENGTH,
    hasDigit: /[0-9]/.test(password),
  };
}

export function passwordIsValid(password: string): boolean {
  const check = checkPassword(password);
  return check.minLength && check.hasDigit;
}
