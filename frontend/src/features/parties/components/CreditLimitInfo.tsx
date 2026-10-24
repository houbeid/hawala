import { useTranslation } from 'react-i18next';
import type { Currency } from '@/features/organization/types';
import { formatAmount } from '@/shared/format/money';
import { creditUsage } from '../partyRules';

// Plafond de crédit d'une agence (maquette 08) : barre de consommation + pourcentage ÉCRIT + ce qui reste.
// Le contrôle réel est fait par le serveur à la création d'un transfert à crédit (solde après ≤ plafond).

export function CreditLimitInfo({ balance, creditLimit, currency }: { balance: number; creditLimit: number | null; currency: Currency }) {
  const { t } = useTranslation();
  const usage = creditUsage(balance, creditLimit);
  if (usage.kind === 'noLimit') return <p className="m-0 text-sm text-muted">{t('parties.credit.noLimit')}</p>;
  if (usage.kind === 'noCredit') return <p className="m-0 text-sm text-muted">{t('parties.credit.noCredit')}</p>;

  const over = usage.remaining < 0;
  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <span className="text-sm text-muted">{t('parties.credit.limit')}</span>
        <span className="amount">{formatAmount(usage.limit, currency)}</span>
      </div>
      <div
        role="progressbar"
        aria-label={t('parties.credit.used')}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={usage.percent}
        className="h-2 overflow-hidden rounded-full bg-surface-2"
      >
        <div className={`h-full rounded-full ${over ? 'bg-red' : 'bg-ink'}`} style={{ width: `${usage.percent}%` }} />
      </div>
      <p className={`m-0 text-sm ${over ? 'font-semibold text-red' : 'text-muted'}`}>
        {over
          ? t('parties.credit.over', { amount: formatAmount(-usage.remaining, currency) })
          : t('parties.credit.usage', { percent: usage.percent, amount: formatAmount(usage.remaining, currency) })}
      </p>
    </div>
  );
}
