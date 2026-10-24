import { useTranslation } from 'react-i18next';
import { formatAmount } from '@/shared/format/money';
import type { CashBudgetLine, CashBudgetView } from '../types';

// La formule du budget, ligne par ligne (maquette 10) : un signe + ou − dans un rond, le montant, et chaque ligne
// s'ouvre sur son détail (comptes, tiers). Les soldes des tiers gardent leurs mots (« ils nous doivent »,
// « on leur doit ») ; tout est dans la devise de l'agence, lu au grand livre.

function Line({ sign, title, line, currency }: { sign: '+' | '−'; title: string; line: CashBudgetLine; currency: CashBudgetView['currency'] }) {
  const { t } = useTranslation();
  const summary = (
    <span className="flex flex-1 items-center gap-3">
      <span className="inline-flex size-7 shrink-0 items-center justify-center rounded-full border-2 border-ink font-bold" aria-hidden="true">
        {sign}
      </span>
      <span className="flex-1 font-semibold">{title}</span>
      <span className="amount">{formatAmount(line.amount, currency)}</span>
    </span>
  );
  if (line.items.length === 0) {
    return <div className="flex min-h-14 items-center rounded-2xl border border-border bg-surface px-3.5">{summary}</div>;
  }
  return (
    <details className="group rounded-2xl border border-border bg-surface">
      <summary className="flex min-h-14 cursor-pointer list-none items-center gap-2 px-3.5">
        {summary}
        <span className="sr-only">{t('budget.showDetail')}</span>
      </summary>
      <ul className="m-0 flex list-none flex-col gap-1 border-t border-border bg-bg px-3.5 py-2 ps-14">
        {line.items.map((item) => (
          <li key={item.id} className="flex items-center justify-between gap-3 text-sm">
            <span className="min-w-0 truncate">{item.name}</span>
            <span className="amount shrink-0">{formatAmount(item.balance, currency)}</span>
          </li>
        ))}
      </ul>
    </details>
  );
}

export function BudgetLines({ budget }: { budget: CashBudgetView }) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-2">
      <Line sign="+" title={t('budget.lines.cash')} line={budget.cash} currency={budget.currency} />
      <Line sign="+" title={t('budget.lines.banks')} line={budget.banks} currency={budget.currency} />
      <Line sign="+" title={t('budget.lines.debtors')} line={budget.debtors} currency={budget.currency} />
      <Line sign="−" title={t('budget.lines.creditors')} line={budget.creditors} currency={budget.currency} />
      <Line sign="−" title={t('budget.lines.undelivered')} line={{ amount: budget.undelivered, items: [] }} currency={budget.currency} />
    </div>
  );
}
