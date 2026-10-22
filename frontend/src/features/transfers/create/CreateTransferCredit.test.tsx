import { screen, waitFor, within } from '@testing-library/react';
import type { UserEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { PartySummary } from '@/features/parties';
import type { TreasuryAccount } from '@/features/treasury/types';
import { todayIso } from '@/shared/format/dates';
import { agentAccount, nkc } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { CreatedTransfer } from '../types';

const today = todayIso();
const accounts: TreasuryAccount[] = [
  { id: 'acc-cash', name: 'Caisse principale', type: 'Cash', agencyId: nkc.id, currency: 'MRO', isActive: true, balance: null },
];

function party(balance: number, creditLimit: number | null): PartySummary {
  return {
    id: 'party-1',
    name: 'Boutique Test',
    type: 'Client',
    accountCode: '41',
    phone: '+22220000011',
    nni: null,
    address: null,
    note: null,
    inBalanceSheet: true,
    isArchived: false,
    balances: [{ agencyId: nkc.id, currency: 'MRO', balance, creditLimit }],
  };
}

function creditServer(found: PartySummary, created: Partial<CreatedTransfer> = {}) {
  const posts: unknown[] = [];
  const partyPosts: unknown[] = [];
  server.use(
    http.get(apiUrl('/rates/:date'), () => HttpResponse.json({ date: today, saleRate: 3650, purchaseRate: null, updatedAt: `${today}T07:00:00Z` })),
    http.get(apiUrl('/treasury-accounts'), () => HttpResponse.json(accounts)),
    http.get(apiUrl('/parties'), () => HttpResponse.json({ items: [found], count: 1, totalCount: 1, totals: null })),
    http.post(apiUrl('/parties'), async ({ request }) => {
      partyPosts.push(await request.json());
      return HttpResponse.json({ id: 'party-new' }, { status: 201 });
    }),
    http.post(apiUrl('/transfers'), async ({ request }) => {
      posts.push(await request.json());
      return HttpResponse.json(
        {
          id: 'tr-1',
          reference: 'TR-NKC-20261022-000044',
          withdrawalCode: '123456',
          amountXof: 1250000,
          amountMro: 912500,
          saleRate: 3650,
          creditAmount: 112500,
          replayed: false,
          ...created,
        },
        { status: 201 },
      );
    }),
  );
  return { posts, partyPosts };
}

/** Étape 1 remplie en MRO (912 500 MRO = 1 250 000 FCFA au taux de 3 650), on arrive au paiement. */
async function toPaymentStep(user: UserEvent) {
  await user.type(await screen.findByLabelText(/Montant payé en MRO/), '912500');
  await screen.findByText('1 250 000 FCFA');
  await user.click(screen.getByRole('button', { name: 'Continuer' }));
  return screen.findByLabelText('Montant de la ligne 1');
}

async function chooseParty(user: UserEvent) {
  await user.type(screen.getByRole('searchbox'), 'Bout');
  await user.click(await screen.findByRole('button', { name: /Boutique Test/ }));
}

describe('Création d’un transfert — crédit', () => {
  beforeEach(() => signedInAs(agentAccount));

  it('crédit partiel : la part non payée va sur le tiers, avec son solde et son plafond', async () => {
    const { posts } = creditServer(party(1200000, 1500000));
    const { user } = renderApp('/transfers/new');
    const firstAmount = await toPaymentStep(user);

    await user.clear(firstAmount);
    await user.type(firstAmount, '800000');
    await user.click(screen.getByRole('switch', { name: 'Activer le crédit' }));
    expect(screen.getByText('Part à crédit')).toBeInTheDocument();
    await chooseParty(user);

    expect(
      await screen.findByText(/Part à crédit : 112 500 MRO, ajoutée à ce que Boutique Test nous doit\. Solde actuel dans cette agence : il nous doit 1 200 000 MRO\. Plafond de crédit : 1 500 000 MRO\./),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Continuer' }));
    await user.type(await screen.findByLabelText('Expéditeur'), 'E');
    await user.type(screen.getByLabelText('Bénéficiaire'), 'B');
    expect(screen.getByRole('region', { name: 'Récapitulatif' })).toHaveTextContent('À crédit · Boutique Test112 500 MRO');
    await user.click(screen.getByRole('button', { name: 'Créer le transfert' }));

    expect(await screen.findByRole('heading', { name: 'Transfert enregistré' })).toBeInTheDocument();
    expect(posts[0]).toMatchObject({
      payments: [{ treasuryAccountId: 'acc-cash', amount: 800000 }],
      creditPartyId: 'party-1',
    });
    expect(screen.getByText('À crédit · Boutique Test')).toBeInTheDocument();
  });

  it('crédit total : la répartition peut être vide', async () => {
    const { posts } = creditServer(party(0, null));
    const { user } = renderApp('/transfers/new');
    await toPaymentStep(user);

    await user.click(screen.getByRole('button', { name: 'Retirer la ligne 1' }));
    await user.click(screen.getByRole('switch', { name: 'Activer le crédit' }));
    await chooseParty(user);
    expect(await screen.findByText(/Pas de plafond de crédit\./)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Continuer' }));
    await user.type(await screen.findByLabelText('Expéditeur'), 'E');
    await user.type(screen.getByLabelText('Bénéficiaire'), 'B');
    await user.click(screen.getByRole('button', { name: 'Créer le transfert' }));

    await screen.findByRole('heading', { name: 'Transfert enregistré' });
    expect(posts[0]).toMatchObject({ payments: [], creditPartyId: 'party-1' });
  });

  it('prévient quand le crédit dépasserait le plafond du tiers', async () => {
    creditServer(party(1450000, 1500000));
    const { user } = renderApp('/transfers/new');
    const firstAmount = await toPaymentStep(user);

    await user.clear(firstAmount);
    await user.type(firstAmount, '800000');
    await user.click(screen.getByRole('switch', { name: 'Activer le crédit' }));
    await chooseParty(user);

    expect(await screen.findByText(/Ce crédit dépasserait le plafond du tiers \(1 500 000 MRO\)/)).toBeInTheDocument();
  });

  it('exige un tiers quand le crédit est activé, et refuse un crédit inutile', async () => {
    creditServer(party(0, null));
    const { user } = renderApp('/transfers/new');
    await toPaymentStep(user);

    await user.click(screen.getByRole('switch', { name: 'Activer le crédit' }));
    await user.click(screen.getByRole('button', { name: 'Continuer' }));

    expect(await screen.findByText('Tout est payé : rien à mettre à crédit.')).toBeInTheDocument();
    expect(screen.getByText('Choisissez le tiers qui porte le crédit.')).toBeInTheDocument();
  });

  it('crée un tiers depuis le crédit, puis le choisit directement', async () => {
    const { partyPosts } = creditServer(party(0, null));
    const { user } = renderApp('/transfers/new');
    const firstAmount = await toPaymentStep(user);
    await user.clear(firstAmount);
    await user.type(firstAmount, '800000');
    await user.click(screen.getByRole('switch', { name: 'Activer le crédit' }));
    await user.type(screen.getByRole('searchbox'), 'Client Neuf');

    await user.click(screen.getByRole('button', { name: 'Créer un tiers' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nouveau tiers' });
    expect(within(dialog).getByLabelText('Nom (30 caractères au plus)')).toHaveValue('Client Neuf');
    await user.type(within(dialog).getByLabelText("Téléphone (facultatif, avec l'indicatif)"), '+22220000099');
    await user.click(within(dialog).getByRole('button', { name: 'Créer le tiers' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(partyPosts).toEqual([{ name: 'Client Neuf', type: 'Client', phone: '+22220000099', nni: null, address: null, note: null }]);
    expect(screen.getByText('Client Neuf')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Changer' })).toBeInTheDocument();
  });

  it("n'affiche pas le crédit sans la permission transfers.credit", async () => {
    signedInAs({ ...agentAccount, permissions: agentAccount.permissions.filter((p) => p !== 'transfers.credit') });
    creditServer(party(0, null));
    const { user } = renderApp('/transfers/new');
    await toPaymentStep(user);

    expect(screen.queryByRole('switch', { name: 'Activer le crédit' })).not.toBeInTheDocument();
  });
});

