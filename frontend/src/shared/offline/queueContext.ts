import { createContext, useContext, useSyncExternalStore } from 'react';
import { useCurrentAccount } from '../auth/hooks';
import type { OperationQueue, QueuedOperation } from './operationQueue';

export const OperationQueueContext = createContext<OperationQueue | null>(null);

/** La file des créations hors ligne ; null si l'application n'en a pas (tests d'un seul composant). */
export function useOperationQueue(): OperationQueue | null {
  return useContext(OperationQueueContext);
}

const none: readonly QueuedOperation[] = [];
const noSubscription = () => () => {};

/** Opérations du compte connecté (dans l'ordre de saisie), d'un type ou de tous. */
export function useQueuedOperations(kind?: string): QueuedOperation[] {
  const queue = useOperationQueue();
  const account = useCurrentAccount();
  const all = useSyncExternalStore(queue?.subscribe ?? noSubscription, queue?.getSnapshot ?? (() => none), queue?.getSnapshot ?? (() => none));
  if (!account) return [];
  return all.filter((op) => op.accountId === account.id && (!kind || op.kind === kind));
}
