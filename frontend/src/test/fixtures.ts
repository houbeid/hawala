import type { Agency } from '@/features/organization/types';
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

export const nkc: Agency = {
  id: '01900000-0000-7000-8000-0000000000a1',
  code: 'NKC',
  name: 'Nouakchott',
  country: 'Mauritania',
  currency: 'MRO',
  color: '#1E8E3E',
  timeZoneId: 'Africa/Nouakchott',
};

export const dkr: Agency = {
  id: '01900000-0000-7000-8000-0000000000a2',
  code: 'DKR',
  name: 'Dakar',
  country: 'Senegal',
  currency: 'XOF',
  color: '#1565C0',
  timeZoneId: 'Africa/Dakar',
};

export const supervisorAccount: CurrentAccount = {
  id: '01900000-0000-7000-8000-000000000009',
  email: 'superviseur@example.com',
  phone: null,
  fullName: 'Superviseur Test',
  role: 'Supervisor',
  mustChangePassword: false,
  agency: null,
  permissions: [
    'agencies.manage',
    'audit.view',
    'balance.manage',
    'balance.view',
    'cash.budget.read',
    'cash.budget.save',
    'cash.create',
    'cash.manage',
    'export',
    'parties.create',
    'parties.manage',
    'parties.view_balance',
    'profit.view',
    'rates.set_daily',
    'transfers.cancel',
    'transfers.create',
    'transfers.credit',
    'transfers.deliver',
    'transfers.edit_rate',
    'transfers.manage_code',
    'treasury.manage',
    'users.manage',
  ],
};

export const cashierAccount: CurrentAccount = {
  id: '01900000-0000-7000-8000-000000000003',
  email: 'caissier.dkr@example.com',
  phone: null,
  fullName: 'Caissier Test Dakar',
  role: 'Cashier',
  mustChangePassword: false,
  agency: { id: dkr.id, code: 'DKR', name: 'Dakar', color: '#1565C0' },
  permissions: ['cash.budget.read', 'cash.budget.save', 'cash.create', 'transfers.deliver'],
};

export const readOnlyAccount: CurrentAccount = {
  id: '01900000-0000-7000-8000-000000000004',
  email: 'consultation.dkr@example.com',
  phone: null,
  fullName: 'Consultation Test Dakar',
  role: 'ReadOnly',
  mustChangePassword: false,
  agency: { id: dkr.id, code: 'DKR', name: 'Dakar', color: '#1565C0' },
  permissions: ['cash.budget.read', 'export', 'parties.view_balance'],
};

export const agentAccount: CurrentAccount = {
  id: '01900000-0000-7000-8000-000000000001',
  email: 'agent.nkc@example.com',
  phone: '+22220000001',
  fullName: 'Agent Test Nouakchott',
  role: 'Agent',
  mustChangePassword: false,
  agency: { id: '01900000-0000-7000-8000-0000000000a1', code: 'NKC', name: 'Nouakchott', color: '#1E8E3E' },
  // Droits par défaut du rôle Agent (tableau validé le 20 octobre 2026).
  permissions: [
    'cash.budget.read',
    'cash.create',
    'parties.create',
    'parties.view_balance',
    'transfers.create',
    'transfers.credit',
    'transfers.deliver',
  ],
};
