import type { ReactNode } from 'react';
import { usePermission } from './hooks';

/** Affiche ses enfants seulement avec la permission ; sinon rien (règle des maquettes : absent, pas grisé). */
export function Can({ permission, children }: { permission: string; children: ReactNode }) {
  return usePermission(permission) ? children : null;
}
