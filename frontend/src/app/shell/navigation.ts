import type { LinkProps } from '@tanstack/react-router';
import type { Role } from '@/shared/auth/types';
import type { IconName } from '@/shared/ui/Icon';

// Entrées de navigation (maquette 02). Une entrée sans la permission requise n'existe pas
// (absente, jamais grisée) ; le serveur revérifie chaque action.

export type NavGroup = 'operations' | 'pilotage' | 'administration' | 'account';

export interface NavItem {
  id: string;
  to: NonNullable<LinkProps['to']>;
  labelKey: string;
  icon: IconName;
  group: NavGroup;
  /** Permission requise ; absente = tout compte connecté. */
  permission?: string;
}

export const NAV_ITEMS: readonly NavItem[] = [
  // Page d'ouverture du Superviseur (décision du 22 octobre 2026) : en tête de ses menus.
  { id: 'dashboard', to: '/dashboard', labelKey: 'nav.dashboard', icon: 'chart', group: 'operations', permission: 'balance.view' },
  { id: 'transfers', to: '/transfers', labelKey: 'nav.transfers', icon: 'transfer', group: 'operations' },
  { id: 'newTransfer', to: '/transfers/new', labelKey: 'nav.newTransfer', icon: 'plus', group: 'operations', permission: 'transfers.create' },
  { id: 'parties', to: '/parties', labelKey: 'nav.parties', icon: 'users', group: 'operations' },
  { id: 'cash', to: '/cash', labelKey: 'nav.cash', icon: 'wallet', group: 'operations', permission: 'cash.budget.read' },
  { id: 'budget', to: '/budget', labelKey: 'nav.budget', icon: 'chart', group: 'pilotage', permission: 'cash.budget.read' },
  { id: 'rates', to: '/rates', labelKey: 'nav.rates', icon: 'rate', group: 'pilotage' },
  { id: 'profit', to: '/profit', labelKey: 'nav.profit', icon: 'trend', group: 'pilotage', permission: 'profit.view' },
  { id: 'balance', to: '/balance', labelKey: 'nav.balance', icon: 'cash', group: 'pilotage', permission: 'balance.view' },
  { id: 'users', to: '/users', labelKey: 'nav.users', icon: 'user', group: 'administration', permission: 'users.manage' },
  { id: 'treasury', to: '/treasury', labelKey: 'nav.treasury', icon: 'bank', group: 'administration' },
  { id: 'audit', to: '/audit', labelKey: 'nav.audit', icon: 'shield', group: 'administration', permission: 'audit.view' },
  { id: 'sessions', to: '/me/sessions', labelKey: 'nav.sessions', icon: 'devices', group: 'account' },
];

export const GROUP_ORDER: readonly NavGroup[] = ['operations', 'pilotage', 'administration', 'account'];

/**
 * Trois emplacements de la barre du bas par rôle (le 4e est « Plus »), tirés des maquettes :
 * l'Agent a « Nouveau » au centre (son action principale), le Caissier la caisse et le budget,
 * le Superviseur son tableau de bord (sa page d'ouverture, décision du 22 octobre 2026 : la caisse passe
 * dans « Plus »), les transferts et les tiers ; la Consultation lit les transferts et les tiers.
 * Une entrée dont la permission a été retirée au compte disparaît simplement.
 */
const BOTTOM_BAR: Record<Role, readonly string[]> = {
  Agent: ['transfers', 'newTransfer', 'parties'],
  Cashier: ['transfers', 'cash', 'budget'],
  Supervisor: ['dashboard', 'transfers', 'parties'],
  ReadOnly: ['transfers', 'parties'],
};

export function allowedItems(permissions: readonly string[]): NavItem[] {
  return NAV_ITEMS.filter((item) => !item.permission || permissions.includes(item.permission));
}

export function bottomBarItems(role: Role, permissions: readonly string[]): NavItem[] {
  const allowed = allowedItems(permissions);
  return BOTTOM_BAR[role].flatMap((id) => allowed.filter((item) => item.id === id));
}

/** Ce qui n'est pas dans la barre du bas va dans « Plus » (sauf « Nouveau transfert », accessible depuis la liste). */
export function moreMenuItems(role: Role, permissions: readonly string[]): NavItem[] {
  const inBar = new Set(bottomBarItems(role, permissions).map((item) => item.id));
  return allowedItems(permissions).filter((item) => !inBar.has(item.id) && item.id !== 'newTransfer');
}

/** Barre latérale (ordinateur) : tout ce qui est permis, sauf « Nouveau transfert » (bouton de la liste). */
export function sidebarItems(permissions: readonly string[]): NavItem[] {
  return allowedItems(permissions).filter((item) => item.id !== 'newTransfer');
}

/**
 * Page d'ouverture (décision du 22 octobre 2026) : Superviseur → Tableau de bord (calcul léger) ;
 * Agent, Caissier, Consultation → Transferts. Le bilan, lourd, n'est jamais ouvert au démarrage.
 */
export function homePathFor(role: Role | undefined): '/dashboard' | '/transfers' {
  return role === 'Supervisor' ? '/dashboard' : '/transfers';
}
