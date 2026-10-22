import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useCurrentAccount, useSessionManager } from '@/shared/auth/hooks';
import { Icon } from '@/shared/ui/Icon';
import { PreferencesSheet } from '@/shared/ui/PreferencesSheet';
import { useTheme } from '@/shared/theme/useTheme';
import { GROUP_ORDER, moreMenuItems } from './navigation';

// « Plus » (maquette 02) : tout ce qui n'est pas dans la barre du bas, groupé (Pilotage, Administration,
// Mon compte), puis les préférences et la déconnexion. Sur ordinateur, la barre latérale montre déjà tout.

const rowClass =
  'flex min-h-14 w-full items-center gap-3 border-b border-border bg-surface px-3.5 text-start font-semibold text-fg no-underline last:border-b-0';

export function MorePage() {
  const { t, i18n } = useTranslation();
  const account = useCurrentAccount();
  const session = useSessionManager();
  const { preference } = useTheme();
  if (!account) return null;

  const items = moreMenuItems(account.role, account.permissions);

  return (
    <div className="flex flex-col gap-4">
      <div className="rounded-2xl border border-border bg-surface p-3.5">
        <div className="font-semibold">{account.fullName}</div>
        <div className="text-sm text-muted">
          {t(`roles.${account.role}`)} · {account.agency ? account.agency.name : t('shell.allAgencies')}
        </div>
      </div>

      {GROUP_ORDER.map((group) => {
        const inGroup = items.filter((item) => item.group === group);
        if (inGroup.length === 0) return null;
        return (
          <section key={group} className="flex flex-col gap-2">
            <h2 className="m-0 px-1 text-sm font-bold tracking-wide text-muted">{t(`nav.groups.${group}`)}</h2>
            <div className="overflow-hidden rounded-2xl border border-border">
              {inGroup.map((item) => (
                <Link key={item.id} to={item.to} className={rowClass}>
                  <Icon name={item.icon} />
                  <span className="flex-1">{t(item.labelKey)}</span>
                  <Icon name="chevron" className="text-muted" />
                </Link>
              ))}
            </div>
          </section>
        );
      })}

      <section className="flex flex-col gap-2">
        <h2 className="m-0 px-1 text-sm font-bold tracking-wide text-muted">{t('nav.groups.preferences')}</h2>
        <div className="overflow-hidden rounded-2xl border border-border">
          <PreferencesSheet
            trigger={
              <button type="button" className={rowClass}>
                <Icon name="globe" />
                <span className="flex-1">{t('shell.preferences')}</span>
                <span className="text-sm font-normal text-muted">
                  {t(`language.${i18n.language === 'ar' ? 'ar' : 'fr'}`)} · {t(`theme.${preference}`)}
                </span>
              </button>
            }
          />
          <button type="button" className={rowClass} onClick={() => void session.logout()}>
            <Icon name="signOut" />
            <span className="flex-1">{t('auth.signOut')}</span>
          </button>
        </div>
      </section>
    </div>
  );
}
