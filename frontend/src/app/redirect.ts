// Page où revenir après la connexion (paramètre « redirect » de /login). Seulement un chemin interne :
// jamais une autre origine (« //site », « https://… »), jamais les écrans de connexion eux-mêmes.

const AUTH_PATHS = ['/login', '/change-password'];

export function safeRedirect(value: unknown): string {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) return '/';
  const path = value.split(/[?#]/)[0] ?? '/';
  return AUTH_PATHS.includes(path) ? '/' : value;
}

export function isAuthPath(pathname: string): boolean {
  return AUTH_PATHS.includes(pathname);
}
