import {
  createRootRoute,
  createRoute,
  createRouter,
  lazyRouteComponent,
  type RouterHistory,
} from '@tanstack/react-router';
import { parseAuditSearch } from '@/features/audit/auditText';
import { ChangePasswordPage } from '@/features/auth/pages/ChangePasswordPage';
import { LoginPage } from '@/features/auth/pages/LoginPage';
import { parseCashSearch, parseNewMovementSearch } from '@/features/cash/cashRules';
import { parsePartySearch, parseStatementSearch } from '@/features/parties/partyRules';
import { parseProfitSearch } from '@/features/profit/profitRules';
import { parseTransferSearch } from '@/features/transfers/search';
import { parseUserSearch } from '@/features/users/usersRules';
import { parsePeriodSearch } from '@/features/treasury/treasurySearch';
import { isIsoDay } from '@/shared/format/dates';
import { NotFoundPage } from './NotFoundPage';
import { RootLayout } from './RootLayout';
import { AppShell } from './shell/AppShell';
import { HomeRedirect } from './shell/HomeRedirect';
import { MorePage } from './shell/MorePage';

// Routes déclarées dans le code (pas de génération de fichiers). Mêmes routes sur mobile et sur bureau.
// staticData : titre de l'en-tête et permission exigée (voir AppShell) ; le serveur revérifie toujours.
// Les écrans métier sont chargés à la demande (lazyRouteComponent) : le premier affichage ne télécharge
// que la connexion, le cadre et l'écran ouvert (décision du 22 octobre 2026, vitesse en 3G).

const rootRoute = createRootRoute({ component: RootLayout, notFoundComponent: NotFoundPage });

const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  validateSearch: (search: Record<string, unknown>): { redirect?: string } =>
    typeof search.redirect === 'string' ? { redirect: search.redirect } : {},
  component: LoginPage,
});

const changePasswordRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/change-password',
  component: ChangePasswordPage,
});

/** Cadre des pages connectées (en-tête, navigation, agence). */
const shellRoute = createRoute({ getParentRoute: () => rootRoute, id: 'shell', component: AppShell });

/** « / » : page d'ouverture selon le compte (Tableau de bord pour le Superviseur, Transferts sinon). */
const indexRoute = createRoute({ getParentRoute: () => shellRoute, path: '/', component: HomeRedirect });

const moreRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/more',
  staticData: { titleKey: 'nav.more' },
  component: MorePage,
});

/** Taux du jour : ?date=aaaa-mm-jj (aujourd'hui sans paramètre). Lecture pour tous, saisie avec rates.set_daily. */
const ratesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/rates',
  staticData: { titleKey: 'nav.rates' },
  validateSearch: (search: Record<string, unknown>): { date?: string } => (isIsoDay(search.date) ? { date: search.date } : {}),
  component: lazyRouteComponent(() => import('@/features/rates/pages/RatesPage'), 'RatesPage'),
});

/** Tiers : filtres dans l'adresse (search, type, balanceSheet, balance, archived). */
const partiesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/parties',
  staticData: { titleKey: 'nav.parties' },
  validateSearch: parsePartySearch,
  component: lazyRouteComponent(() => import('@/features/parties/pages/PartiesPage'), 'PartiesPage'),
});

const partyDetailRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/parties/$partyId',
  staticData: { titleKey: 'parties.detail.title' },
  component: lazyRouteComponent(() => import('@/features/parties/pages/PartyDetailPage'), 'PartyDetailPage'),
});

/** Relevé d'un tiers dans une agence (agencyId, from, to dans l'adresse). */
const partyStatementRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/parties/$partyId/statement',
  staticData: { titleKey: 'parties.statement.title', permission: 'parties.view_balance' },
  validateSearch: parseStatementSearch,
  component: lazyRouteComponent(() => import('@/features/parties/pages/PartyStatementPage'), 'PartyStatementPage'),
});

/** Mise en service (phase 8) : import des tiers et de leurs soldes depuis le modèle Excel (Superviseur). */
const setupRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/setup',
  staticData: { titleKey: 'nav.setup', permission: 'parties.manage' },
  component: lazyRouteComponent(() => import('@/features/setup/pages/SetupPage'), 'SetupPage'),
});

const treasuryRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/treasury',
  staticData: { titleKey: 'nav.treasury' },
  component: lazyRouteComponent(() => import('@/features/treasury/pages/TreasuryPage'), 'TreasuryPage'),
});

/** Fiche d'un compte de trésorerie et son relevé (période dans l'adresse : from, to). */
const treasuryAccountRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/treasury/$accountId',
  staticData: { titleKey: 'treasury.detail.title' },
  validateSearch: parsePeriodSearch,
  component: lazyRouteComponent(() => import('@/features/treasury/pages/TreasuryAccountPage'), 'TreasuryAccountPage'),
});

/** Journal d'audit (audit.view) : période et famille d'actions dans l'adresse. */
const auditRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/audit',
  staticData: { titleKey: 'nav.audit', permission: 'audit.view' },
  validateSearch: parseAuditSearch,
  component: lazyRouteComponent(() => import('@/features/audit/pages/AuditPage'), 'AuditPage'),
});

/** Mes appareils : tout compte connecté. */
const mySessionsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/me/sessions',
  staticData: { titleKey: 'nav.sessions' },
  component: lazyRouteComponent(() => import('@/features/auth/pages/MySessionsPage'), 'MySessionsPage'),
});

/** Comptes (users.manage) : filtres dans l'adresse (agencyId, role, status, search). */
const usersRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/users',
  staticData: { titleKey: 'nav.users', permission: 'users.manage' },
  validateSearch: parseUserSearch,
  component: lazyRouteComponent(() => import('@/features/users/pages/UsersPage'), 'UsersPage'),
});

const userDetailRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/users/$userId',
  staticData: { titleKey: 'users.detailTitle', permission: 'users.manage' },
  component: lazyRouteComponent(() => import('@/features/users/pages/UserDetailPage'), 'UserDetailPage'),
});

/** Bilan par caisse (balance.view) : calcul lourd, seulement sur cet écran. */
const balanceRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/balance',
  staticData: { titleKey: 'nav.balance', permission: 'balance.view' },
  component: lazyRouteComponent(() => import('@/features/balance/pages/BalancePage'), 'BalancePage'),
});

/** Bénéfice (profit.view) : période dans l'adresse (from, to), agence de l'en-tête. */
const profitRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/profit',
  staticData: { titleKey: 'nav.profit', permission: 'profit.view' },
  validateSearch: parseProfitSearch,
  component: lazyRouteComponent(() => import('@/features/profit/pages/ProfitPage'), 'ProfitPage'),
});

/** Budget de la caisse (cash.budget.read) ; agence choisie par le Superviseur dans l'adresse. */
const budgetRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/budget',
  staticData: { titleKey: 'nav.budget', permission: 'cash.budget.read' },
  validateSearch: (search: Record<string, unknown>): { agencyId?: string } =>
    typeof search.agencyId === 'string' && search.agencyId ? { agencyId: search.agencyId } : {},
  component: lazyRouteComponent(() => import('@/features/budget/pages/BudgetPage'), 'BudgetPage'),
});

/** Caisse : filtres dans l'adresse (agencyId, from, to, type, account, search, page). */
const cashRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/cash',
  staticData: { titleKey: 'nav.cash', permission: 'cash.budget.read' },
  validateSearch: parseCashSearch,
  component: lazyRouteComponent(() => import('@/features/cash/pages/CashPage'), 'CashPage'),
});

/** Nouveau mouvement de caisse (cash.create) ; type et agence proposés par l'adresse. */
const createCashMovementRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/cash/new',
  staticData: { titleKey: 'cash.form.title', permission: 'cash.create' },
  validateSearch: parseNewMovementSearch,
  component: lazyRouteComponent(() => import('@/features/cash/pages/CreateCashMovementPage'), 'CreateCashMovementPage'),
});

/** Liste des transferts : filtres dans l'adresse (from, to, status, direction, search, page). */
const transfersRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/transfers',
  staticData: { titleKey: 'nav.transfers' },
  validateSearch: parseTransferSearch,
  component: lazyRouteComponent(() => import('@/features/transfers/pages/TransfersPage'), 'TransfersPage'),
});

