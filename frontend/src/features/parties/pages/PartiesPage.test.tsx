import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { agentAccount, cashierAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { stubDownloads } from '@/test/downloads';
import { compactDay, todayIso } from '@/shared/format/dates';
import { exportServer } from '@/test/exportServer';
import { apiUrl, server } from '@/test/server';
import type { PartyList, PartyStatement, PartySummary } from '../types';

// Données fictives uniquement.
function party(overrides: Partial<PartySummary>): PartySummary {
  return {
    id: 'p',
    name: 'Tiers',
    type: 'Client',
    accountCode: '41',
    phone: null,
    nni: null,
    address: null,
    note: null,
    inBalanceSheet: true,
    isArchived: false,
    balances: [],
    ...overrides,
  };
}

const wafa = party({
  id: 'p-wafa',
  name: 'Boutique Fictive',
  phone: '+22220000011',
  nni: '0000000001',
  address: 'Quartier fictif',
  balances: [
    { agencyId: nkc.id, currency: 'MRO', balance: 1_200_000, creditLimit: 1_500_000, openingBalance: 1_000_000 },
    { agencyId: dkr.id, currency: 'XOF', balance: 0, creditLimit: null, openingBalance: 0 },
  ],
});
const sahel = party({
  id: 'p-sahel',
  name: 'Fournisseur Fictif',
  type: 'Supplier',
  accountCode: '40',
  inBalanceSheet: false,
  balances: [
    { agencyId: nkc.id, currency: 'MRO', balance: -640_000, creditLimit: 0, openingBalance: 0 },
    { agencyId: dkr.id, currency: 'XOF', balance: 870_000, creditLimit: null, openingBalance: 0 },
  ],
});

/** Le serveur des tiers : la liste garde les soldes des agences demandées, comme le backend. */
function partiesServer(items: PartySummary[], options: { hideBalances?: boolean } = {}) {
  const queries: URLSearchParams[] = [];
  const view = (p: PartySummary, agencyId: string | null): PartySummary => ({
    ...p,
    balances: options.hideBalances ? null : (p.balances ?? []).filter((b) => !agencyId || b.agencyId === agencyId),
  });
  server.use(
    http.get(apiUrl('/parties'), ({ request }) => {
      const query = new URL(request.url).searchParams;
      queries.push(query);
      const agencyId = query.get('agencyId');
      const list = items
        .filter((p) => !query.get('type') || p.type === query.get('type'))
        .filter((p) => !query.get('search') || p.name.toLowerCase().includes(query.get('search')!.toLowerCase()))
        .map((p) => view(p, agencyId));
      const totals = options.hideBalances
        ? null
        : (['MRO', 'XOF'] as const)
            .map((currency) => {
              const all = list.flatMap((p) => p.balances ?? []).filter((b) => b.currency === currency);
              const red = all.filter((b) => b.balance > 0).reduce((s, b) => s + b.balance, 0);
              const green = all.filter((b) => b.balance < 0).reduce((s, b) => s + b.balance, 0);
              return all.length > 0 ? [{ currency, red, green, net: red + green }] : [];
            })
            .flat();
      const body: PartyList = { items: list, count: list.length, totalCount: items.length, totals };
      return HttpResponse.json(body);
    }),
    http.get(apiUrl('/parties/:id'), ({ params }) => {
      const found = items.find((p) => p.id === params.id);
      return found ? HttpResponse.json(view(found, null)) : HttpResponse.json({ code: 'Parties.Party.NotFound' }, { status: 404 });
    }),
  );
  return { queries };
}

describe('Tiers — liste, Agent de Nouakchott', () => {
  beforeEach(() => signedInAs(agentAccount));

  it('montre le solde de son agence en toutes lettres, les totaux et le compteur', async () => {
    const { queries } = partiesServer([wafa, sahel]);
    renderApp('/parties');

    const wafaRow = (await screen.findByText('Boutique Fictive')).closest('a') as HTMLElement;
    expect(within(wafaRow).getByText('1 200 000 MRO')).toBeInTheDocument();
    expect(within(wafaRow).getByText('Il nous doit')).toBeInTheDocument();
    expect(within(wafaRow).queryByText(/FCFA/)).not.toBeInTheDocument();
    const sahelRow = screen.getByText('Fournisseur Fictif').closest('a') as HTMLElement;
    expect(within(sahelRow).getByText('640 000 MRO')).toBeInTheDocument();
    expect(within(sahelRow).getByText('On lui doit')).toBeInTheDocument();
    expect(within(sahelRow).getByText('Hors bilan')).toBeInTheDocument();
    expect(screen.getByText('Ils nous doivent').nextElementSibling).toHaveTextContent('1 200 000 MRO');
    expect(screen.getByText('On leur doit').nextElementSibling).toHaveTextContent('640 000 MRO');
    expect(screen.getByText('2 / 2 tiers')).toBeInTheDocument();
    expect(queries[0]?.get('agencyId')).toBe(nkc.id);
  });

  it('envoie les filtres et la recherche au serveur', async () => {
    const { queries } = partiesServer([wafa, sahel]);
    const { user, router } = renderApp('/parties');
    await screen.findByText('Boutique Fictive');

    await user.selectOptions(screen.getByLabelText('Type'), 'Supplier');
    await waitFor(() => expect(screen.queryByText('Boutique Fictive')).not.toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText('Solde'), 'Green');
    await user.type(screen.getByRole('searchbox', { name: 'Rechercher un tiers' }), 'fourn');

    await waitFor(() => expect(queries.at(-1)?.get('search')).toBe('fourn'));
    const last = queries.at(-1)!;
    expect(last.get('type')).toBe('Supplier');
    expect(last.get('balance')).toBe('Green');
    expect(router.state.location.search).toMatchObject({ type: 'Supplier', balance: 'Green', search: 'fourn' });

    await user.click(screen.getByRole('button', { name: 'Réinitialiser (2 filtres)' }));
    await waitFor(() => expect(queries.at(-1)?.has('type')).toBe(false));
  });

  it('dit quand aucun tiers ne correspond', async () => {
    partiesServer([wafa]);
    const { user } = renderApp('/parties?type=Staff');
    expect(await screen.findByText('Aucun tiers trouvé.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Réinitialiser les filtres' }));
    expect(await screen.findByText('Boutique Fictive')).toBeInTheDocument();
  });

  it('crée un tiers depuis la liste et ouvre sa fiche', async () => {
    const created = party({ id: 'p-new', name: 'Nouveau Fictif', balances: [] });
    partiesServer([wafa, created]);
    const bodies: unknown[] = [];
    server.use(
      http.post(apiUrl('/parties'), async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json({ id: 'p-new' }, { status: 201 });
      }),
    );
    const { user, router } = renderApp('/parties');
    await user.click(await screen.findByRole('button', { name: 'Nouveau tiers' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText('Nom (30 caractères au plus)'), 'Nouveau Fictif');
    await user.click(within(dialog).getByRole('button', { name: 'Créer le tiers' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/parties/p-new'));
    expect(bodies).toEqual([{ name: 'Nouveau Fictif', type: 'Client', phone: null, nni: null, address: null, note: null }]);
  });
});

describe('Tiers — liste, Superviseur sur les deux agences', () => {
  it('montre les deux soldes avec leur agence, sans filtre de solde, totaux par devise', async () => {
    signedInAs(supervisorAccount);
    const { queries } = partiesServer([wafa, sahel]);
    renderApp('/parties');

    const sahelRow = (await screen.findByText('Fournisseur Fictif')).closest('a') as HTMLElement;
    expect(within(sahelRow).getByText('NKC')).toBeInTheDocument();
    expect(within(sahelRow).getByText('870 000 FCFA')).toBeInTheDocument();
    expect(screen.queryByLabelText('Solde')).not.toBeInTheDocument();
    expect(screen.getAllByText('Net')).toHaveLength(2);
    expect(queries[0]?.has('agencyId')).toBe(false);
  });

  it('exporte la liste en Excel, avec ses filtres', async () => {
    signedInAs(supervisorAccount);
    partiesServer([wafa, sahel]);
    const exports = exportServer('/parties/export');
    const downloads = stubDownloads();
    const { user } = renderApp('/parties?type=Supplier');

    await user.click(await screen.findByRole('button', { name: 'Exporter' }));

    expect(await screen.findByText('Fichier Excel téléchargé.')).toBeInTheDocument();
    expect(exports).toEqual([{ type: 'Supplier', lang: 'fr' }]);
    expect(downloads.files).toEqual([`Tiers-${compactDay(todayIso())}.xlsx`]);
    downloads.restore();
  });
});

describe('Tiers — export, Agent', () => {
  it('pas de bouton « Exporter » sans la permission export', async () => {
    signedInAs(agentAccount);
    partiesServer([wafa]);
    renderApp('/parties');

    await screen.findByText(wafa.name);
    expect(screen.queryByRole('button', { name: 'Exporter' })).not.toBeInTheDocument();
  });
});

describe('Tiers — liste, compte sans parties.view_balance', () => {
  it('n’affiche ni solde ni total', async () => {
    signedInAs(cashierAccount);
    partiesServer([wafa], { hideBalances: true });
    renderApp('/parties');

    expect(await screen.findByText('Boutique Fictive')).toBeInTheDocument();
    expect(screen.queryByText('Il nous doit')).not.toBeInTheDocument();
    expect(screen.queryByText('Ils nous doivent')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Solde')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nouveau tiers' })).not.toBeInTheDocument();
  });
});

describe('Tiers — fiche', () => {
  it('montre la fiche, le solde et le plafond par agence, et le lien vers le relevé', async () => {
    signedInAs(supervisorAccount);
    partiesServer([wafa]);
    const { user, router } = renderApp('/parties/p-wafa');

    expect(await screen.findByRole('heading', { name: 'Boutique Fictive' })).toBeInTheDocument();
    expect(screen.getByText('+22220000011')).toBeInTheDocument();
    expect(screen.getByText('Quartier fictif')).toBeInTheDocument();
    expect(screen.getByText(/compte 41/)).toBeInTheDocument();
    expect(screen.getByText('Utilisé 80 % · il reste 300 000 MRO')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Plafond utilisé' })).toHaveAttribute('aria-valuenow', '80');
    expect(screen.getByText('Pas de plafond de crédit.')).toBeInTheDocument();

    await user.click(screen.getAllByRole('link', { name: 'Voir le relevé' })[0]!);
    await waitFor(() => expect(router.state.location.pathname).toBe('/parties/p-wafa/statement'));
    expect(router.state.location.search).toEqual({ agencyId: nkc.id });
  });

  it('distingue « aucun crédit » (plafond à zéro) et un tiers introuvable', async () => {
    signedInAs(agentAccount);
    partiesServer([sahel]);
    renderApp('/parties/p-sahel');
    expect(await screen.findByText('Aucun crédit autorisé (plafond à zéro).')).toBeInTheDocument();
  });

  it('dit qu’un tiers hors de portée est introuvable', async () => {
    signedInAs(agentAccount);
    partiesServer([]);
    renderApp('/parties/p-absent');
    expect(await screen.findByRole('link', { name: 'Retour à la liste des tiers' })).toBeInTheDocument();
  });
});

describe('Tiers — relevé', () => {
  const statement: PartyStatement = {
    partyId: 'p-wafa',
    partyName: 'Boutique Fictive',
    agencyId: nkc.id,
    currency: 'MRO',
    from: '2026-10-01',
    to: '2026-10-21',
    openingBalance: 950_000,
    lines: [
      {
        operationDate: '2026-10-04',
        recordedAt: '2026-10-04T10:00:00Z',
        entryType: 'Transfers.Created',
        label: 'Transfert TR-NKC-20261004-000012',
        reason: null,
        isReversal: false,
        debit: 112_500,
        credit: 0,
        balanceAfter: 1_062_500,
      },
      {
        operationDate: '2026-10-12',
        recordedAt: '2026-10-12T10:00:00Z',
        entryType: 'Tests.Unknown',
        label: 'Écriture de test',
        reason: null,
        isReversal: false,
        debit: 0,
        credit: 1_100_000,
        balanceAfter: -37_500,
      },
    ],
    closingBalance: -37_500,
  };

  it('montre début, mouvements signés avec le solde après, et fin ; la période part au serveur', async () => {
    signedInAs(agentAccount);
    const queries: URLSearchParams[] = [];
    server.use(
      http.get(apiUrl('/parties/:id/statement'), ({ request }) => {
        queries.push(new URL(request.url).searchParams);
        return HttpResponse.json(statement);
      }),
    );
    renderApp(`/parties/p-wafa/statement?agencyId=${nkc.id}`);

    expect(await screen.findByText('Solde de début · 01/10')).toBeInTheDocument();
    expect(screen.getByText('950 000 MRO')).toBeInTheDocument();
    expect(screen.getByText('+ 112 500 MRO')).toBeInTheDocument();
    expect(screen.getByText('− 1 100 000 MRO')).toBeInTheDocument();
    expect(screen.getByText('Solde de fin · 21/10').parentElement).toHaveTextContent('37 500 MRO');
    expect(screen.getByText('Solde de fin · 21/10').parentElement).toHaveTextContent('On lui doit');
    // Titre traduit d'après le type d'écriture, libellé du serveur dessous ; type inconnu = libellé du serveur.
    expect(screen.getByText('Crédit sur un transfert')).toBeInTheDocument();
    expect(screen.getByText('Transfert TR-NKC-20261004-000012')).toBeInTheDocument();
    expect(screen.getByText('Écriture de test')).toBeInTheDocument();
    expect(queries[0]?.get('agencyId')).toBe(nkc.id);
    expect(queries[0]?.has('from')).toBe(false);

    // Un champ date ne se tape pas caractère par caractère dans jsdom : on pose la valeur choisie.
    fireEvent.change(screen.getByLabelText('Du'), { target: { value: '2026-09-01' } });
    await waitFor(() => expect(queries.at(-1)?.get('from')).toBe('2026-09-01'));
  });

  it('est absent pour un compte sans parties.view_balance', async () => {
    signedInAs(cashierAccount);
    renderApp('/parties/p-wafa/statement');
    expect(await screen.findByText("Votre compte n'a pas accès à cet écran.")).toBeInTheDocument();
  });
});
