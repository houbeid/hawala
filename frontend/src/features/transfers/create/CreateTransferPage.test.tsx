import { screen, waitFor, within } from '@testing-library/react';
import type { UserEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { TreasuryAccount } from '@/features/treasury/types';
import { todayIso } from '@/shared/format/dates';
import { agentAccount, dkr, nkc, readOnlyAccount, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { stubDownloads } from '@/test/downloads';
import { setNetwork } from '@/test/network';
import { apiUrl, server } from '@/test/server';
import type { CreatedTransfer } from '../types';

const today = todayIso();

const nkcAccounts: TreasuryAccount[] = [
  { id: 'acc-cash', name: 'Caisse principale', type: 'Cash', agencyId: nkc.id, currency: 'MRO', isActive: true, balance: null },
  { id: 'acc-bankily', name: 'Bankily', type: 'Wallet', agencyId: nkc.id, currency: 'MRO', isActive: true, balance: null },
];
const dkrAccounts: TreasuryAccount[] = [
  { id: 'acc-dkr-cash', name: 'Caisse Dakar', type: 'Cash', agencyId: dkr.id, currency: 'XOF', isActive: true, balance: null },
];

const created: CreatedTransfer = {
  id: 'tr-1',
  reference: 'TR-NKC-20261022-000043',
  withdrawalCode: '482917',
  amountXof: 1250000,
  amountMro: 912500,
  saleRate: 3650,
  creditAmount: 0,
  replayed: false,
};

/** Serveur de la création : taux du jour, comptes, et POST /transfers qui enregistre ce qu'il reçoit. */
function creationServer(options: { rate?: boolean; saleRate?: number; post?: () => Response } = {}) {
  const posts: { body: unknown; key: string | null }[] = [];
  const receipts: { id: unknown; body: unknown }[] = [];
  const accountQueries: (string | null)[] = [];
  server.use(
    http.get(apiUrl('/rates/:date'), () =>
      options.rate === false
        ? HttpResponse.json({ code: 'Rates.Rate.NotFound' }, { status: 404 })
        : HttpResponse.json({ date: today, saleRate: options.saleRate ?? 3650, purchaseRate: null, updatedAt: `${today}T07:00:00Z` }),
    ),
    http.get(apiUrl('/treasury-accounts'), ({ request }) => {
      const agencyId = new URL(request.url).searchParams.get('agencyId');
      accountQueries.push(agencyId);
      return HttpResponse.json(agencyId === dkr.id ? dkrAccounts : nkcAccounts);
    }),
    http.post(apiUrl('/transfers'), async ({ request }) => {
      posts.push({ body: await request.json(), key: request.headers.get('Idempotency-Key') });
      return options.post ? options.post() : HttpResponse.json(created, { status: 201 });
    }),
    http.post(apiUrl('/transfers/:id/receipt'), async ({ params, request }) => {
      receipts.push({ id: params.id, body: await request.json() });
      return new HttpResponse(new Uint8Array([0x25, 0x50, 0x44, 0x46]), { headers: { 'Content-Type': 'application/pdf' } });
    }),
  );
  return { posts, receipts, accountQueries };
}

const mroField = () => screen.getByLabelText(/Montant payé en MRO/);

/** À Nouakchott, l'agent saisit en MRO par défaut ; au taux de 3 650, 912 500 MRO = 1 250 000 FCFA. */
async function fillAmount(user: UserEvent, amount = '912 500') {
  await user.type(await screen.findByLabelText(/Montant payé en MRO|Montant en FCFA/), amount);
}

describe('Création d’un transfert — Agent de Nouakchott', () => {
  beforeEach(() => signedInAs(agentAccount));

  it('saisit en MRO à Nouakchott et calcule le FCFA en direct, avec le taux du jour verrouillé', async () => {
    creationServer();
    const { user } = renderApp('/transfers/new');

    await fillAmount(user);

    expect(await screen.findByText('1 250 000 FCFA')).toBeInTheDocument();
    expect(screen.getByText('912 500 × 5 000 ÷ 3 650, arrondi au FCFA')).toBeInTheDocument();
    expect(screen.getByText('5 000 FCFA = 3 650 MRO')).toBeInTheDocument();
    expect(screen.getByText('Verrouillée sur votre agence.')).toBeInTheDocument();
    expect(mroField()).toHaveValue('912 500');
    expect(within(screen.getByRole('group', { name: 'Saisir en :' })).getByRole('button', { name: 'MRO' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  it('au taux de 3 700, 912 500 MRO saisis donnent 1 233 108 FCFA et un paiement de 912 500 MRO', async () => {
    const { posts } = creationServer({ saleRate: 3700 });
    const { user } = renderApp('/transfers/new');

    await fillAmount(user);
    expect(await screen.findByText('1 233 108 FCFA')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Continuer' }));
    expect(await screen.findByLabelText('Montant de la ligne 1')).toHaveValue('912500');
    await user.click(screen.getByRole('button', { name: 'Continuer' }));
    await user.type(await screen.findByLabelText('Expéditeur'), 'E');
    await user.type(screen.getByLabelText('Bénéficiaire'), 'B');
    await user.click(screen.getByRole('button', { name: 'Créer le transfert' }));

    await screen.findByRole('heading', { name: 'Transfert enregistré' });
    expect(posts[0]!.body).toMatchObject({ amountXof: 1233108, payments: [{ treasuryAccountId: 'acc-cash', amount: 912500 }] });
  });

  it('permet de saisir en FCFA avec la bascule, le MRO étant alors calculé', async () => {
    const { posts } = creationServer();
    const { user } = renderApp('/transfers/new');
    await screen.findByLabelText(/Montant payé en MRO/);

    await user.click(within(screen.getByRole('group', { name: 'Saisir en :' })).getByRole('button', { name: 'FCFA' }));
    await user.type(screen.getByLabelText(/Montant en FCFA/), '1 250 000');

    expect(await screen.findByText('912 500 MRO')).toBeInTheDocument();
    expect(screen.getByText('1 250 000 × 3 650 ÷ 5 000, arrondi à l’unité')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Continuer' }));
    await user.click(await screen.findByRole('button', { name: 'Continuer' }));
    await user.type(await screen.findByLabelText('Expéditeur'), 'E');
    await user.type(screen.getByLabelText('Bénéficiaire'), 'B');
    await user.click(screen.getByRole('button', { name: 'Créer le transfert' }));
    await screen.findByRole('heading', { name: 'Transfert enregistré' });
    expect(posts[0]!.body).toMatchObject({ amountXof: 1250000, payments: [{ treasuryAccountId: 'acc-cash', amount: 912500 }] });
  });

  it('crée un transfert réparti sur deux comptes, dont un saisi en MRU, et affiche le code une seule fois', async () => {
    const { posts, receipts } = creationServer();
    const { user } = renderApp('/transfers/new');

    await fillAmount(user);
    await screen.findByText('1 250 000 FCFA');
    await user.click(screen.getByRole('button', { name: 'Continuer' }));

    // Étape 2 : une ligne pré-remplie sur la caisse espèces, du montant total.
    const firstAmount = await screen.findByLabelText('Montant de la ligne 1');
    expect(firstAmount).toHaveValue('912500');
    expect(screen.getByLabelText('Compte 1')).toHaveValue('acc-cash');
    await user.clear(firstAmount);
    await user.type(firstAmount, '612500');
    await user.click(screen.getByRole('button', { name: 'Ajouter un compte' }));
    await user.selectOptions(screen.getByLabelText('Compte 2'), 'Bankily');
    await user.type(screen.getByLabelText('Montant de la ligne 2'), '30 000');
    const units = screen.getAllByRole('group', { name: 'Unité de saisie' });
    await user.click(within(units[1]!).getByRole('button', { name: 'MRU' }));
    expect(screen.getByText('= 300 000 MRO')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Continuer' }));

    // Étape 3 : personnes et récapitulatif.
    await user.type(await screen.findByLabelText('Expéditeur'), 'Expéditeur Test');
    await user.type(screen.getByLabelText('Bénéficiaire'), 'Bénéficiaire Test');
    expect(screen.getByRole('region', { name: 'Récapitulatif' })).toHaveTextContent('À livrer · Dakar');
    await user.click(screen.getByRole('button', { name: 'Créer le transfert' }));

    expect(await screen.findByRole('heading', { name: 'Transfert enregistré' })).toBeInTheDocument();
    expect(screen.getByText('TR-NKC-20261022-000043')).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Code de retrait' })).toBeInTheDocument();
    expect(screen.getByText('482917')).toHaveClass('sr-only');
    expect(screen.getByText(/Ce code ne sera plus jamais affiché/)).toBeInTheDocument();

    // Pas d'imprimante : le reçu PDF, qui porte le code, est envoyé à l'expéditeur.
    expect(screen.queryByRole('button', { name: 'Imprimer' })).not.toBeInTheDocument();
    const downloads = stubDownloads();
    await user.click(screen.getByRole('button', { name: 'Envoyer le reçu avec le code' }));
    expect(await screen.findByText('Reçu téléchargé : envoyez-le au client.')).toBeInTheDocument();
    expect(receipts).toEqual([{ id: created.id, body: { code: '482917' } }]);
    expect(downloads.files).toEqual(['Recu-TR-NKC-20261022-000043.pdf']);
    downloads.restore();

    expect(posts).toHaveLength(1);
    expect(posts[0]!.key).toMatch(/^[0-9a-f-]{36}$/);
    expect(posts[0]!.body).toEqual({
      sourceAgencyId: nkc.id,
      destinationAgencyId: dkr.id,
      amountXof: 1250000,
      senderInfo: 'Expéditeur Test',
      beneficiaryInfo: 'Bénéficiaire Test',
      note: null,
      operationAt: null,
      payments: [
        { treasuryAccountId: 'acc-cash', amount: 612500 },
        { treasuryAccountId: 'acc-bankily', amount: 300000 },
      ],
      creditPartyId: null,
    });
  });

  it('bloque l’étape 1 sans taux du jour', async () => {
    creationServer({ rate: false });
    renderApp('/transfers/new');

    expect(await screen.findByText(/Le taux du jour n'est pas encore fixé pour cette date/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continuer' })).toBeDisabled();
  });

  it('refuse de continuer tant que la répartition ne fait pas le total', async () => {
    creationServer();
    const { user } = renderApp('/transfers/new');
    await fillAmount(user);
    await screen.findByText('1 250 000 FCFA');
    await user.click(screen.getByRole('button', { name: 'Continuer' }));

    const firstAmount = await screen.findByLabelText('Montant de la ligne 1');
    await user.clear(firstAmount);
    await user.type(firstAmount, '800000');
    expect(screen.getByText('112 500 MRO')).toBeInTheDocument(); // reste non payé
    await user.click(screen.getByRole('button', { name: 'Continuer' }));

    expect(await screen.findByText('Le total réparti doit être égal au montant à payer.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Expéditeur')).not.toBeInTheDocument();
  });

  it('ramène à l’étape 1 avec le montant restant quand le plafond du compte est dépassé', async () => {
    creationServer({
      post: () =>
        HttpResponse.json({ code: 'Transfers.Limits.PerDayExceeded', remaining: 250000, currency: 'MRO' }, { status: 409 }),
    });
    const { user } = renderApp('/transfers/new');
    await fillAmount(user);
    await screen.findByText('1 250 000 FCFA');
    await user.click(screen.getByRole('button', { name: 'Continuer' }));
    await user.click(await screen.findByRole('button', { name: 'Continuer' }));
    await user.type(await screen.findByLabelText('Expéditeur'), 'E');
    await user.type(screen.getByLabelText('Bénéficiaire'), 'B');
    await user.click(screen.getByRole('button', { name: 'Créer le transfert' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Plafond par jour de votre compte dépassé : il reste 250 000 MRO.');
    expect(mroField()).toBeInTheDocument();
  });

  it('hors ligne : gardé sur le téléphone, envoyé au retour du réseau avec sa clé et l’heure de la saisie, puis le code', async () => {
    const { posts } = creationServer();
    server.use(
      http.get(apiUrl('/transfers'), () => HttpResponse.json({ items: [], page: 1, pageSize: 50, totalCount: 0, allCount: 0 })),
      http.get(apiUrl('/transfers/summary'), () => HttpResponse.json({
        agencyId: nkc.id, currency: 'MRO', otherAgencyId: dkr.id, otherCurrency: 'XOF', from: today, to: today,
        incoming: { amount: 0, count: 0 }, outgoing: { amount: 0, count: 0 }, sentByOther: null, deliveredByOther: null,
        notDelivered: { amount: 0, count: 0 }, profitMro: null, saleRate: 3650, purchaseRate: null,
      })),
    );
    const { user } = renderApp('/transfers/new');

    await fillAmount(user);
    await screen.findByText('1 250 000 FCFA');
    await user.click(screen.getByRole('button', { name: 'Continuer' }));
    await screen.findByLabelText('Montant de la ligne 1');
    await user.click(screen.getByRole('button', { name: 'Continuer' }));
    await user.type(await screen.findByLabelText('Expéditeur'), 'Expéditeur Test');
    await user.type(screen.getByLabelText('Bénéficiaire'), 'Bénéficiaire Test');

    setNetwork(false);
    expect(screen.getByText(/le transfert sera gardé sur ce téléphone/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Enregistrer sur le téléphone' }));

    expect(await screen.findByRole('heading', { name: 'Transfert gardé sur le téléphone' })).toBeInTheDocument();
    expect(posts).toHaveLength(0);

    await user.click(screen.getByRole('button', { name: 'Terminé' }));
    const queued = await screen.findByRole('region', { name: 'Transferts saisis hors ligne' });
    expect(within(queued).getByText('En attente d’envoi')).toBeInTheDocument();
    expect(within(queued).getByText('Expéditeur Test → Bénéficiaire Test')).toBeInTheDocument();
    expect(screen.getByText('1 opération en attente d’envoi')).toBeInTheDocument();

    setNetwork(true);

    expect(await within(queued).findByText('Envoyé')).toBeInTheDocument();
    expect(posts).toHaveLength(1);
    expect(posts[0]!.key).toMatch(/^[0-9a-f-]{36}$/);
    expect((posts[0]!.body as { operationAt: string | null }).operationAt).not.toBeNull();
    expect(within(queued).getByRole('group', { name: 'Code de retrait' })).toBeInTheDocument();
    expect(within(queued).getByRole('button', { name: 'Envoyer le reçu avec le code' })).toBeInTheDocument();

    await user.click(within(queued).getByRole('button', { name: 'Terminé' }));
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Transferts saisis hors ligne' })).not.toBeInTheDocument());
  });

  it('réutilise la même clé d’idempotence quand l’agent renvoie après une coupure réseau', async () => {
    let attempt = 0;
    const { posts } = creationServer({
      post: () => {
        attempt += 1;
        return attempt === 1 ? HttpResponse.error() : HttpResponse.json(created, { status: 201 });
      },
    });
    const { user } = renderApp('/transfers/new');
    await fillAmount(user);
    await screen.findByText('1 250 000 FCFA');
    await user.click(screen.getByRole('button', { name: 'Continuer' }));
    await user.click(await screen.findByRole('button', { name: 'Continuer' }));
    await user.type(await screen.findByLabelText('Expéditeur'), 'E');
    await user.type(screen.getByLabelText('Bénéficiaire'), 'B');

    await user.click(screen.getByRole('button', { name: 'Créer le transfert' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Pas de connexion au serveur');
    await user.click(screen.getByRole('button', { name: 'Créer le transfert' }));

    expect(await screen.findByRole('heading', { name: 'Transfert enregistré' })).toBeInTheDocument();
    expect(posts).toHaveLength(2);
    expect(posts[1]!.key).toBe(posts[0]!.key);
  });
});

describe('Création d’un transfert — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('choisit l’agence d’envoi : depuis Dakar, la répartition est en FCFA sur les comptes de Dakar', async () => {
    const { accountQueries } = creationServer();
    const { user } = renderApp('/transfers/new');

    const source = await screen.findByLabelText("Agence d'envoi");
    await waitFor(() => expect(source).toHaveValue(nkc.id));
    await user.selectOptions(source, 'DKR · Dakar');
    await fillAmount(user, '400 000');
    await user.click(screen.getByRole('button', { name: 'Continuer' }));

    // Total à payer et total réparti (ligne pré-remplie) : les deux en FCFA.
    await waitFor(() => expect(screen.getAllByText('400 000 FCFA')).toHaveLength(2));
    expect(screen.getByText('Total à payer')).toBeInTheDocument();
    expect(screen.getByLabelText('Compte 1')).toHaveValue('acc-dkr-cash');
    expect(accountQueries).toContain(dkr.id);
  });
});

describe('Création d’un transfert — droits', () => {
  it('n’est pas accessible sans transfers.create', async () => {
    signedInAs(readOnlyAccount);
    server.use(http.get(apiUrl('/transfers'), () => HttpResponse.json({ items: [], page: 1, pageSize: 50, totalCount: 0, allCount: 0 })));
    renderApp('/transfers/new');

    expect(await screen.findByText("Votre compte n'a pas accès à cet écran.")).toBeInTheDocument();
  });
});
