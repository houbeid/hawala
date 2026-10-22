import type { AuthResponse, CurrentAccount } from '@/shared/auth/types';

// Données fictives uniquement (jamais de vrais noms, téléphones ou emails).

export function tokens(accessToken: string, overrides: Partial<AuthResponse> = {}): AuthResponse {
  return {
    accessToken,
    accessTokenExpiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
    mustChangePassword: false,
    ...overrides,
  };
}

export const agentAccount: CurrentAccount = {
  id: '01900000-0000-7000-8000-000000000001',
  email: 'agent.nkc@example.com',
  phone: '+22220000001',
  fullName: 'Agent Test Nouakchott',
  role: 'Agent',
  mustChangePassword: false,
  agency: { id: '01900000-0000-7000-8000-0000000000a1', code: 'NKC', name: 'Nouakchott', color: '#1E8E3E' },
  permissions: ['cash.budget.read', 'transfers.create', 'transfers.deliver'],
};
