// Clé Idempotency-Key d'une création (transfert, mouvement de caisse) : générée UNE fois à l'ouverture
// du formulaire et réutilisée à chaque nouvel envoi du même formulaire (double clic, réseau coupé,
// synchronisation hors ligne). Le serveur renvoie alors l'opération déjà créée au lieu d'en créer une seconde.

export const IDEMPOTENCY_HEADER = 'Idempotency-Key';

export function newIdempotencyKey(): string {
  return crypto.randomUUID();
}
