import { useQueries, useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { useSessionManager } from '@/shared/auth/hooks';
import { todayIso } from '@/shared/format/dates';
import { formatAmount } from '@/shared/format/money';
import { Icon } from '@/shared/ui/Icon';
import {
  balanceSavedOn,
  budgetSavedOn,
  capitalByAgency,
  countActiveAccounts,
  countParties,
  countTreasuryAccounts,
  currentBudget,
  getStartDate,
  hasRate,
} from '../api';
import { startDateKey } from '../queries';

// Liste de contrôle de la mise en service (tour 4, phase 8) : ce qui doit être fait avant d'ouvrir l'app aux agents, lu
// en direct sur les autres écrans (rien n'est gardé ici). Chaque point dit « fait » ou « à faire » en mots, avec son lien.
// Le budget actuel de chaque caisse est montré pour le comparer à celui de l'ancienne app au même moment.

type Screen = '/setup' | '/parties' | '/rates' | '/users' | '/treasury' | '/balance' | '/budget';

interface Item {
  key: string;
  done: boolean | undefined;
  label: string;
  detail?: string;
  to: Screen;
}

export function SetupChecklist() {
  const { t } = useTranslation();
  const session = useSessionManager();
  const agencies = useAgencies().data ?? [];
  const today = todayIso();
  const http = session.http;
  const queryKey = (name: string, ...rest: unknown[]) => ['setup', 'checklist', name, ...rest];

  const startDate = useQuery({ queryKey: startDateKey, queryFn: () => getStartDate(http) });
  const parties = useQuery({ queryKey: queryKey('parties'), queryFn: () => countParties(http) });
  const rate = useQuery({ queryKey: queryKey('rate', today), queryFn: () => hasRate(http, today) });
  const capital = useQuery({ queryKey: queryKey('capital'), queryFn: () => capitalByAgency(http) });
  const balances = useQuery({ queryKey: queryKey('balance', today), queryFn: () => balanceSavedOn(http, today) });
  const perAgency = useQueries({
    queries: agencies.flatMap((agency) => [
      { queryKey: queryKey('users', agency.id), queryFn: () => countActiveAccounts(http, agency.id) },
      { queryKey: queryKey('treasury', agency.id), queryFn: () => countTreasuryAccounts(http, agency.id) },
      { queryKey: queryKey('budget', agency.id), queryFn: () => currentBudget(http, agency.id) },
      { queryKey: queryKey('budgetSaved', agency.id, today), queryFn: () => budgetSavedOn(http, agency.id, today) },
    ]),
  });

  const city = (code: string) => t(`agencies.${code}`, { defaultValue: code });
  const items: Item[] = [
    { key: 'startDate', done: startDate.data ? startDate.data.startDate !== null : undefined, label: t('setup.checklist.startDate'), to: '/setup' },
    {
      key: 'parties',
      done: parties.data === undefined ? undefined : parties.data > 0,
      label: t('setup.checklist.parties'),
      detail: parties.data === undefined ? undefined : t('setup.checklist.count', { count: parties.data }),
      to: '/parties',
    },
    { key: 'rate', done: rate.data, label: t('setup.checklist.rate'), to: '/rates' },
  ];
  agencies.forEach((agency, index) => {
    const [users, treasury, budget, budgetSaved] = perAgency.slice(index * 4, index * 4 + 4);
    const name = city(agency.code);
    items.push(
      {
        key: `users-${agency.id}`,
        done: users?.data === undefined ? undefined : (users.data as number) > 0,
        label: t('setup.checklist.users', { agency: name }),
        detail: users?.data === undefined ? undefined : t('setup.checklist.count', { count: users.data as number }),
        to: '/users',
      },
      {
        key: `treasury-${agency.id}`,
        done: treasury?.data === undefined ? undefined : (treasury.data as number) > 0,
        label: t('setup.checklist.treasury', { agency: name }),
        detail: treasury?.data === undefined ? undefined : t('setup.checklist.count', { count: treasury.data as number }),
        to: '/treasury',
      },
      {
        key: `capital-${agency.id}`,
        done: capital.data ? capital.data[agency.id] != null : undefined,
        label: t('setup.checklist.capital', { agency: name }),
        to: '/balance',
      },
      {
        key: `budget-${agency.id}`,
        done: budgetSaved?.data as boolean | undefined,
        label: t('setup.checklist.budget', { agency: name }),
        detail: budget?.data === undefined ? undefined : t('setup.checklist.budgetNow', { amount: formatAmount(budget.data as number, agency.currency) }),
        to: '/budget',
      },
      {
        key: `balance-${agency.id}`,
        done: balances.data ? balances.data.includes(agency.id) : undefined,
        label: t('setup.checklist.balance', { agency: name }),
        to: '/balance',
      },
    );
  });

  return (
    <section className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4" aria-label={t('setup.checklist.title')}>
      <h2 className="m-0 text-lg font-bold">{t('setup.checklist.title')}</h2>
      <p className="m-0 text-muted">{t('setup.checklist.hint')}</p>
      <ul className="m-0 flex list-none flex-col gap-0 p-0" aria-label={t('setup.checklist.title')}>
        {items.map((item) => (
          <li key={item.key} className="flex items-start gap-3 border-b border-border py-2.5 last:border-b-0">
            <Icon name={item.done ? 'check' : item.done === false ? 'clock' : 'sync'} className="mt-0.5 size-5 shrink-0" />
            <span className="flex min-w-0 flex-1 flex-col">
              <span className="font-semibold">{item.label}</span>
              {item.detail && <span className="text-sm text-muted">{item.detail}</span>}
            </span>
            <span className="text-sm font-semibold">
              {item.done === undefined ? t('common.loading') : item.done ? t('setup.checklist.done') : t('setup.checklist.todo')}
            </span>
            {item.done === false && item.to !== '/setup' && (
              <Link to={item.to} className="text-sm font-semibold text-fg underline">
                {t('setup.checklist.open')}
              </Link>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}
