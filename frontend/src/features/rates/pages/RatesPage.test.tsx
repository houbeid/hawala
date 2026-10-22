import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { addDays, todayIso } from '@/shared/format/dates';
import { agentAccount, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { DailyRate } from '../types';

const today = todayIso();
const yesterday = addDays(today, -1);

function rate(date: string, saleRate: number, purchaseRate: number | null): DailyRate {
  return { date, saleRate, purchaseRate, updatedAt: `${date}T08:00:00Z` };
}

const notFound = () => HttpResponse.json({ code: 'Rates.Rate.NotFound' }, { status: 404 });

/** Le serveur des taux : un dictionnaire date → taux, modifiable par PUT. */
function ratesServer(initial: Record<string, DailyRate>, options: { hidePurchase?: boolean } = {}) {
  const store = { ...initial };
  const puts: { date: string; body: unknown }[] = [];
  const view = (r: DailyRate) => (options.hidePurchase ? { ...r, purchaseRate: null } : r);
  server.use(
    http.get(apiUrl('/rates'), () => HttpResponse.json(Object.values(store).map(view))),
    http.get(apiUrl('/rates/:date'), ({ params }) => {
      const found = store[params.date as string];
      return found ? HttpResponse.json(view(found)) : notFound();
    }),
    http.put(apiUrl('/rates/:date'), async ({ params, request }) => {
      const body = (await request.json()) as { purchaseRate: number; saleRate: number };
      const date = params.date as string;
      puts.push({ date, body });
      store[date] = { date, ...body, updatedAt: new Date().toISOString() };
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return { puts };
}

const purchaseField = () => screen.getByLabelText("Prix d'achat (taux du marché)");
const saleField = () => screen.getByLabelText('Prix de vente (taux appliqué au client)');

describe('Taux du jour — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('affiche le taux du jour dans la phrase à trou, avec l’écart de commission', async () => {
    ratesServer({ [today]: rate(today, 3650, 3600) });
    renderApp('/rates');

    await waitFor(() => expect(purchaseField()).toHaveValue('3600'));
    expect(saleField()).toHaveValue('3650');
    expect(screen.getByText('Écart de commission')).toBeInTheDocument();
    expect(screen.getByText(/aujourd'hui/)).toBeInTheDocument();
  });

  it('enregistre un taux avec des décimales, sans arrondi', async () => {
    const { puts } = ratesServer({ [today]: rate(today, 3650, 3600) });
    const { user } = renderApp('/rates');
    await waitFor(() => expect(purchaseField()).toHaveValue('3600'));

    await user.clear(purchaseField());
    await user.type(purchaseField(), '3 604,77');
    await user.click(screen.getByRole('button', { name: 'Enregistrer le taux' }));

    expect(await screen.findByText(/^Taux du .* enregistré\.$/)).toBeInTheDocument();
    expect(puts).toEqual([{ date: today, body: { purchaseRate: 3604.77, saleRate: 3650 } }]);
  });

  it('refuse plus de deux décimales avant tout envoi', async () => {
    const { puts } = ratesServer({ [today]: rate(today, 3650, 3600) });
    const { user } = renderApp('/rates');
    await waitFor(() => expect(purchaseField()).toHaveValue('3600'));

    await user.clear(purchaseField());
    await user.type(purchaseField(), '3600,555');
    await user.click(screen.getByRole('button', { name: 'Enregistrer le taux' }));

    expect(await screen.findByText('Deux décimales au maximum.')).toBeInTheDocument();
    expect(purchaseField()).toHaveAttribute('aria-invalid', 'true');
    expect(puts).toEqual([]);
  });

  it('avertit sans bloquer quand le prix de vente est inférieur au prix d’achat', async () => {
    const { puts } = ratesServer({ [today]: rate(today, 3650, 3600) });
    const { user } = renderApp('/rates');
    await waitFor(() => expect(saleField()).toHaveValue('3650'));

    await user.clear(saleField());
    await user.type(saleField(), '3550');

    expect(screen.getByText(/la commission sera négative \(− 50 MRO pour 5 000 FCFA\)/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Enregistrer le taux' }));
    await screen.findByText(/^Taux du .* enregistré\.$/);
    expect(puts).toHaveLength(1);
  });

  it('sans taux pour la date : prévient, et reprend le taux de la veille sans l’enregistrer', async () => {
    const { puts } = ratesServer({ [yesterday]: rate(yesterday, 3648, 3598.5) });
    const { user } = renderApp('/rates');

    expect(await screen.findByText(/Aucun taux fixé pour cette date\. Sans taux du jour/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reprendre le taux de la veille' }));

    await waitFor(() => expect(purchaseField()).toHaveValue('3598,5'));
    expect(saleField()).toHaveValue('3648');
    expect(puts).toEqual([]);
  });

  it('change de jour avec les flèches et garde la date dans l’adresse', async () => {
    ratesServer({ [today]: rate(today, 3650, 3600), [yesterday]: rate(yesterday, 3640, 3590) });
    const { user, router } = renderApp('/rates');
    await waitFor(() => expect(saleField()).toHaveValue('3650'));

    await user.click(screen.getByRole('button', { name: 'Jour précédent' }));

    await waitFor(() => expect(saleField()).toHaveValue('3640'));
    expect(screen.getByText(/· hier/)).toBeInTheDocument();
    expect(router.state.location.search).toEqual({ date: yesterday });
  });

  it('liste l’historique du plus récent au plus ancien, avec le prix d’achat', async () => {
    ratesServer({ [yesterday]: rate(yesterday, 3648, 3598.5), [today]: rate(today, 3650, 3600) });
    renderApp('/rates');

    const table = await screen.findByRole('table', { name: 'Historique' });
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows[0]).toHaveTextContent(`${today.slice(8, 10)}/${today.slice(5, 7)}`);
    expect(within(table).getByRole('columnheader', { name: 'Achat' })).toBeInTheDocument();
    // Testing Library ram\u00E8ne les espaces sp\u00E9ciales \u00E0 des espaces ordinaires avant de comparer.
    expect(rows[1]).toHaveTextContent('3 598,50');
  });

  it('traduit un refus du serveur', async () => {
    ratesServer({ [today]: rate(today, 3650, 3600) });
    server.use(
      http.put(apiUrl('/rates/:date'), () => HttpResponse.json({ code: 'Rates.Rate.SaleInvalid' }, { status: 400 })),
    );
    const { user } = renderApp('/rates');
    await waitFor(() => expect(saleField()).toHaveValue('3650'));

    await user.click(screen.getByRole('button', { name: 'Enregistrer le taux' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Le prix de vente doit être positif, avec au plus 2 décimales.');
  });
});

describe('Taux du jour — Agent (lecture seule)', () => {
  beforeEach(() => signedInAs(agentAccount));

  it('montre le prix de vente seulement, sans champ ni bouton', async () => {
    ratesServer({ [today]: rate(today, 3650, 3600) }, { hidePurchase: true });
    renderApp('/rates');

    expect(await screen.findByText('Prix de vente (taux appliqué au client)')).toBeInTheDocument();
    expect(screen.getByText('5 000 FCFA = 3 650 MRO')).toBeInTheDocument();
    expect(screen.queryByText("Prix d'achat (taux du marché)")).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Enregistrer le taux' })).not.toBeInTheDocument();
    expect(within(await screen.findByRole('table')).queryByRole('columnheader', { name: 'Achat' })).not.toBeInTheDocument();
  });

  it('prévient quand aucun taux n’est fixé', async () => {
    ratesServer({});
    renderApp('/rates');

    expect(await screen.findByText(/les transferts ne peuvent pas être créés/)).toBeInTheDocument();
  });
});
