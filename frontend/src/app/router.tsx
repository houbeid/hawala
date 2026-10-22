import {
  createRootRoute,
  createRoute,
  createRouter,
  lazyRouteComponent,
  type RouterHistory,
} from '@tanstack/react-router';
import { ChangePasswordPage } from '@/features/auth/pages/ChangePasswordPage';
import { LoginPage } from '@/features/auth/pages/LoginPage';
import { parseTransferSearch } from '@/features/transfers/search';
import { isIsoDay } from '@/shared/format/dates';
import { NotFoundPage } from './NotFoundPage';
import { RootLayout } from './RootLayout';
import { AppShell } from './shell/AppShell';
import { HomeRedirect } from './shell/HomeRedirect';
import { MorePage } from './shell/MorePage';
import { PlaceholderPage } from './shell/PlaceholderPage';

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
  component: lazyRouteComponent(() => import('@/features/transfers/create/CreateTransferPage'), 'CreateTransferPage'),
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

// POINT OUVERT : écrans provisoires, remplacés un par un aux tours suivants.
function placeholder<const TPath extends string>(path: TPath, titleKey: string, permission?: string) {
  return createRoute({
    getParentRoute: () => shellRoute,
    path,
    staticData: { titleKey, permission },
    component: PlaceholderPage,
  });
}

const shellChildren = [
  indexRoute,
  moreRoute,
  transfersRoute,
  createTransferRoute,
  placeholder('/transfers/$transferId', 'transfers.detailTitle'),
  placeholder('/parties', 'nav.parties'),
  placeholder('/cash', 'nav.cash', 'cash.budget.read'),
  placeholder('/budget', 'nav.budget', 'cash.budget.read'),
  ratesRoute,
  placeholder('/profit', 'nav.profit', 'profit.view'),
  placeholder('/balance', 'nav.balance', 'balance.view'),
  dashboardRoute,
  placeholder('/users', 'nav.users', 'users.manage'),
  placeholder('/treasury', 'nav.treasury'),
  placeholder('/audit', 'nav.audit', 'audit.view'),
  placeholder('/me/sessions', 'nav.sessions'),
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
