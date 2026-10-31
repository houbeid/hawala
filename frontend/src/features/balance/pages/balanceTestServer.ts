import { http, HttpResponse } from 'msw';
import { dkr, nkc } from '@/test/fixtures';
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

// Données des tests du bilan (bilan général et bilan par caisse). Données fictives uniquement ; les montants du bilan général
// reprennent les chiffres de référence (167 871 591 MRO au taux de 3 675,25).

export const overview: BalanceOverview = {
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

export function balanceServer(options: { noGeneralSnapshot?: boolean } = {}) {
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

