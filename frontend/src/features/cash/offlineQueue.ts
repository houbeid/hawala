import type { CashMovementType } from './types';

// Type d'opération des mouvements de caisse dans la file des créations hors ligne. Hors ligne, seuls l'encaissement
// et le décaissement se saisissent (décision du 24 octobre 2026) ; les autres opérations exigent le réseau.
export const CASH_QUEUE_KIND = 'cashMovement';

export const OFFLINE_CASH_TYPES: readonly CashMovementType[] = ['Receipt', 'Payment'];
