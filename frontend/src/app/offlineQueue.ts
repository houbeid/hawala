import type { QueryClient } from '@tanstack/react-query';
import { createCashMovement } from '@/features/cash/api';
import { CASH_QUEUE_KIND } from '@/features/cash/offlineQueue';
import type { CreateCashMovementRequest } from '@/features/cash/types';
import { createTransfer } from '@/features/transfers/api';
import { TRANSFER_QUEUE_KIND } from '@/features/transfers/offlineQueue';
import type { CreateTransferRequest } from '@/features/transfers/types';
import type { KeyValueStore } from '@/shared/offline/offlineStore';
import { OperationQueue } from '@/shared/offline/operationQueue';

// File des créations hors ligne de l'application : comment envoyer chaque type d'opération, et quelles listes relire
// après un envoi. Un transfert envoyé reste dans la file avec sa réponse (référence et code de retrait) jusqu'à ce que
// l'agent l'ait vue ; un mouvement de caisse envoyé disparaît.

export const QUEUE_KINDS = { transfer: TRANSFER_QUEUE_KIND, cashMovement: CASH_QUEUE_KIND } as const;

export function createOperationQueue(store: KeyValueStore, queryClient: QueryClient): OperationQueue {
  return new OperationQueue(
    store,
    {
      [QUEUE_KINDS.transfer]: {
        send: (http, request, key) => createTransfer(http, request as CreateTransferRequest, key),
        keepResult: () => true,
      },
      [QUEUE_KINDS.cashMovement]: {
        send: (http, request, key) => createCashMovement(http, request as CreateCashMovementRequest, key),
      },
    },
    (kind) => {
      const roots = kind === QUEUE_KINDS.transfer ? ['transfers', 'treasury', 'parties', 'profit', 'budget'] : ['cash', 'treasury', 'parties', 'budget'];
      for (const root of roots) void queryClient.invalidateQueries({ queryKey: [root] });
    },
  );
}
