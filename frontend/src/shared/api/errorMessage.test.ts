import i18n from '@/i18n';
import { ApiError, ClientErrorCodes } from './errors';
import { errorMessage } from './errorMessage';

describe('errorMessage', () => {
  afterEach(async () => {
    await i18n.changeLanguage('fr');
  });

  it('traduit un code du serveur', () => {
    const error = new ApiError({ status: 401, code: 'Identity.Auth.InvalidCredentials' });
    expect(errorMessage(i18n, error)).toBe('Identifiant ou mot de passe incorrect.');
  });

  it('traduit en arabe quand l’interface est en arabe', async () => {
    await i18n.changeLanguage('ar');
    const error = new ApiError({ status: 403, code: 'Identity.Auth.AccountBlocked' });
    expect(errorMessage(i18n, error)).toBe('هذا الحساب محظور. اتصل بالمشرف.');
  });

  it('donne le délai d’attente d’un 429', () => {
    const error = new ApiError({ status: 429, code: 'Api.RateLimited', retryAfterSeconds: 37 });
    expect(errorMessage(i18n, error)).toBe('Trop de tentatives. Réessayez dans 37 s.');
  });

  it('signale le réseau coupé', () => {
    const error = new ApiError({ status: 0, code: ClientErrorCodes.Network });
    expect(errorMessage(i18n, error)).toBe('Pas de connexion au serveur. Vérifiez le réseau et réessayez.');
  });

  it("n'affiche jamais le texte du serveur pour un code non traduit", () => {
    const forbidden = new ApiError({ status: 403, code: 'Module.Inconnu', message: 'texte serveur' });
    const other = new ApiError({ status: 409, code: 'Module.Autre', message: 'texte serveur' });

    expect(errorMessage(i18n, forbidden)).toBe("Vous n'avez pas le droit de faire cette action.");
    expect(errorMessage(i18n, other)).toBe('Une erreur est survenue. Réessayez.');
  });

  it('donne un message générique pour une erreur inattendue', () => {
    expect(errorMessage(i18n, new TypeError('x'))).toBe('Une erreur est survenue. Réessayez.');
  });
});
