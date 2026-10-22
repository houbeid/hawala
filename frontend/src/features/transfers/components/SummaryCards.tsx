import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { Agency } from '@/features/organization/types';
import { formatAmount, formatRate } from '@/shared/format/money';
import type { SummaryAmount, TransferSummary } from '../types';

// Cartes de l'écran des transferts (maquette 03, règles du 21 octobre 2026), pour une agence A face à B,
// chacune dans sa devise, sans total converti :
// - Superviseur (profit.view) : entrées A, sorties A, envoyé par B, livré par B, non livrés, bénéfice, taux ;
// - autres comptes : total acheté, total vendu, non livrés, prix de vente (le serveur ne renvoie rien d'autre).

function Card({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col gap-1 rounded-2xl border border-border bg-surface p-3">
      <span className="text-sm text-muted">{label}</span>
      {children}
    </div>
  );
}

function AmountCard({ label, value, currency }: { label: string; value: SummaryAmount; currency: 'MRO' | 'XOF' }) {
  const { t } = useTranslation();
  return (
    <Card label={label}>
      <span className="amount text-xl leading-tight">{formatAmount(value.amount, currency, { maxDecimals: 0 })}</span>
      <span className="text-xs text-muted">{t('transfers.cards.count', { count: value.count })}</span>
    </Card>
  );
}

export function SummaryCards({ summary, agencies }: { summary: TransferSummary; agencies: Map<string, Agency> }) {
  const { t } = useTranslation();
  const a = agencies.get(summary.agencyId)?.code ?? '';
  const b = agencies.get(summary.otherAgencyId)?.code ?? '';
  const full = summary.sentByOther !== null && summary.deliveredByOther !== null;

  return (
    <section aria-label={t('transfers.cards.label')} className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
      <AmountCard
        label={full ? t('transfers.cards.incoming', { agency: a }) : t('transfers.cards.bought')}
        value={summary.incoming}
        currency={summary.currency}
      />
      <AmountCard
        label={full ? t('transfers.cards.outgoing', { agency: a }) : t('transfers.cards.sold')}
        value={summary.outgoing}
        currency={summary.currency}
      />
      {summary.sentByOther && (
        <AmountCard label={t('transfers.cards.sentByOther', { agency: b })} value={summary.sentByOther} currency={summary.otherCurrency} />
      )}
      {summary.deliveredByOther && (
        <AmountCard label={t('transfers.cards.deliveredByOther', { agency: b })} value={summary.deliveredByOther} currency={summary.otherCurrency} />
      )}
      <AmountCard label={t('transfers.cards.notDelivered')} value={summary.notDelivered} currency={summary.currency} />
      {summary.profitMro !== null && (
        <Card label={t('transfers.cards.profit')}>
          {/* Pleine précision côté serveur, seul le total affiché est arrondi (règle d'arrondi 4). */}
          <span className="amount text-xl leading-tight">{formatAmount(summary.profitMro, 'MRO', { signed: true, maxDecimals: 0 })}</span>
        </Card>
      )}
      <Card label={full ? t('transfers.cards.rates') : t('transfers.cards.saleRate')}>
        {summary.saleRate === null ? (
          <span className="text-sm font-semibold text-warn-fg">{t('transfers.cards.rateNotSet')}</span>
        ) : (
          <span className="amount text-sm whitespace-normal">
            {full && `${t('rates.saleShort')} · `}
            {formatRate(summary.saleRate)}
          </span>
        )}
        {summary.purchaseRate !== null && (
          <span className="amount text-sm whitespace-normal text-muted">
            {t('rates.purchaseShort')} · {formatRate(summary.purchaseRate)}
          </span>
        )}
      </Card>
    </section>
  );
}
