import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { agentAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { PartySummary } from '../types';

// Actions du Superviseur sur la fiche d'un tiers (livraison 2). Données fictives uniquement.

function party(overrides: Partial<PartySummary> = {}): PartySummary {
  return {
    id: 'p-1',
    name: 'Boutique Fictive',
    type: 'Client',
    accountCode: '41',
    phone: '+22220000011',
    nni: '0000000001',
    address: 'Quartier fictif',
    note: null,
    inBalanceSheet: true,
    isArchived: false,
    balances: [
      { agencyId: nkc.id, currency: 'MRO', balance: 1_200_000, creditLimit: 1_500_000, openingBalance: 1_000_000 },
      { agencyId: dkr.id, currency: 'XOF', balance: 0, creditLimit: null, openingBalance: 0 },
    ],
    ...overrides,
  };
}

/** Le serveur de la fiche : chaque appel modifiant est noté ; la fiche relue reflète l'état voulu. */
function cardServer(initial: PartySummary, options: { failWith?: string } = {}) {
  let current = initial;
  const calls: { method: string; path: string; body: unknown }[] = [];
  const record = async (request: Request, path: string) => {
    const text = await request.text();
    calls.push({ method: request.method, path, body: text ? JSON.parse(text) : null });
  };
  const fail = () => HttpResponse.json({ code: options.failWith }, { status: 409 });
  server.use(
    http.get(apiUrl('/parties/:id'), () => HttpResponse.json(current)),
    http.put(apiUrl('/parties/:id'), async ({ request }) => {
      await record(request, 'update');
      if (options.failWith) return fail();
      current = { ...current, ...((calls.at(-1)!.body as object) ?? {}) };
      return new HttpResponse(null, { status: 204 });
    }),
    http.put(apiUrl('/parties/:id/agencies/:agencyId/:what'), async ({ request, params }) => {
      await record(request, `${params.what as string}:${params.agencyId as string}`);
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(apiUrl('/parties/:id/:action'), async ({ request, params }) => {
      await record(request, params.action as string);
      current = { ...current, isArchived: params.action === 'archive' };
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return { calls };
}

async function openCard() {
  const view = renderApp('/parties/p-1');
  await screen.findByRole('heading', { name: 'Boutique Fictive' });
  return view;
}

describe('Fiche d’un tiers — actions du Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('modifie la fiche en envoyant son état complet (champ vidé = retiré)', async () => {
    const { calls } = cardServer(party());
    const { user } = await openCard();
    await user.click(screen.getByRole('button', { name: 'Modifier la fiche' }));
    const dialog = await screen.findByRole('dialog');

    await user.clear(within(dialog).getByLabelText('Adresse'));
    await user.type(within(dialog).getByLabelText('Note'), 'Paie en fin de mois');
    await user.click(within(dialog).getByLabelText('Dans le bilan (compté dans le budget de la caisse)'));
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer la fiche' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(calls).toEqual([
      {
        method: 'PUT',
        path: 'update',
        body: {
          name: 'Boutique Fictive',
          type: 'Client',
          phone: '+22220000011',
          nni: '0000000001',
          address: null,
          note: 'Paie en fin de mois',
          inBalanceSheet: false,
        },
      },
    ]);
  });

  it('traduit un refus du serveur sans fermer la fenêtre', async () => {
    cardServer(party(), { failWith: 'Parties.Party.PhoneTaken' });
    const { user } = await openCard();
    await user.click(screen.getByRole('button', { name: 'Modifier la fiche' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer la fiche' }));
    expect(await within(dialog).findByText(/Ce numéro appartient déjà à un autre tiers/)).toBeInTheDocument();
  });

  it('solde initial : montre l’actuel, calcule la différence et envoie le nouveau solde signé', async () => {
    const { calls } = cardServer(party());
    const { user } = await openCard();
    await user.click(screen.getByRole('button', { name: 'Solde initial · Nouakchott' }));
    const dialog = await screen.findByRole('dialog');

    expect(within(dialog).getByText('Solde initial actuel').parentElement).toHaveTextContent('1 000 000 MRO');
    expect(within(dialog).getByText('Solde actuel (toutes opérations)').parentElement).toHaveTextContent('1 200 000 MRO');
    const field = within(dialog).getByLabelText('Nouveau solde initial');
    expect(field).toHaveValue('1000000');
    expect(within(dialog).getByText('Aucun changement : rien ne sera écrit.')).toBeInTheDocument();

    await user.click(within(dialog).getByRole('radio', { name: 'On lui doit' }));
    await user.clear(field);
    await user.type(field, '250000');
    expect(within(dialog).getByText("Écriture d'ouverture : − 1 250 000 MRO")).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer le solde initial' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(calls).toEqual([{ method: 'PUT', path: `opening-balance:${nkc.id}`, body: { amount: -250_000, operationDate: null } }]);
  });

  it('solde initial : saisie en MRU convertie, date passée envoyée ; même valeur = aucun envoi', async () => {
    const { calls } = cardServer(party());
    const { user } = await openCard();
    await user.click(screen.getByRole('button', { name: 'Solde initial · Nouakchott' }));
    let dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer le solde initial' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(calls).toEqual([]);

    await user.click(screen.getByRole('button', { name: 'Solde initial · Nouakchott' }));
    dialog = await screen.findByRole('dialog');
    const field = within(dialog).getByLabelText('Nouveau solde initial');
    await user.click(within(dialog).getByRole('button', { name: 'MRU' }));
    await user.clear(field);
    await user.type(field, '12,345');
    expect(within(dialog).getByText('= 123,45 MRO')).toBeInTheDocument();
    // Un champ date ne se tape pas caractère par caractère dans jsdom : on pose la valeur choisie.
    fireEvent.change(within(dialog).getByLabelText("Date de l'écriture d'ouverture"), { target: { value: '2026-10-01' } });
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer le solde initial' }));

    await waitFor(() => expect(calls).toHaveLength(1));
    expect(calls[0]?.body).toEqual({ amount: 123.45, operationDate: '2026-10-01' });
  });

  it('plafond : « pas de plafond » envoie null, 0 = aucun crédit, FCFA à décimales refusé', async () => {
    const { calls } = cardServer(party());
    const { user } = await openCard();

    await user.click(screen.getByRole('button', { name: 'Plafond de crédit · Nouakchott' }));
    let dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Plafond actuel : 1 500 000 MRO.')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('radio', { name: 'Pas de plafond' }));
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer le plafond' }));
    await waitFor(() => expect(calls).toHaveLength(1));

    await user.click(screen.getByRole('button', { name: 'Plafond de crédit · Dakar' }));
    dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('radio', { name: 'Fixer un plafond' }));
    const field = within(dialog).getByLabelText('Plafond de crédit');
    await user.type(field, '10,5');
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer le plafond' }));
    expect(within(dialog).getByText('Le FCFA est toujours entier.')).toBeInTheDocument();
    await user.clear(field);
    await user.type(field, '0');
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer le plafond' }));

    await waitFor(() => expect(calls).toHaveLength(2));
    expect(calls).toEqual([
      { method: 'PUT', path: `credit-limit:${nkc.id}`, body: { creditLimit: null } },
      { method: 'PUT', path: `credit-limit:${dkr.id}`, body: { creditLimit: 0 } },
    ]);
  });

  it('n’offre pas d’archiver un tiers dont un solde n’est pas nul', async () => {
    const { calls } = cardServer(party());
    const { user } = await openCard();
    await user.click(screen.getByRole('button', { name: 'Archiver' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/son solde doit être à zéro dans les deux agences/)).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Archiver' })).not.toBeInTheDocument();
    expect(calls).toEqual([]);
  });

  it('archive un tiers soldé, puis le désarchive ; archivé, la fiche ne se modifie plus', async () => {
    const settled = party({
      balances: [
        { agencyId: nkc.id, currency: 'MRO', balance: 0, creditLimit: null, openingBalance: 0 },
        { agencyId: dkr.id, currency: 'XOF', balance: 0, creditLimit: null, openingBalance: 0 },
      ],
    });
    const { calls } = cardServer(settled);
    const { user } = await openCard();

    await user.click(screen.getByRole('button', { name: 'Archiver' }));
    let dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Archiver' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    await screen.findByRole('button', { name: 'Désarchiver' });
    expect(screen.getByText('Archivé')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Modifier la fiche' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Solde initial · Nouakchott' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Désarchiver' }));
    dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Désarchiver' }));
    await waitFor(() => expect(calls.map((c) => c.path)).toEqual(['archive', 'unarchive']));
  });
});

describe('Fiche d’un tiers — sans parties.manage', () => {
  it('n’affiche aucune action du Superviseur', async () => {
    signedInAs(agentAccount);
    cardServer(party({ balances: [{ agencyId: nkc.id, currency: 'MRO', balance: 0, creditLimit: null, openingBalance: 0 }] }));
    await openCard();
    expect(screen.queryByRole('button', { name: 'Modifier la fiche' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Archiver' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Solde initial/ })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Voir le relevé' })).toBeInTheDocument();
  });
});
