import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import type { CurrentAccount } from '@/shared/auth/types';
import { Icon } from '@/shared/ui/Icon';
import { GROUP_ORDER, type NavItem } from './navigation';

// Barre latérale (ordinateur, ≥ 1024 px, bureau D1) : toutes les entrées permises, par groupe ;
// en bas, le compte connecté et la déconnexion.

const linkClass =
  'flex min-h-11 items-center gap-2.5 rounded-[10px] px-2.5 font-semibold text-muted no-underline ' +
  'data-[status=active]:bg-surface-2 data-[status=active]:text-fg data-[status=active]:shadow-[inset_3px_0_0_var(--accent)] ' +
  'rtl:data-[status=active]:shadow-[inset_-3px_0_0_var(--accent)]';

export function Sidebar({ items, account, onSignOut }: { items: NavItem[]; account: CurrentAccount; onSignOut: () => void }) {
  const { t } = useTranslation();

  return (
    <aside className="sticky top-0 hidden h-dvh w-[236px] shrink-0 flex-col gap-1 overflow-y-auto border-e border-border bg-surface px-3 py-4 lg:flex">
      <div className="px-2.5 pb-3.5 text-lg font-bold">{t('app.name')}</div>
      <nav aria-label={t('shell.sideNav')} className="flex flex-col gap-1">
        {GROUP_ORDER.map((group) => {
          const inGroup = items.filter((item) => item.group === group);
          if (inGroup.length === 0) return null;
          return (
            <div key={group} className="flex flex-col gap-1">
              <div className="px-2.5 pt-3.5 pb-1 text-xs font-bold tracking-wide text-muted">{t(`nav.groups.${group}`)}</div>
              {inGroup.map((item) => (
                <Link key={item.id} to={item.to} className={linkClass} activeOptions={{ exact: item.id === 'transfers' }}>
                  <Icon name={item.icon} />
                  {t(item.labelKey)}
                </Link>
              ))}
            </div>
          );
        })}
      </nav>
      <div className="mt-auto flex flex-col gap-1 border-t border-border pt-3">
        <div className="px-2.5">
          <div className="truncate font-semibold">{account.fullName}</div>
          <div className="text-sm text-muted">{t(`roles.${account.role}`)}</div>
        </div>
        <button type="button" onClick={onSignOut} className={`${linkClass} w-full text-start`}>
          <Icon name="signOut" />
          {t('auth.signOut')}
        </button>
      </div>
    </aside>
  );
}
