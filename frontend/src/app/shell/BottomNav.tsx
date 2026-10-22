import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Icon } from '@/shared/ui/Icon';
import type { NavItem } from './navigation';

// Barre du bas (téléphone, < 1024 px) : 3 emplacements selon le rôle + « Plus » ; pas de menu burger.
// « Nouveau » (création de transfert) est mis en avant au centre pour l'Agent.

const linkClass =
  'flex min-h-16 flex-1 flex-col items-center justify-center gap-0.5 text-xs font-semibold text-muted no-underline ' +
  'data-[status=active]:text-fg data-[status=active]:shadow-[inset_0_3px_0_var(--accent)]';

export function BottomNav({ items }: { items: NavItem[] }) {
  const { t } = useTranslation();

  return (
    <nav
      aria-label={t('shell.mainNav')}
      className="fixed inset-x-0 bottom-0 z-30 flex border-t border-border bg-surface pb-[env(safe-area-inset-bottom)] lg:hidden"
    >
      {items.map((item) =>
        item.id === 'newTransfer' ? (
          <Link key={item.id} to={item.to} className={linkClass} activeOptions={{ exact: true }}>
            <span className="inline-flex size-10 items-center justify-center rounded-full bg-ink text-on-ink" aria-hidden="true">
              <Icon name="plus" />
            </span>
            <span>{t(item.labelKey)}</span>
          </Link>
        ) : (
          <Link key={item.id} to={item.to} className={linkClass} activeOptions={{ exact: item.id === 'transfers' }}>
            <Icon name={item.icon} />
            <span>{t(item.labelKey)}</span>
          </Link>
        ),
      )}
      <Link to="/more" className={linkClass}>
        <Icon name="menu" />
        <span>{t('nav.more')}</span>
      </Link>
    </nav>
  );
}
