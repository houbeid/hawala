import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { todayIso } from '@/shared/format/dates';
import { agentAccount, cashierAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { CashBudgetSnapshot, CashBudgetView, CoherenceAlert, CoherenceCheckView } from '../types';

// Budget de la caisse (maquette 10). Données fictives uniquement.

const budget: CashBudgetView = {
  agencyId: dkr.id,
  currency: 'XOF',
  cash: { amount: 4_400_000, items: [{ id: 'c1', name: 'Caisse principale', balance: 4_400_000 }] },
  banks: { amount: 33_700_000, items: [{ id: 'b1', name: 'Wave fictif', balance: 3_200_000 }, { id: 'b2', name: 'Banque fictive', balance: 30_500_000 }] },
  debtors: { amount: 4_750_000, items: [{ id: 'p1', name: 'Établissement Fictif', balance: 4_750_000 }] },
  creditors: { amount: 1_530_000, items: [] },
  undelivered: 1_150_000,
  budget: 40_170_000,
};

const alerts: CoherenceAlert[] = [
  { kind: 'UnusualRate', at: '2026-10-24T10:00:00Z', sourceId: 't1', label: 'TR-DKR-20261024-000004 : 3900 pour 3650 le jour même', amount: 3900, reason: null },
  { kind: 'NegativeBalance', at: '2026-10-24T12:00:00Z', sourceId: 'b1', label: 'Wave fictif', amount: -45_000, reason: null },
  { kind: 'CashMovementCancelled', at: '2026-10-24T11:30:00Z', sourceId: 'm1', label: 'Encaissement', amount: 300_000, reason: 'Saisi en double' },
];

function snapshot(date: string, savedAt: string): CashBudgetSnapshot {
  return {
    id: `s-${date}`,
    agencyId: dkr.id,
    date,
    currency: 'XOF',
    cash: 1,
    banks: 1,
    debtors: 0,
    creditors: 0,
    undelivered: 0,
    budget: 39_000_000,
    savedBy: 'u',
    savedAt,
    checkedSince: null,
    alerts: [],
  };
}

function budgetServer(options: { alerts?: CoherenceAlert[]; snapshots?: CashBudgetSnapshot[]; saveFails?: boolean } = {}) {
  const queried: { path: string; agencyId: string | null }[] = [];
  const saves: unknown[] = [];
  const check: CoherenceCheckView = { agencyId: dkr.id, since: '2026-10-23T18:02:00Z', alerts: options.alerts ?? alerts };
  const remember = (path: string) => ({ request }: { request: Request }) => {
    queried.push({ path, agencyId: new URL(request.url).searchParams.get('agencyId') });
  };
  server.use(
    http.get(apiUrl('/cash-budget'), (info) => {
      remember('live')(info);
      return HttpResponse.json(budget);
    }),
    http.get(apiUrl('/cash-budget/coherence'), (info) => {
      remember('coherence')(info);
      return HttpResponse.json(check);
    }),
    http.get(apiUrl('/cash-budget/snapshots'), () => HttpResponse.json(options.snapshots ?? [snapshot('2026-10-20', '2026-10-20T18:02:00Z')])),
    http.post(apiUrl('/cash-budget/snapshots'), async ({ request }) => {
      saves.push(await request.json());
      if (options.saveFails) return HttpResponse.error();
      return HttpResponse.json({ ...snapshot(todayIso(), `${todayIso()}T15:24:00Z`), budget: budget.budget, alerts: check.alerts });
    }),
  );
  return { queried, saves };
}

describe('Budget de la caisse — Caissier de Dakar', () => {
  beforeEach(() => signedInAs(cashierAccount));

  it('montre le budget, chaque ligne de la formule avec son détail, et l’historique', async () => {
    budgetServer();
    const { user } = renderApp('/budget');

    const total = await screen.findByRole('region', { name: 'Budget de la caisse (en direct)' });
    expect(within(total).getByText('40 170 000 FCFA')).toBeInTheDocument();
    expect(screen.getByText('Soldes créditeurs (on leur doit)').parentElement).toHaveTextContent('1 530 000 FCFA');
    expect(screen.getByText('Transferts non livrés').parentElement).toHaveTextContent('1 150 000 FCFA');

    await user.click(screen.getByText('Banques et wallets'));
    expect(screen.getByText('Banque fictive')).toBeVisible();

    const history = screen.getByRole('region', { name: 'Budgets enregistrés (30 derniers jours)' });
    expect(within(history).getByText('20/10')).toBeInTheDocument();
    expect(within(history).getByText('39 000 000 FCFA')).toBeInTheDocument();
  });

  it('trace la courbe des budgets enregistrés à partir de deux jours, résumée en texte', async () => {
    budgetServer({ snapshots: [snapshot('2026-10-20', '2026-10-20T18:02:00Z'), { ...snapshot('2026-10-18', '2026-10-18T18:00:00Z'), budget: 37_500_000 }] });
    renderApp('/budget');

    const history = await screen.findByRole('region', { name: 'Budgets enregistrés (30 derniers jours)' });
    const spaced = (text: string) => text.replace(/\s+/g, ' ');
    const summary = 'Courbe : Budget enregistré · FCFA, du 18/10 au 20/10, de 37 500 000 FCFA à 39 000 000 FCFA.';
    expect(await within(history).findByRole('img', { name: (name) => spaced(name) === summary })).toBeInTheDocument();
    expect(within(history).getByText('Budget enregistré · FCFA')).toBeInTheDocument();
  });

  it('liste les alertes de cohérence par famille, avec la référence ou le nom et le motif', async () => {
    budgetServer();
    renderApp('/budget');

    const panel = await screen.findByRole('region', { name: 'Contrôle de cohérence' });
    expect(within(panel).getByText(/depuis le dernier budget \(23\/10 · 18:02\)/)).toBeInTheDocument();
    expect(within(panel).getByText('Taux de change inhabituel (plus de 5 % d’écart)')).toBeInTheDocument();
    expect(within(panel).getByText('TR-DKR-20261024-000004')).toBeInTheDocument();
    expect(within(panel).getByText('5 000 FCFA = 3 900 MRO')).toBeInTheDocument();
    expect(within(panel).getByText('Comptes en négatif')).toBeInTheDocument();
    expect(within(panel).getByText('− 45 000 FCFA')).toBeInTheDocument();
    expect(within(panel).getByText('Motif : Saisi en double')).toBeInTheDocument();
  });

  it('enregistre le budget du jour après une confirmation qui rappelle les alertes', async () => {
    const { saves } = budgetServer();
    const { user } = renderApp('/budget');
    await user.click(await screen.findByRole('button', { name: 'Enregistrer le budget du jour' }));

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('3 à vérifier')).toBeInTheDocument();
    expect(saves).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer quand même' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(saves).toEqual([{ agencyId: dkr.id }]);
    expect(await screen.findByText(/Budget enregistré \(.* · 15:24\) : 40 170 000 FCFA\./)).toBeInTheDocument();
  });

  it('dit qu’un budget du jour existe déjà et qu’il sera remplacé ; sans alerte, rien à signaler', async () => {
    budgetServer({ alerts: [], snapshots: [snapshot(todayIso(), `${todayIso()}T09:00:00Z`)] });
    const { user } = renderApp('/budget');

    expect(await screen.findByText('Rien à signaler depuis le dernier budget enregistré.')).toBeInTheDocument();
    expect(screen.getByText(/Budget du jour déjà enregistré à .* · 09:00/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Enregistrer le budget du jour' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Le budget déjà enregistré aujourd’hui sera remplacé.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Je confirme' })).toBeInTheDocument();
  });

  it('garde la fenêtre ouverte et explique un échec d’enregistrement', async () => {
    budgetServer({ saveFails: true });
    const { user } = renderApp('/budget');
    await user.click(await screen.findByRole('button', { name: 'Enregistrer le budget du jour' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer quand même' }));
    expect(await within(dialog).findByRole('alert')).toBeInTheDocument();
  });
});

describe('Budget de la caisse — autres comptes', () => {
  it('un agent lit le budget sans pouvoir l’enregistrer', async () => {
    signedInAs(agentAccount);
    budgetServer();
    renderApp('/budget');
    expect(await screen.findByRole('region', { name: 'Budget de la caisse (en direct)' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Enregistrer le budget du jour' })).not.toBeInTheDocument();
  });

  it('le Superviseur sur « les deux agences » choisit la caisse', async () => {
    signedInAs(supervisorAccount);
    const { queried } = budgetServer();
    const { user } = renderApp('/budget');

    await screen.findByRole('region', { name: 'Budget de la caisse (en direct)' });
    expect(queried.find((q) => q.path === 'live')?.agencyId).toBe(nkc.id);
    await user.click(screen.getByRole('button', { name: 'DKR' }));
    await waitFor(() => expect(queried.filter((q) => q.path === 'live').at(-1)?.agencyId).toBe(dkr.id));
  });
});
