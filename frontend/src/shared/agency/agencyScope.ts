import { createContext, useContext } from 'react';

// Agence sur laquelle travaille l'écran (décision des maquettes 02) :
// - Agent, Caissier, Consultation : leur agence, VERROUILLÉE (badge non cliquable) ;
// - Superviseur : « les deux agences » par défaut, ou une agence choisie dans l'en-tête.
// Ce n'est qu'un filtre d'affichage : le serveur applique toujours le périmètre du compte.

export interface AgencyRef {
  id: string;
  code: string;
  name: string;
  color: string;
  /** Null tant que la liste des agences n'est pas chargée (compte d'agent, premier affichage). */
  currency: 'MRO' | 'XOF' | null;
}

export type AgencyScope =
  | { kind: 'locked'; agency: AgencyRef }
  | { kind: 'all'; agencies: AgencyRef[] }
  | { kind: 'selected'; agency: AgencyRef; agencies: AgencyRef[] };

export interface AgencyScopeValue {
  scope: AgencyScope;
  /** Superviseur seulement : un identifiant d'agence ou « all ». Sans effet pour un compte verrouillé. */
  select(agencyId: string | 'all'): void;
}

export const AgencyScopeContext = createContext<AgencyScopeValue | null>(null);

export function useAgencyScope(): AgencyScopeValue {
  const value = useContext(AgencyScopeContext);
  if (!value) throw new Error('useAgencyScope doit être utilisé sous <AgencyScopeProvider>.');
  return value;
}

/** Agence à passer aux requêtes (`agencyId`) : undefined = toutes celles que le compte peut voir. */
export function scopedAgencyId(scope: AgencyScope): string | undefined {
  return scope.kind === 'all' ? undefined : scope.agency.id;
}
