import { createContext, useContext } from 'react';
import type { RealtimeStatus } from './connection';

/** État de la connexion temps réel, fourni par RealtimeProvider ; « off » sans session. */
export const RealtimeStatusContext = createContext<RealtimeStatus>('off');

export function useRealtimeStatus(): RealtimeStatus {
  return useContext(RealtimeStatusContext);
}
