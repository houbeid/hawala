import { Link, Outlet, useMatches } from '@tanstack/react-router';
import { RealtimeNotice } from '@/shared/realtime/RealtimeNotice';
import { useTranslation } from 'react-i18next';
import { useCurrentAccount, useSessionManager } from '@/shared/auth/hooks';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { AgencyScopeProvider } from './AgencyScopeProvider';
import { BottomNav } from './BottomNav';
import { bottomBarItems, sidebarItems } from './navigation';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';

// Cadre de toutes les pages connectées (maquette 02, bureau D1) : barre latérale sur ordinateur,
// barre du bas sur téléphone, en-tête avec l'agence. Chaque route déclare son titre et sa permission
// (staticData) ; sans la permission, la page n'est pas affichée (le serveur refuserait de toute façon).

function usePageMeta(): { titleKey: string; permission?: string } {
  const matches = useMatches();
  let titleKey = 'app.name';
  let permission: string | undefined;
  for (const match of matches) {
    if (match.staticData.titleKey) titleKey = match.staticData.titleKey;
    if (match.staticData.permission) permission = match.staticData.permission;
  }
  return { titleKey, permission };
}

function ForbiddenNotice() {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-3">
      <Notice tone="warning">{t('shell.forbidden')}</Notice>
      <Link to="/transfers" className="font-semibold text-fg underline">
        {t('common.backHome')}
      </Link>
    </div>
  );
}

export function AppShell() {
  const { t } = useTranslation();
  const account = useCurrentAccount();
  const session = useSessionManager();
  const online = useOnlineStatus();
  const { titleKey, permission } = usePageMeta();
  if (!account) return null;

  const allowed = !permission || account.permissions.includes(permission);

  return (
    <AgencyScopeProvider account={account}>
      <div className="min-h-dvh lg:flex">
        <Sidebar items={sidebarItems(account.permissions)} account={account} onSignOut={() => void session.logout()} />
        <div className="flex min-w-0 flex-1 flex-col">
          <TopBar title={t(titleKey)} />
          {!online && (
            <div role="status" className="flex items-center gap-2 bg-warn-bg px-4 py-2.5 text-sm font-semibold text-warn-fg">
              <Icon name="wifiOff" />
              <span>{t('shell.offlineBanner')}</span>
            </div>
          )}
          <main className="mx-auto w-full max-w-6xl flex-1 px-4 pt-4 pb-24 lg:px-6 lg:pb-8">
            <RealtimeNotice />
            {allowed ? <Outlet /> : <ForbiddenNotice />}
          </main>
        </div>
        <BottomNav items={bottomBarItems(account.role, account.permissions)} />
      </div>
    </AgencyScopeProvider>
  );
}
