import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { formatDayShort, formatDayTimeShort } from '@/shared/format/dates';
import { formatAmount, formatRate } from '@/shared/format/money';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { costAtPurchaseRate } from '../profitRules';
import { useProfitDay } from '../queries';
import type { ProfitDay, TransferProfitLine } from '../types';

// Un jour du bénéfice (maquettes 11 et D4) : date, nombre de transferts, montant envoyé, prix d'achat du jour,
// bénéfice signé (le mot « perte » s'il est négatif : jamais par la seule couleur), et le bénéfice enregistré.
// La ligne se déplie sur ses transferts (lus à la demande) où la formule se lit : FCFA, prix de vente, montant MRO,
// coût au prix d'achat, bénéfice. Pleine précision au serveur ; l'affichage arrondit au MRO.

/** Bénéfice affiché : MRO arrondi à l'unité, signé ; « perte » écrit en toutes lettres. */
export function ProfitValue({ value, size = 'md' }: { value: number; size?: 'md' | 'lg' | 'xl' }) {
  const { t } = useTranslation();
  const sizes = { md: '', lg: 'text-xl', xl: 'text-[34px] leading-tight' };
  return (
    <span className="inline-flex flex-wrap items-baseline justify-end gap-x-2">
      <span className={`amount ${sizes[size]}`}>{formatAmount(value, 'MRO', { signed: true, maxDecimals: 0 })}</span>
      {value < 0 && <span className="text-sm font-bold">{t('profit.loss')}</span>}
    </span>
  );
}

function TransferLine({ line, agencyCodes }: { line: TransferProfitLine; agencyCodes: Map<string, string> }) {
  const { t } = useTranslation();
  const route = `${agencyCodes.get(line.sourceAgencyId) ?? '?'} → ${agencyCodes.get(line.destinationAgencyId) ?? '?'}`;
  return (
    <li className="flex flex-col gap-1 border-b border-border py-2.5 last:border-b-0">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <span className="font-mono text-xs font-semibold" dir="ltr">
          {line.reference}
        </span>
        <span className="text-sm text-muted">{route}</span>
      </div>
      <dl className="m-0 grid grid-cols-[1fr_auto] gap-x-3 gap-y-0.5 text-sm">
        <dt className="text-muted">{t('profit.detail.amountXof')}</dt>
        <dd className="amount m-0 text-end">{formatAmount(line.amountXof, 'XOF')}</dd>
        <dt className="text-muted">{t('profit.detail.saleRate')}</dt>
        <dd className="m-0 text-end tabular-nums">{formatRate(line.saleRate)}</dd>
        <dt className="text-muted">{t('profit.detail.amountMro')}</dt>
        <dd className="amount m-0 text-end">{formatAmount(line.amountMro, 'MRO')}</dd>
        <dt className="text-muted">{t('profit.detail.cost', { rate: formatRate(line.purchaseRate) })}</dt>
        <dd className="amount m-0 text-end">{formatAmount(costAtPurchaseRate(line.amountXof, line.purchaseRate), 'MRO', { maxDecimals: 0 })}</dd>
        <dt className="font-semibold">{t('profit.detail.profit')}</dt>
        <dd className="m-0 text-end">
          <ProfitValue value={line.profitMro} />
        </dd>
      </dl>
    </li>
  );
}

interface Props {
  day: ProfitDay;
  agencyId: string | undefined;
  agencyCodes: Map<string, string>;
  isToday: boolean;
}

export function ProfitDayRow({ day, agencyId, agencyCodes, isToday }: Props) {
  const { t, i18n } = useTranslation();
  const [open, setOpen] = useState(false);
  const panelId = useId();
  const detail = useProfitDay(day.date, agencyId, open);
  return (
    <li className="rounded-2xl border border-border bg-surface">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((value) => !value)}
        className="flex min-h-16 w-full items-center gap-3 px-3.5 py-2.5 text-start"
      >
        <span className="flex min-w-0 flex-1 flex-col gap-0.5">
          <span className="font-semibold">
            {formatDayShort(day.date)}
            {isToday && ` · ${t('profit.today')}`}
            {' · '}
            {t('profit.transfers', { count: day.transferCount })}
          </span>
          <span className="text-sm text-muted">
            {formatAmount(day.amountXof, 'XOF')}
            {' · '}
            {day.purchaseRate === null ? t('profit.severalRates') : t('profit.purchaseRate', { rate: formatRate(day.purchaseRate) })}
          </span>
          {day.savedProfitMro !== null && day.savedAt && (
            <span className="text-sm text-muted">
              {t('profit.saved', { amount: formatAmount(day.savedProfitMro, 'MRO', { signed: true, maxDecimals: 0 }), time: formatDayTimeShort(day.savedAt) })}
            </span>
          )}
        </span>
        <ProfitValue value={day.profitMro} />
        <Icon name="down" className={`size-5 text-muted transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>
      {open && (
        <div id={panelId} className="border-t border-border px-3.5 pb-2">
          {detail.isPending && (
            <p role="status" className="m-0 py-2 text-sm text-muted">
              {t('common.loading')}
            </p>
          )}
          {detail.isError && (
            <div className="py-2">
              <Notice tone="error">{errorMessage(i18n, detail.error)}</Notice>
            </div>
          )}
          {detail.data && (
            <ul className="m-0 list-none p-0" aria-label={t('profit.detail.title', { date: formatDayShort(day.date) })}>
              {detail.data.transfers.map((line) => (
                <TransferLine key={line.transferId} line={line} agencyCodes={agencyCodes} />
              ))}
            </ul>
          )}
        </div>
      )}
    </li>
  );
}
