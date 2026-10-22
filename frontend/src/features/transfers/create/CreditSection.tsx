import { useTranslation } from 'react-i18next';
import type { Currency } from '@/features/organization/types';
import { PartyPicker, type PartySummary } from '@/features/parties';
import { formatAmount } from '@/shared/format/money';
import { Notice } from '@/shared/ui/Notice';

// Crédit d'un transfert (maquette 04, règles validées du 20 octobre 2026) : la part non payée va sur un tiers
// choisi dans la liste (ou créé), en rouge dans l'agence d'envoi. Le plafond du tiers bloque tout le monde,
// Superviseur compris : l'écran prévient (solde après le transfert > plafond) mais c'est le serveur qui refuse.

interface Props {
  enabled: boolean;
  onToggle: (enabled: boolean) => void;
  party: PartySummary | null;
  onParty: (party: PartySummary | null) => void;
  sourceAgencyId: string;
  currency: Currency;
  /** Part à crédit = montant à payer − total réparti. */
  creditPart: number;
  error?: string;
}

function balanceText(t: (key: string, options?: Record<string, unknown>) => string, balance: number, currency: Currency): string {
  if (balance > 0) return t('transfers.create.credit.owesUs', { amount: formatAmount(balance, currency) });
  if (balance < 0) return t('transfers.create.credit.weOwe', { amount: formatAmount(-balance, currency) });
  return t('transfers.create.credit.settled');
}

export function CreditSection({ enabled, onToggle, party, onParty, sourceAgencyId, currency, creditPart, error }: Props) {
  const { t } = useTranslation();
  const account = party?.balances?.find((b) => b.agencyId === sourceAgencyId) ?? null;
  const after = account ? account.balance + creditPart : null;
  const overLimit = account !== null && account.creditLimit !== null && after !== null && creditPart > 0 && after > account.creditLimit;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex min-h-14 items-center gap-3 rounded-2xl border border-border bg-surface px-3.5">
        <span id="credit-label" className="flex-1 font-semibold">
          {t('transfers.create.credit.toggle')}
        </span>
        <button
          type="button"
          role="switch"
          aria-checked={enabled}
          aria-labelledby="credit-label"
          onClick={() => onToggle(!enabled)}
          className="relative inline-flex h-8 w-14 shrink-0 items-center rounded-full border-2 border-ink bg-surface aria-checked:bg-ink"
        >
          <span
            aria-hidden="true"
            className={`inline-block size-5 rounded-full transition-transform ${enabled ? 'translate-x-7 bg-on-ink rtl:-translate-x-7' : 'translate-x-1 bg-ink rtl:-translate-x-1'}`}
          />
        </button>
        <span className="text-sm font-semibold text-muted" aria-hidden="true">
          {enabled ? t('transfers.create.credit.on') : t('transfers.create.credit.off')}
        </span>
      </div>

      {enabled && (
        <>
          <PartyPicker label={t('transfers.create.credit.party')} selected={party} onSelect={onParty} error={error} />
          {party && creditPart > 0 && (
            <Notice tone="info">
              {t('transfers.create.credit.part', { amount: formatAmount(creditPart, currency), name: party.name })}
              {account && (
                <>
                  {' '}
                  {t('transfers.create.credit.current', { balance: balanceText(t, account.balance, currency) })}{' '}
                  {account.creditLimit === null
                    ? t('transfers.create.credit.noLimit')
                    : t('transfers.create.credit.limit', { limit: formatAmount(account.creditLimit, currency) })}
                </>
              )}
            </Notice>
          )}
          {overLimit && account?.creditLimit !== null && account && (
            <Notice tone="warning">{t('transfers.create.credit.overLimit', { limit: formatAmount(account.creditLimit ?? 0, currency) })}</Notice>
          )}
        </>
      )}
    </div>
  );
}