/** Création d'un transfert (transfers.create) : 3 étapes puis le code de retrait, affiché une seule fois. */
const createTransferRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/transfers/new',
  staticData: { titleKey: 'transfers.new', permission: 'transfers.create' },
  // « Corriger » un transfert rejeté de la file hors ligne : ?fromQueue=clé.
  validateSearch: (search: Record<string, unknown>): { fromQueue?: string } =>
    typeof search.fromQueue === 'string' && search.fromQueue ? { fromQueue: search.fromQueue } : {},
  component: lazyRouteComponent(() => import('@/features/transfers/create/CreateTransferPage'), 'CreateTransferPage'),
});

/** Fiche d'un transfert : détails, historique, actions selon les droits (404 hors périmètre). */
const transferDetailRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/transfers/$transferId',
  staticData: { titleKey: 'transfers.detailTitle' },
  component: lazyRouteComponent(() => import('@/features/transfers/detail/TransferDetailPage'), 'TransferDetailPage'),
});

/** Modifier montant ou taux (Superviseur, transfers.edit_rate) : page à part (répartition et crédit complets). */
const editAmountsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/transfers/$transferId/amounts',
  staticData: { titleKey: 'transfers.amounts.title', permission: 'transfers.edit_rate' },
  component: lazyRouteComponent(() => import('@/features/transfers/amounts/EditAmountsPage'), 'EditAmountsPage'),
});

/** Livrer (transfers.deliver) : code donné par le bénéficiaire, puis remise sur les caisses de destination. */
const deliverRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/transfers/$transferId/deliver',
  staticData: { titleKey: 'transfers.deliver.title', permission: 'transfers.deliver' },
  component: lazyRouteComponent(() => import('@/features/transfers/deliver/DeliverPage'), 'DeliverPage'),
});

/** Livrer sans code (transfers.manage_code, Superviseur) : pièce d'identité et motif, puis remise. */
const deliverWithoutCodeRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/transfers/$transferId/deliver-without-code',
  staticData: { titleKey: 'transfers.deliver.withoutCodeTitle', permission: 'transfers.manage_code' },
  component: lazyRouteComponent(() => import('@/features/transfers/deliver/DeliverPage'), 'DeliverWithoutCodePage'),
});

/** Tableau de bord (balance.view) : période ?from=&to= (aujourd'hui sans paramètre). */
const dashboardRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/dashboard',
  staticData: { titleKey: 'nav.dashboard', permission: 'balance.view' },
  validateSearch: (search: Record<string, unknown>): { from?: string; to?: string } => ({
    ...(isIsoDay(search.from) ? { from: search.from } : {}),
    ...(isIsoDay(search.to) ? { to: search.to } : {}),
  }),
  component: lazyRouteComponent(() => import('@/features/transfers/pages/DashboardPage'), 'DashboardPage'),
});

const shellChildren = [
  indexRoute,
  moreRoute,
  setupRoute,
  transfersRoute,
  createTransferRoute,
  transferDetailRoute,
  editAmountsRoute,
  deliverRoute,
  deliverWithoutCodeRoute,
  partiesRoute,
  partyDetailRoute,
  partyStatementRoute,
  cashRoute,
  createCashMovementRoute,
  budgetRoute,
  ratesRoute,
  profitRoute,
  balanceRoute,
  dashboardRoute,
  usersRoute,
  userDetailRoute,
  treasuryRoute,
  treasuryAccountRoute,
  auditRoute,
  mySessionsRoute,
] as const;

const routeTree = rootRoute.addChildren([loginRoute, changePasswordRoute, shellRoute.addChildren(shellChildren)]);

export function createAppRouter(history?: RouterHistory) {
  return createRouter({ routeTree, history, defaultPreload: false });
}

export type AppRouter = ReturnType<typeof createAppRouter>;

declare module '@tanstack/react-router' {
  interface Register {
    router: AppRouter;
  }
  interface StaticDataRouteOption {
    /** Clé de traduction du titre affiché dans l'en-tête. */
    titleKey?: string;
    /** Permission exigée pour afficher la page. */
    permission?: string;
  }
}
