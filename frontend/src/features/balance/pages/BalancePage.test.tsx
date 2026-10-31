import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { agentAccount, dkr, nkc, readOnlyAccount, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type {
  BalanceComponents,
  BalanceOverview,
  BalanceSnapshot,
  CaisseCapital,
  GeneralBalanceSnapshot,
  GeneralBalanceVariation,
  GeneralBalanceView,
} from '../types';

// Bilan (maquettes 12 / D5) : en tête le bilan général (Nouakchott + Dakar converti au taux du bilan, décisions du
// 31 octobre 2026), dessous le bilan par caisse (deux caisses, deux capitaux). Données fictives uniquement ; les montants du
// bilan général reprennent les chiffres de référence (167 871 591 MRO au taux de 3 675,25).

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

const components = (currency: 'MRO' | 'XOF', cash: number, banks: number, debtors: number, creditors: number): BalanceComponents => ({
  currency,
  cash,
  banks,
  debtors,
  creditors,
  undelivered: 0,
  ownerContributions: 0,
  budget: cash + banks + debtors - creditors,
});

const general: GeneralBalanceView = {
  date: '2026-10-24',
  rate: 3675.25,
  nouakchott: components('MRO', 20_000_000, 250_000_000, 300_000_000, 425_872_343),
  dakar: components('XOF', 5_000_000, 20_000_000, 30_000_000, 22_697_525),
  dakarInMro: { ...components('MRO', 3_675_250, 14_701_000, 22_051_500, 16_683_815.75125), budget: 23_743_934.24875 },
  total: { ...components('MRO', 23_675_250, 264_701_000, 322_051_500, 442_556_158.75125), budget: 167_871_591.24875 },
  generalBalance: 167_871_591.24875,
  capital: 165_500_000,
  ownerContributionsMro: 0,
  capitalUsed: 165_500_000,
  netResult: 2_371_591.24875,
  netResultPercent: 1.432985,
  savedProfitsMro: 430_074,
};

const generalSnapshot: GeneralBalanceSnapshot = {
  id: 'g1',
  date: '2026-10-23',
  rate: 3675.25,
  nouakchott: general.nouakchott,
  dakar: general.dakar,
  total: general.total,
  generalBalance: 167_586_606,
  capital: 165_500_000,
  capitalUsed: 165_500_000,
  netResult: 2_086_606,
  netResultPercent: 1.26,
  savedProfitsMro: 430_074,
  savedBy: 'u',
  savedAt: '2026-10-23T22:19:00Z',
};

// Depuis la photo du 23/10 (taux 3 600) : +1 000 000 MRO d'espèces à Nouakchott, −2 000 000 FCFA d'espèces à Dakar, et l'effet
// du taux (3 600 → 3 675,25) sur les 32 302 475 FCFA de Dakar. Effets : +1 000 000 − 1 470 100 + 486 152,2 = +16 052,2.
const variation: GeneralBalanceVariation = {
  fromDate: '2026-10-23',
  fromSavedAt: '2026-10-23T22:19:00Z',
  fromRate: 3600,
  toRate: 3675.25,
  fromGeneralBalance: 167_855_539.05,
  toGeneralBalance: 167_871_591.25,
  components: [
    { component: 'Cash', nouakchottChange: 1_000_000, dakarChange: -2_000_000, dakarChangeMro: -1_470_100, effect: -470_100 },
    { component: 'Banks', nouakchottChange: 0, dakarChange: 0, dakarChangeMro: 0, effect: 0 },
    { component: 'Debtors', nouakchottChange: 0, dakarChange: 0, dakarChangeMro: 0, effect: 0 },
    { component: 'Creditors', nouakchottChange: 0, dakarChange: 0, dakarChangeMro: 0, effect: 0 },
    { component: 'Undelivered', nouakchottChange: 0, dakarChange: 0, dakarChangeMro: 0, effect: 0 },
  ],
  rateEffect: 486_152.2,
  generalBalanceChange: 16_052.2,
  capitalChange: 0,
  ownerContributionsChange: 1_000_000,
  netResultChange: -983_947.8,
};

function balanceServer(options: { noGeneralSnapshot?: boolean } = {}) {
  let reads = 0;
  const saves: unknown[] = [];
  const capitals: unknown[] = [];
  const generalRates: (string | null)[] = [];
  const generalSaves: unknown[] = [];
  const generalCapitals: unknown[] = [];
  server.use(
    http.get(apiUrl('/rates/:date'), ({ params }) =>
      HttpResponse.json({ date: params.date, saleRate: 3700, purchaseRate: 3675.25, updatedAt: '2026-10-24T07:00:00Z' }),
    ),
    http.get(apiUrl('/balance/general'), ({ request }) => {
      const rate = new URL(request.url).searchParams.get('rate');
      generalRates.push(rate);
      return HttpResponse.json({ ...general, rate: Number(rate) });
    }),
    http.get(apiUrl('/balance/general/snapshots'), () => HttpResponse.json([generalSnapshot])),
    http.get(apiUrl('/balance/general/variation'), () =>
      options.noGeneralSnapshot
        ? HttpResponse.json({ code: 'Balance.General.NoSnapshot' }, { status: 404 })
        : HttpResponse.json(variation),
    ),
    http.post(apiUrl('/balance/general/snapshots'), async ({ request }) => {
      generalSaves.push(await request.json());
      return HttpResponse.json({ ...generalSnapshot, id: 'g2', date: '2026-10-24', savedAt: '2026-10-24T22:08:00Z' });
    }),
    http.get(apiUrl('/balance/general/capital'), () =>
      HttpResponse.json({ current: 165_500_000, history: [{ id: 'gc1', amount: 165_500_000, reason: 'Mise en service', setBy: 'u', setAt: '2026-10-01T09:00:00Z' }] }),
    ),
    http.put(apiUrl('/balance/general/capital'), async ({ request }) => {
      generalCapitals.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }),
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
  return { reads: () => reads, saves, capitals, generalRates, generalSaves, generalCapitals };
}

const card = (name: string) => screen.getByRole('article', { name });
const generalSection = () => screen.findByRole('region', { name: 'Bilan général' });

describe('Bilan général — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('calcule au prix d’achat du jour : total, résultat net, bilan de chaque caisse et détail des composants', async () => {
    const { generalRates } = balanceServer();
    renderApp('/balance');

    const section = await generalSection();
    expect(await within(section).findByLabelText('Taux du bilan')).toHaveValue('3675,25');
    expect(await within(section).findByText('167 871 591 MRO')).toBeInTheDocument();
    expect(generalRates).toEqual(['3675.25']);
    expect(within(section).getByText('Capital général', { selector: 'dt' }).nextElementSibling).toHaveTextContent('165 500 000 MRO');
    expect(within(section).getByText('Résultat net').nextElementSibling).toHaveTextContent('+ 2 371 591 MRO');
    expect(within(section).getByText('Trésorerie toutes caisses').nextElementSibling).toHaveTextContent('288 376 250 MRO');
    expect(within(section).getByText('Résultat tiers').nextElementSibling).toHaveTextContent('120 504 659 MRO');
    expect(within(section).getByText('Bilan Dakar').nextElementSibling).toHaveTextContent('32 302 475 FCFA');
    expect(within(section).getByText('soit 23 743 934 MRO')).toBeInTheDocument();
    expect(within(section).getByText('Bénéfices enregistrés').nextElementSibling).toHaveTextContent('430 074 MRO');

    // Détail : totaux par composant, puis chaque caisse dans sa devise ; jamais le détail par tiers.
    const details = within(section).getByRole('region', { name: 'Détail des composants' });
    expect(within(details).getByText('Dettes tiers').closest('summary')).toHaveTextContent('442 556 159 MRO');
    expect(within(details).getByText('22 697 525 FCFA · soit 16 683 816 MRO')).toBeInTheDocument();
  });

  it('un autre taux ne part qu’avec « Actualiser », et un taux mal saisi est refusé', async () => {
    const { generalRates } = balanceServer();
    const { user } = renderApp('/balance');
    const section = await generalSection();
    await within(section).findByText('167 871 591 MRO');

    const rate = within(section).getByLabelText('Taux du bilan');
    await user.clear(rate);
    await user.type(rate, '3650,123');
    expect(generalRates).toEqual(['3675.25']);
    await user.click(within(section).getByRole('button', { name: 'Actualiser' }));
    expect(within(section).getByText('Deux décimales au maximum.')).toBeInTheDocument();

    await user.clear(rate);
    await user.type(rate, '3600{Enter}');
    await waitFor(() => expect(generalRates).toEqual(['3675.25', '3600']));
  });

  it('sauvegarde le bilan général avec son taux, après confirmation', async () => {
    const { generalSaves } = balanceServer();
    const { user } = renderApp('/balance');
    const section = await generalSection();
    await user.click(await within(section).findByRole('button', { name: 'Sauvegarder le bilan général' }));

    const dialog = await screen.findByRole('dialog', { name: 'Sauvegarder le bilan général ?' });
    expect(within(dialog).getByText('Bilan général').nextElementSibling).toHaveTextContent('167 871 591 MRO');
    await user.click(within(dialog).getByRole('button', { name: 'Je confirme' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(generalSaves).toEqual([{ rate: 3675.25 }]);
    expect(await within(section).findByText(/Bilan général sauvegardé \(24\/10 · 22:08\)\./)).toBeInTheDocument();
  });

  it('saisit le capital général en MRO', async () => {
    const { generalCapitals } = balanceServer();
    const { user } = renderApp('/balance');
    const section = await generalSection();
    await user.click(await within(section).findByRole('button', { name: 'Capital général' }));

    const dialog = await screen.findByRole('dialog', { name: 'Capital général' });
    expect(await within(dialog).findByText('Capital général actuel : 165 500 000 MRO.')).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText('Nouveau capital général'), '170000000');
    await user.type(within(dialog).getByLabelText('Motif (facultatif)'), 'Correction');
    await user.click(within(dialog).getByRole('button', { name: 'Enregistrer le capital' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(generalCapitals).toEqual([{ amount: 170_000_000, reason: 'Correction' }]);
  });

  it('« Expliquer la variation » : écart depuis la dernière photo, poste par poste, effet du taux et résultat', async () => {
    balanceServer();
    const { user } = renderApp('/balance');
    const section = await generalSection();
    await user.click(await within(section).findByRole('button', { name: 'Expliquer la variation' }));

    const dialog = await screen.findByRole('dialog', { name: 'D’où vient la variation ?' });
    expect(await within(dialog).findByText(/Depuis le bilan général sauvegardé le 23\/10 à 22:19/)).toBeInTheDocument();
    expect(within(dialog).getByText('Écart').nextElementSibling).toHaveTextContent('+ 16 052 MRO');

    // Seuls les postes qui ont bougé, avec Nouakchott en MRO et Dakar en FCFA.
    const lines = within(dialog).getByRole('region', { name: 'Poste par poste' });
    expect(within(lines).getByText('Espèces').closest('li')).toHaveTextContent('− 470 100 MRO');
    expect(within(lines).getByText('Espèces').closest('li')).toHaveTextContent('Nouakchott + 1 000 000 MRO · Dakar − 2 000 000 FCFA (soit − 1 470 100 MRO)');
    expect(within(lines).queryByText('Dettes tiers')).not.toBeInTheDocument();
    expect(within(lines).getByText('Effet du taux sur la caisse de Dakar').closest('li')).toHaveTextContent('+ 486 152 MRO');
    expect(within(lines).getByText('5 000 FCFA = 3 600 → 3 675,25 MRO')).toBeInTheDocument();
    expect(within(dialog).getByText('Apports nets du patron (deux caisses)').nextElementSibling).toHaveTextContent('+ 1 000 000 MRO');
    expect(within(dialog).getByText('Résultat net').nextElementSibling).toHaveTextContent('− 983 948 MRO');
  });

  it('« Expliquer la variation » sans aucune photo : le dit simplement', async () => {
    balanceServer({ noGeneralSnapshot: true });
    const { user } = renderApp('/balance');
    const section = await generalSection();
    await user.click(await within(section).findByRole('button', { name: 'Expliquer la variation' }));

    const dialog = await screen.findByRole('dialog', { name: 'D’où vient la variation ?' });
    expect(await within(dialog).findByText(/Aucun bilan général sauvegardé/)).toBeInTheDocument();
  });

  it('liste les bilans généraux sauvegardés, chacun dépliable sur ses totaux', async () => {
    balanceServer();
    const { user } = renderApp('/balance');

    const history = await screen.findByRole('region', { name: 'Bilans généraux sauvegardés (30 derniers jours)' });
    const list = within(history).getByRole('list', { name: 'Liste des bilans généraux sauvegardés' });
    const item = within(list).getByText('167 586 606 MRO').closest('details')!;
    expect(item).toHaveTextContent('23/10 · 22:19');
    expect(item).toHaveTextContent('Résultat net : + 2 086 606 MRO');
    expect(item).toHaveTextContent('5 000 FCFA = 3 675,25 MRO');

    await user.click(within(item).getByText('167 586 606 MRO'));
    expect(within(item).getByText('Trésorerie toutes caisses').nextElementSibling).toHaveTextContent('288 376 250 MRO');
    expect(within(item).getByText('Résultat tiers').nextElementSibling).toHaveTextContent('120 504 659 MRO');
  });
});

describe('Bilan par caisse — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('montre les deux caisses dans leur devise ; sans capital, pas de résultat net', async () => {
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

    const history = screen.getByRole('region', { name: 'Bilans enregistrés (30 derniers jours)' });
    expect(within(history).getByText('+ 270 000 MRO')).toBeInTheDocument();
    // Un seul bilan enregistré : pas de courbe, la liste suffit.
    expect(within(history).queryByRole('img')).not.toBeInTheDocument();
  });

  it('« Actualiser » recalcule le bilan', async () => {
    const server = balanceServer();
    const { user } = renderApp('/balance');
    await screen.findByRole('article', { name: 'Caisse de Nouakchott' });
    // Le premier « Actualiser » est celui de la page ; celui du bilan général applique son taux.
    await user.click(screen.getAllByRole('button', { name: 'Actualiser' })[0]!);
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
    expect(await screen.findByText('167 871 591 MRO')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Sauvegarder le bilan général' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Capital général' })).not.toBeInTheDocument();
  });

  it('sans balance.view : écran refusé', async () => {
    signedInAs(agentAccount);
    renderApp('/balance');
    expect(await screen.findByText("Votre compte n'a pas accès à cet écran.")).toBeInTheDocument();
  });
});
