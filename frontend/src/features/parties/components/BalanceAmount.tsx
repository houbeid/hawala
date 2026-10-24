import { useTranslation } from 'react-i18next';
import type { Currency } from '@/features/organization/types';
import { formatAmount } from '@/shared/format/money';
import { balanceState } from '../partyRules';

// Solde d'un tiers (maquette 08) : le sens est TOUJOURS écrit (« Il nous doit » / « On lui doit » / « Soldé »),
// la couleur ne fait que le doubler. Écart avec la maquette, qui se contredit sur le signe (− sur téléphone,
// + sur ordinateur pour « Il nous doit ») : montant sans signe, comme l'étape du crédit d'un transfert
// (« il nous doit 1 200 000 MRO ») ; c'est le mot qui donne le sens.

const LOOKS = {
  red: { amount: 'text-red', chip: 'bg-red-bg text-red border-red' },
  green: { amount: 'text-green', chip: 'bg-green-bg text-green border-green' },
  zero: { amount: '', chip: 'bg-surface-2 text-muted border-border' },
} as const;

interface Props {
  balance: number;
  currency: Currency;
  size?: 'md' | 'lg';
  /** Agence du solde, écrite devant quand plusieurs agences sont affichées (« NKC »). */
  agencyCode?: string;
}

export function BalanceAmount({ balance, currency, size = 'md', agencyCode }: Props) {
  const { t } = useTranslation();
  const state = balanceState(balance);
  const look = LOOKS[state];
  return (
    <span className="inline-flex flex-wrap items-center justify-end gap-x-2 gap-y-1">
      {agencyCode && <span className="text-xs font-bold tracking-wide text-muted">{agencyCode}</span>}
      <span className={`amount ${size === 'lg' ? 'text-2xl' : ''} ${look.amount}`}>{formatAmount(Math.abs(balance), currency)}</span>
      <span className={`inline-flex h-6 items-center whitespace-nowrap rounded-full border-[1.5px] px-2 text-xs font-bold ${look.chip}`}>
        {t(`parties.balance.${state}`)}
      </span>
    </span>
  );
}
