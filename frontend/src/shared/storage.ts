// Préférences de confort de l'appareil (langue, thème) : localStorage peut être absent
// ou refusé (navigation privée), l'app doit alors marcher avec les valeurs par défaut.
// Jamais de jeton ni de donnée métier ici.

export function readPreference(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

export function writePreference(key: string, value: string): void {
  try {
    window.localStorage.setItem(key, value);
  } catch {
    // Préférence perdue au prochain chargement : sans conséquence.
  }
}
