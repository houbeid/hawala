import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { agentAccount, dkr, nkc, readOnlyAccount, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { BalanceOverview, BalanceSnapshot, CaisseCapital } from '../types';

// Bilan par caisse (maquettes 12 / D5) : deux caisses, deux capitaux, sans taux ni total converti.
// Données fictives uniquement.

const overview: BalanceOverview = {
  date: '2026-10-24',
  caisses: [
    {
      agencyId: nkc.id,
      currency: 'MRO',
      date: '2026-10-24',
      budget: 3_920_000.4,
      initialCapital: 3_500_000,
      ownerContributions: 120_000,
      capitalUsed: 3_620_000,
      netResult: 300_000.4,
      netResultPercent: 8.29,
      savedProfitsMro: 792_900,
    },
    {
      agencyId: dkr.id,
      currency: 'XOF',
      date: '2026-10-24',
      budget: 62_300_000,
      initialCapital: null,
      ownerContributions: 0,
      capitalUsed: null,
      netResult: null,
      netResultPercent: null,
      savedProfitsMro: 0,
    },
  ],
};

const snapshot: BalanceSnapshot = {
  ...overview.caisses[0]!,
  id: 's1',
  date: '2026-10-20',
  netResult: 270_000,
  netResultPercent: 7.5,
  savedBy: 'u',
  savedAt: '2026-10-20T18:05:00Z',
};

const capital: CaisseCapital[] = [
  { agencyId: nkc.id, currency: 'MRO', current: 3_500_000, history: [{ id: 'c1', amount: 3_500_000, currency: 'MRO', reason: 'Mise en service', setBy: 'u', setAt: '2026-10-01T09:00:00Z' }] },
  { agencyId: dkr.id, currency: 'XOF', current: null, history: [] },
];

function balanceServer() {
  let reads = 0;
  const saves: unknown[] = [];
  const capitals: unknown[] = [];
  server.use(
    http.get(apiUrl('/balance'), () => {
      reads += 1;
      return HttpResponse.json(overview);
    }),
    http.get(apiUrl('/balance/snapshots'), () => HttpResponse.json([snapshot])),
    http.get(apiUrl('/balance/capital'), () => HttpResponse.json(capital)),
    http.post(apiUrl('/balance/snapshots'), async ({ request }) => {
      saves.push(await request.json());
      return HttpResponse.json({ ...snapshot, date: '2026-10-24', savedAt: '2026-10-24T15:30:00Z' });
    }),
    http.put(apiUrl('/balance/capital'), async ({ request }) => {
      capitals.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return { reads: () => reads, saves, capitals };
}

const card = (name: string) => screen.getByRole('article', { name });

describe('Bilan par caisse — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('montre les deux caisses dans leur devise, sans total converti ; sans capital, pas de résultat net', async () => {
    balanceServer();
    renderApp('/balance');

    const nouakchott = await screen.findByRole('article', { name: 'Caisse de Nouakchott' });
    expect(within(nouakchott).getByText('Budget de la caisse').nextElementSibling).toHaveTextContent('3 920 000 MRO');
    expect(within(nouakchott).getByText('Apports nets du patron').nextElementSibling).toHaveTextContent('+ 120 000 MRO');
    expect(within(nouakchott).getByText('Capital pris en compte').nextElementSibling).toHaveTextContent('3 620 000 MRO');
    expect(within(nouakchott).getByText('Résultat net').nextElementSibling).toHaveTextContent('+ 300 000 MRO');
    expect(within(nouakchott).getByText('Rapport au capital').nextElementSibling).toHaveTextContent('+ 8,29 %');
    expect(within(nouakchott).getByText('Bénéfices enregistrés').nextElementSibling).toHaveTextContent('792 900 MRO');

    const dakar = card('Caisse de Dakar');
    expect(within(dakar).getByText('Budget de la caisse').nextElementSibling).toHaveTextContent('62 300 000 FCFA');
    expect(within(dakar).getByText('Capital initial', { selector: 'dt' }).nextElementSibling).toHaveTextContent('non saisi');
    expect(within(dakar).getByText(/Saisissez le capital initial/)).toBeInTheDocument();
    expect(within(dakar).queryByText('Résultat net')).not.toBeInTheDocument();
    expect(screen.queryByText(/Taux du bilan/)).not.toBeInTheDocument();

    const history = screen.getByRole('region', { name: 'Bilans enregistrés (30 derniers jours)' });
    expect(within(history).getByText('+ 270 000 MRO')).toBeInTheDocument();
    // Un seul bilan enregistré : pas de courbe, la liste suffit.
    expect(within(history).queryByRole('img')).not.toBeInTheDocument();
  });

  it('« Actualiser » recalcule le bilan', async () => {
    const server = balanceServer();
    const { user } = renderApp('/balance');
    await screen.findByRole('article', { name: 'Caisse de Nouakchott' });
    await user.click(screen.getByRole('button', { name: 'Actualiser' }));
    await waitFor(() => expect(server.reads()).toBe(2));
  });

  it('enregistre le bilan d’une caisse après confirmation', async () => {
    const { saves } = balanceServer();
    const { user } = renderApp('/balance');
    await user.click(within(await screen.findByRole('article', { name: 'Caisse de Nouakchott' })).getByRole('button', { name: 'Enregistrer le bilan' }));

    const dialog = await screen.findByRole('dialog', { name: 'Enregistrer le bilan de Nouakchott ?' });
    expect(within(dialog).getByText('Le bilan de l’autre caisse s’enregistre séparément.')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Je confirme' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(saves).toEqual([{ agencyId: nkc.id }]);
    expect(await screen.findByText(/Bilan de Nouakchott enregistré \(24\/10 · 15:30\)\./)).toBeInTheDocument();
  });

  it('saisit le capital initial d’une caisse, dans sa devise', async () => {
    const { capitals } = balanceServer();
    const { user } = renderApp('/balance');
    await user.click(within(await screen.findByRole('article', { name: 'Caisse de Dakar' })).getByRole('button', { name: 'Capital initial' }));

    const dialog = await screen.findByRole('dialog', { name: 'Capital initial · Dakar' });
    expect(await within(dialog).findByText('Aucun capital initial saisi pour cette caisse.')).toBeInTheDocument();
    const field = within(dialog).getByLabelText('Nouveau capital initial');
    await user.type(field, '58000000,5');
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer le capital' }));
    expect(within(dialog).getByText('Le FCFA est toujours entier.')).toBeInTheDocument();
    expect(capitals).toEqual([]);

    await user.clear(field);
    await user.type(field, '58000000');
    await user.type(within(dialog).getByLabelText('Motif (facultatif)'), 'Mise en service');
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer le capital' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(capitals).toEqual([{ agencyId: dkr.id, amount: 58_000_000, reason: 'Mise en service' }]);
  });
});

describe('Bilan par caisse — autres comptes', () => {
  it('avec balance.view seulement : lecture, sans enregistrement ni capital', async () => {
    signedInAs({ ...readOnlyAccount, permissions: [...readOnlyAccount.permissions, 'balance.view'] });
    balanceServer();
    renderApp('/balance');
    await screen.findByRole('article', { name: 'Caisse de Nouakchott' });
    expect(screen.queryByRole('button', { name: 'Enregistrer le bilan' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Capital initial' })).not.toBeInTheDocument();
  });

  it('sans balance.view : écran refusé', async () => {
    signedInAs(agentAccount);
    renderApp('/balance');
    expect(await screen.findByText("Votre compte n'a pas accès à cet écran.")).toBeInTheDocument();
  });
});
