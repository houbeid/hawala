import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useAgencies } from '@/features/organization/queries';
import type { Agency } from '@/features/organization/types';
import { AgencyScopeContext, type AgencyRef, type AgencyScope } from '@/shared/agency/agencyScope';
import type { CurrentAccount } from '@/shared/auth/types';
import { readPreference, writePreference } from '@/shared/storage';

// Choix de l'agence du Superviseur gardé sur l'appareil (confort) ; un agent n'a pas de choix.
const STORAGE_KEY = 'hawala.supervisorAgency';

function toRef(agency: Agency): AgencyRef {
  return { id: agency.id, code: agency.code, name: agency.name, color: agency.color, currency: agency.currency };
}

function computeScope(account: CurrentAccount, agencies: Agency[] | undefined, selected: string): AgencyScope {
  if (account.agency) {
    const known = agencies?.find((a) => a.id === account.agency?.id);
    return { kind: 'locked', agency: known ? toRef(known) : { ...account.agency, currency: null } };
  }
  const refs = (agencies ?? []).map(toRef);
  const chosen = refs.find((a) => a.id === selected);
  return chosen ? { kind: 'selected', agency: chosen, agencies: refs } : { kind: 'all', agencies: refs };
}

/** Pose la couleur de l'agence sur <html> (filet d'en-tête, badge) ; neutre quand le Superviseur voit tout. */
function useAgencyTheme(scope: AgencyScope): void {
  const code = scope.kind === 'all' ? 'ALL' : scope.agency.code;
  const color = scope.kind === 'all' ? 'var(--muted)' : scope.agency.color;
  useEffect(() => {
    const root = document.documentElement;
    root.dataset.agency = code;
    root.style.setProperty('--agency', color);
    return () => {
      delete root.dataset.agency;
      root.style.removeProperty('--agency');
    };
  }, [code, color]);
}

export function AgencyScopeProvider({ account, children }: { account: CurrentAccount; children: ReactNode }) {
  const agencies = useAgencies();
  const [selected, setSelected] = useState(() => readPreference(STORAGE_KEY) ?? 'all');

  const scope = useMemo(() => computeScope(account, agencies.data, selected), [account, agencies.data, selected]);
  useAgencyTheme(scope);

  const select = useCallback(
    (agencyId: string | 'all') => {
      if (account.agency) return;
      setSelected(agencyId);
      writePreference(STORAGE_KEY, agencyId);
    },
    [account.agency],
  );

  const value = useMemo(() => ({ scope, select }), [scope, select]);
  return <AgencyScopeContext.Provider value={value}>{children}</AgencyScopeContext.Provider>;
}
