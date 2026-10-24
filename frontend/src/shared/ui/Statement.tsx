import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { formatDayShort, isIsoDay } from '../format/dates';
import { formatAmount, type Currency } from '../format/money';

// Relevé d'un compte du grand livre (tiers, compte de trésorerie) : période, solde de début, une ligne par écriture
// avec le solde après, solde de fin. Composant partagé (décision du 22 octobre 2026) : il ne connaît aucune règle
// métier ; l'écran donne le titre de chaque ligne, la couleur d'un mouvement et la façon d'écrire un solde (« Il nous
// doit » pour un tiers, un simple montant pour une caisse).

export interface StatementRow {
  key: string;
  operationDate: string;
  title: string;
  /** Détail sous le titre (libellé du serveur, référence d'un transfert…). */
  subtitle?: string | null;
  reason: string | null;
  isReversal: boolean;
  /** Débit − crédit de la ligne (sens du grand livre). */
  delta: number;
  balanceAfter: number;
}

interface PeriodProps {
  from: string | undefined;
  to: string | undefined;
  onChange: (patch: { from?: string; to?: string }) => void;
}

/** Les deux dates de la période ; une date vidée revient à la valeur par défaut du serveur (le mois en cours). */
export function StatementPeriod({ from, to, onChange }: PeriodProps) {
  const { t } = useTranslation();
  const field = 'min-h-12 rounded-xl border-[1.5px] border-border bg-surface px-3 text-fg';
  return (
    <div className="flex flex-wrap items-end gap-3" role="group" aria-label={t('statement.period')}>
      <label className="flex flex-col gap-1">
        <span className="text-sm font-semibold text-muted">{t('statement.from')}</span>
        <input
          type="date"
          value={from ?? ''}
          max={to}
          onChange={(e) => onChange({ from: isIsoDay(e.target.value) ? e.target.value : undefined })}
          className={field}
        />
      </label>
      <label className="flex flex-col gap-1">
        <span className="text-sm font-semibold text-muted">{t('statement.to')}</span>
        <input
          type="date"
          value={to ?? ''}
          min={from}
          onChange={(e) => onChange({ to: isIsoDay(e.target.value) ? e.target.value : undefined })}
          className={field}
        />
      </label>
    </div>
  );
}

interface BodyProps {
  title: string;
  /** Explication du signe des mouvements. */
  legend: string;
  currency: Currency;
  from: string;
  to: string;
  openingBalance: number;
  closingBalance: number;
  rows: StatementRow[];
  /** Classe de couleur d'un mouvement selon son signe (débit − crédit). */
  deltaClass: (delta: number) => string;
  /** Écriture d'un solde (début, après une ligne, fin). */
  renderBalance: (balance: number, size: 'md' | 'lg') => ReactNode;
  /** Données précédentes gardées pendant le chargement d'une autre période. */
  stale?: boolean;
}

export function StatementBody({ title, legend, currency, from, to, openingBalance, closingBalance, rows, deltaClass, renderBalance, stale = false }: BodyProps) {
  const { t } = useTranslation();
  return (
    <section className={`flex flex-col gap-3 ${stale ? 'opacity-60' : ''}`} aria-label={t('statement.title')}>
      <h2 className="m-0 text-lg font-bold">{title}</h2>
      <p className="m-0 text-sm text-muted">{legend}</p>
      <div className="flex flex-wrap items-center justify-between gap-2 rounded-2xl bg-surface-2 p-3.5">
        <span className="text-sm font-semibold">{t('statement.opening', { date: formatDayShort(from) })}</span>
        {renderBalance(openingBalance, 'md')}
      </div>
      {rows.length === 0 ? (
        <p className="m-0 text-center text-muted">{t('statement.empty')}</p>
      ) : (
        <ul className="m-0 list-none rounded-2xl border border-border bg-surface px-3.5 py-0">
          {rows.map((row) => (
            <li key={row.key} className="flex flex-col gap-1 border-b border-border py-3 last:border-b-0">
              <div className="flex items-start justify-between gap-3">
                <span className="flex min-w-0 flex-col gap-0.5">
                  <span className="text-sm tabular-nums text-muted">{formatDayShort(row.operationDate)}</span>
                  <span className="break-words font-semibold">{row.title}</span>
                  {row.subtitle && <span className="break-words text-sm text-muted">{row.subtitle}</span>}
                  {row.isReversal && <span className="text-sm font-semibold">{t('statement.reversal')}</span>}
                  {row.reason && <span className="text-sm text-muted">{row.reason}</span>}
                </span>
                <span className={`amount shrink-0 ${deltaClass(row.delta)}`}>{formatAmount(row.delta, currency, { signed: true })}</span>
              </div>
              <div className="flex flex-wrap items-center justify-end gap-2 text-sm text-muted">
                <span>{t('statement.after')}</span>
                {renderBalance(row.balanceAfter, 'md')}
              </div>
            </li>
          ))}
        </ul>
      )}
      <div className="flex flex-wrap items-center justify-between gap-2 rounded-2xl bg-surface-2 p-3.5">
        <span className="text-sm font-semibold">{t('statement.closing', { date: formatDayShort(to) })}</span>
        {renderBalance(closingBalance, 'lg')}
      </div>
    </section>
  );
}
