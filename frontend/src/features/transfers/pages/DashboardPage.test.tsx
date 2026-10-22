import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { addDays, todayIso } from '@/shared/format/dates';
import { agentAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { TransferDashboard } from '../types';

const today = todayIso();

const dashboard: TransferDashboard = {
  from: today,
  to: today,
  caisses: [
    {
      agencyId: nkc.id,
      currency: 'MRO',
      sent: { amount: 9880000, count: 24 },
      delivered: { amount: 7900000, count: 19 },
      notDelivered: { amount: 1980000, count: 5 },
      deliveryRatePercent: 79.16666,
    },
    {
      agencyId: dkr.id,
      currency: 'XOF',
      sent: { amount: 0, count: 0 },
      delivered: { amount: 0, count: 0 },
      notDelivered: { amount: 0, count: 0 },
      deliveryRatePercent: null,
    },
  ],
};

/** Enregistre les périodes demandées, et toute lecture du bilan (qui ne doit jamais partir d'ici). */
function dashboardServer() {
  const periods: { from: string | null; to: string | null }[] = [];
  let balanceCalls = 0;
  server.use(
    http.get(apiUrl('/transfers/dashboard'), ({ request }) => {
      const url = new URL(request.url);
      periods.push({ from: url.searchParams.get('from'), to: url.searchParams.get('to') });
      return HttpResponse.json(dashboard);
    }),
    http.get(apiUrl('/balance'), () => {
      balanceCalls += 1;
      return HttpResponse.json({});
    }),
  );
  return { periods, balanceCalls: () => balanceCalls };
}

describe('Tableau de bord', () => {
  it("est la page d'ouverture du Superviseur, sans calculer le bilan", async () => {
    signedInAs(supervisorAccount);
    const calls = dashboardServer();
    const { router } = renderApp('/');

    expect(await screen.findByRole('heading', { level: 1, name: 'Tableau de bord' })).toBeInTheDocument();
    await waitFor(() => expect(router.state.location.pathname).toBe('/dashboard'));
    await screen.findByRole('region', { name: 'Caisse de Nouakchott' });
    expect(calls.balanceCalls()).toBe(0);
    expect(screen.getByRole('link', { name: 'Voir le bilan par caisse' })).toHaveAttribute('href', '/balance');
  });

  it('montre chaque caisse dans sa devise : envoyé, livrés, non livrés, taux de livraison', async () => {
    signedInAs(supervisorAccount);
    dashboardServer();
    renderApp('/dashboard');

    const nouakchott = await screen.findByRole('region', { name: 'Caisse de Nouakchott' });
    expect(within(nouakchott).getByText('Envoyés par Nouakchott')).toBeInTheDocument();
    expect(within(nouakchott).getByText('9 880 000 MRO')).toBeInTheDocument();
    expect(within(nouakchott).getByText('24')).toBeInTheDocument();
    expect(within(nouakchott).getByText('7 900 000 MRO')).toBeInTheDocument();
    expect(within(nouakchott).getByText('1 980 000 MRO')).toBeInTheDocument();
    expect(within(nouakchott).getByText('79,2 %')).toBeInTheDocument();

    const dakar = screen.getByRole('region', { name: 'Caisse de Dakar' });
    // Envoyé, livrés et non livrés : trois fois zéro, dans la devise de Dakar.
    expect(within(dakar).getAllByText('0 FCFA')).toHaveLength(3);
    expect(within(dakar).getByText('Aucun transfert envoyé sur la période.')).toBeInTheDocument();
  });

  it('change de période et la garde dans l’adresse', async () => {
    signedInAs(supervisorAccount);
    const { periods } = dashboardServer();
    const { user, router } = renderApp('/dashboard');
    await screen.findByRole('region', { name: 'Caisse de Nouakchott' });
    expect(screen.getByRole('button', { name: "Aujourd'hui" })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('button', { name: '7 derniers jours' }));

    const from = addDays(today, -6);
    await waitFor(() => expect(router.state.location.search).toEqual({ from, to: today }));
    await waitFor(() => expect(periods.at(-1)).toEqual({ from, to: today }));
    expect(screen.getByRole('button', { name: '7 derniers jours' })).toHaveAttribute('aria-pressed', 'true');
  });

  it("n'est pas accessible sans balance.view, et l'Agent ouvre toujours sur les transferts", async () => {
    signedInAs(agentAccount);
    server.use(
      http.get(apiUrl('/transfers'), () => HttpResponse.json({ items: [], page: 1, pageSize: 50, totalCount: 0, allCount: 0 })),
      http.get(apiUrl('/transfers/summary'), () => HttpResponse.json({}, { status: 500 })),
    );
    const { router } = renderApp('/');

    await waitFor(() => expect(router.state.location.pathname).toBe('/transfers'));

    await router.navigate({ to: '/dashboard' });
    expect(await screen.findByText("Votre compte n'a pas accès à cet écran.")).toBeInTheDocument();
  });
});
