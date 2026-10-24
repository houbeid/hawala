import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { formatAmount } from '@/shared/format/money';
import { Icon } from '@/shared/ui/Icon';
import type { TreasuryAccount } from '../types';

// Carte d'un compte (maquette 14) : icône du type, nom, type et état en texte (jamais par la couleur seule),
// solde dans la devise de l'agence. Écart voulu avec la maquette : les banques et wallets de Nouakchott
// s'affichent en MRO, pas en MRU (règle validée du 19 octobre 2026 : le MRU ne sert qu'à la saisie).
// La carte ouvre la fiche du compte (relevé, et actions avec treasury.manage).

export function TreasuryAccountCard({ account }: { account: TreasuryAccount }) {
  const { t } = useTranslation();
  const content = (
    <>
      <span className={`inline-flex size-11 shrink-0 items-center justify-center rounded-xl bg-surface-2 ${account.isActive ? '' : 'text-muted'}`}>
        <Icon name={account.type === 'Cash' ? 'cash' : 'bank'} />
      </span>
      <span className="flex min-w-0 flex-1 flex-col gap-0.5 text-start">
        <span className={`truncate font-semibold ${account.isActive ? '' : 'text-muted'}`}>{account.name}</span>
        <span className="flex flex-wrap items-center gap-x-2 text-sm text-muted">
          <span>{t(`treasury.types.${account.type}`)}</span>
          <span aria-hidden="true">·</span>
          {account.isActive ? (
            <span className="inline-flex items-center gap-1">
              <Icon name="check" className="size-4" />
              {t('treasury.active')}
            </span>
          ) : (
            <span className="inline-flex items-center gap-1 font-semibold">
              <Icon name="ban" className="size-4" />
              {t('treasury.inactive')}
            </span>
          )}
        </span>
      </span>
      {account.balance !== null && (
        <span className="flex shrink-0 flex-col items-end gap-0.5">
          <span className="text-xs text-muted">{t('treasury.balance')}</span>
          <span className={`amount ${account.balance < 0 ? 'text-red' : ''}`}>{formatAmount(account.balance, account.currency)}</span>
        </span>
      )}
      <Icon name="chevron" className="size-5 text-muted" />
    </>
  );

  return (
    <Link
      to="/treasury/$accountId"
      params={{ accountId: account.id }}
      className="flex min-h-[72px] w-full items-center gap-3 rounded-2xl border border-border bg-surface p-3.5 text-fg no-underline"
    >
      {content}
    </Link>
  );
}
