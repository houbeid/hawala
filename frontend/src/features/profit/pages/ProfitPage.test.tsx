import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { addDays, todayIso } from '@/shared/format/dates';
import { agentAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { ProfitDayDetail, ProfitReport } from '../types';

// Écran « Bénéfice » (maquettes 11 et D4). Données fictives uniquement.

const today = todayIso();
const yesterday = addDays(today, -1);

const report: ProfitReport = {
  from: yesterday,
  to: today,
  agencyId: null,
  totalProfitMro: 251_650.4,
  transferCount: 11,
  days: [
    { date: yesterday, transferCount: 5, amountXof: 9_000_000, purchaseRate: 3600, profitMro: 118_250, savedProfitMro: 118_250, savedAt: `${yesterday}T18:02:00Z` },
    { date: today, transferCount: 6, amountXof: 10_000_000, purchaseRate: 3600, profitMro: 133_400.4, savedProfitMro: null, savedAt: null },
  ],
};

const detail: ProfitDayDetail = {
  date: today,
  agencyId: null,
  totalProfitMro: 133_400.4,
  transfers: [
    {
      transferId: 't1',
      reference: 'TR-NKC-20261024-000052',
      sourceAgencyId: nkc.id,
      destinationAgencyId: dkr.id,
      operationAt: `${today}T10:00:00Z`,
      amountXof: 5_000_000,
      amountMro: 3_650_000,
      saleRate: 3650,
      purchaseRate: 3600,
      profitMro: 50_000,
    },
    {
      transferId: 't2',
      reference: 'TR-DKR-20261024-000019',
      sourceAgencyId: dkr.id,
      destinationAgencyId: nkc.id,
      operationAt: `${today}T11:00:00Z`,
      amountXof: 1_000_000,
      amountMro: 724_000,
      saleRate: 3620,
      purchaseRate: 3600,
      profitMro: -4_000,
    },
  ],
};

function profitServer(body: ProfitReport = report) {
  const queries: URLSearchParams[] = [];
  const saves: unknown[] = [];
  server.use(
    http.get(apiUrl('/profit'), ({ request }) => {
      queries.push(new URL(request.url).searchParams);
      return HttpResponse.json(body);
    }),
    http.get(apiUrl('/profit/days/:date'), () => HttpResponse.json(detail)),
    http.post(apiUrl('/profit/snapshots'), async ({ request }) => {
      saves.push(await request.json());
      return HttpResponse.json({
        id: 's',
        agencyId: null,
        date: today,
        transferCount: 6,
        amountXof: 10_000_000,
        purchaseRate: 3600,
        profitMro: 133_400.4,
        savedBy: 'u',
        savedAt: `${today}T15:24:00Z`,
      });
    }),
  );
  return { queries, saves };
}

describe('Bénéfice — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('montre le total de la période, arrondi à l’affichage, et une ligne par jour du plus récent au plus ancien', async () => {
    const { queries } = profitServer();
    renderApp('/profit');

    const total = await screen.findByRole('region', { name: 'Bénéfice de la période · 2 jours' });
    expect(within(total).getByText('+ 251 650 MRO')).toBeInTheDocument();
    expect(within(total).getByText('11 transferts, livrés ou non, hors annulés')).toBeInTheDocument();
    const days = within(screen.getByRole('region', { name: 'Par jour' })).getAllByRole('button');
    expect(days[0]).toHaveTextContent(/aujourd’hui · 6 transferts/);
    expect(days[1]).toHaveTextContent(/Enregistré : \+ 118 250 MRO/);
    expect(queries[0]?.has('agencyId')).toBe(false);
    expect(queries[0]?.has('from')).toBe(false);
  });

  it('déplie un jour sur ses transferts : formule lue ligne à ligne, perte écrite en toutes lettres', async () => {
    profitServer();
    const { user } = renderApp('/profit');
    const byDay = await screen.findByRole('region', { name: 'Par jour' });
    const day = within(byDay).getAllByRole('button', { expanded: false })[0]!;
    await user.click(day);

    const list = await screen.findByRole('list', { name: /^Transferts du / });
    expect(within(list).getByText('TR-NKC-20261024-000052')).toBeInTheDocument();
    expect(within(list).getByText('NKC → DKR')).toBeInTheDocument();
    expect(within(list).getByText('3 600 000 MRO')).toBeInTheDocument();
    expect(within(list).getByText('+ 50 000 MRO')).toBeInTheDocument();
    expect(within(list).getByText('− 4 000 MRO')).toBeInTheDocument();
    expect(within(list).getByText('perte')).toBeInTheDocument();
  });

  it('envoie la période choisie et la garde dans l’adresse', async () => {
    const { queries } = profitServer();
    const { user, router } = renderApp('/profit');
    await screen.findByRole('region', { name: /Bénéfice de la période/ });

    await user.click(screen.getByRole('button', { name: '7 derniers jours' }));
    await waitFor(() => expect(queries.at(-1)?.get('from')).toBe(addDays(today, -6)));
    expect(router.state.location.search).toEqual({ from: addDays(today, -6) });
    expect(screen.getByRole('button', { name: '7 derniers jours' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('enregistre le bénéfice du jour après confirmation (les deux agences)', async () => {
    const { saves } = profitServer();
    const { user } = renderApp('/profit');
    await user.click(await screen.findByRole('button', { name: 'Enregistrer le bénéfice du jour' }));

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Transferts comptés').nextElementSibling).toHaveTextContent('6');
    await user.click(within(dialog).getByRole('button', { name: 'Je confirme' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(saves).toEqual([{ agencyId: null }]);
    expect(await screen.findByText(/Bénéfice du jour enregistré \(.* · 15:24\) : \+ 133 400 MRO\./)).toBeInTheDocument();
  });

  it('dit qu’il n’y a aucun transfert sur la période', async () => {
    profitServer({ ...report, totalProfitMro: 0, transferCount: 0, days: [] });
    renderApp('/profit');
    expect(await screen.findByText('Aucun transfert sur cette période.')).toBeInTheDocument();
  });
});

describe('Bénéfice — sans profit.view', () => {
  it('l’écran est refusé', async () => {
    signedInAs(agentAccount);
    renderApp('/profit');
    expect(await screen.findByText("Votre compte n'a pas accès à cet écran.")).toBeInTheDocument();
  });
});
