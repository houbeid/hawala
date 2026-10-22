import { screen, waitFor, within } from '@testing-library/react';
import type { UserEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { TreasuryAccount } from '@/features/treasury/types';
import { agentAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { DeliveredTransfer, Transfer, TransferEvent } from '../types';

const ID = '01900000-0000-7000-8000-0000000000f3';
const dakarAgent = { ...agentAccount, agency: { id: dkr.id, code: 'DKR', name: 'Dakar', color: '#1565C0' } };

const transfer: Transfer = {
  id: ID,
  reference: 'TR-NKC-20261022-000060',
  status: 'NotDelivered',
  direction: 'Received',
  sourceAgencyId: nkc.id,
  sourceCurrency: 'MRO',
  destinationAgencyId: dkr.id,
  destinationCurrency: 'XOF',
  amountXof: 1250000,
  amountMro: 912500,
  saleRate: 3650,
  purchaseRate: null,
  profitMro: null,
  senderInfo: 'Expéditeur Test',
  beneficiaryInfo: 'Bénéficiaire Test',
  note: null,
  operationAt: '2026-10-22T14:32:00Z',
  createdAt: '2026-10-22T14:32:05Z',
  payments: null,
  credit: null,
  deliveryOperationAt: null,
  isCodeLocked: false,
  deliveredWithoutCode: false,
  cancellationOperationAt: null,
  cancellationReason: null,
  version: 1,
};

function accountsWith(balance: number | null): TreasuryAccount[] {
  return [
    { id: 'acc-dkr-cash', name: 'Caisse Dakar', type: 'Cash', agencyId: dkr.id, currency: 'XOF', isActive: true, balance },
    { id: 'acc-wave', name: 'Wave', type: 'Wallet', agencyId: dkr.id, currency: 'XOF', isActive: true, balance },
  ];
}

const delivered: DeliveredTransfer = {
  id: ID,
  reference: 'TR-NKC-20261022-000060',
  amountDelivered: 1250000,
  currency: 'XOF',
  deliveryOperationAt: '2026-10-22T15:10:00Z',
  negativeBalances: [],
};

function deliveryServer(options: { balance?: number | null; history?: TransferEvent[]; deliver?: () => Response; current?: Transfer } = {}) {
  const posts: { path: string; body: unknown }[] = [];
  server.use(
    http.get(apiUrl(`/transfers/${ID}`), () => HttpResponse.json(options.current ?? transfer)),
    http.get(apiUrl(`/transfers/${ID}/history`), () => HttpResponse.json(options.history ?? [])),
    http.get(apiUrl('/treasury-accounts'), () => HttpResponse.json(accountsWith(options.balance === undefined ? 5000000 : options.balance))),
    http.post(apiUrl(`/transfers/${ID}/deliver`), async ({ request }) => {
      posts.push({ path: 'deliver', body: await request.json() });
      return options.deliver ? options.deliver() : HttpResponse.json(delivered);
    }),
    http.post(apiUrl(`/transfers/${ID}/deliver-without-code`), async ({ request }) => {
      posts.push({ path: 'withoutCode', body: await request.json() });
      return HttpResponse.json(delivered);
    }),
  );
  return posts;
}

async function enterCode(user: UserEvent, code = '482917') {
  await user.type(await screen.findByLabelText('Code de retrait (6 chiffres)'), code);
  await user.click(screen.getByRole('button', { name: 'Continuer' }));
}

describe('Livraison — agent de l’agence de destination', () => {
  beforeEach(() => signedInAs(dakarAgent));

  it('depuis la fiche : code, remise pré-remplie sur la caisse espèces, livraison confirmée', async () => {
    const posts = deliveryServer();
    const { user, router } = renderApp(`/transfers/${ID}`);

    await user.click(await screen.findByRole('link', { name: 'Livrer' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/transfers/${ID}/deliver`));
    expect(await screen.findByText('5 essais restants sur 5')).toBeInTheDocument();
    await enterCode(user);

    expect(await screen.findByLabelText('Compte 1')).toHaveValue('acc-dkr-cash');
    expect(screen.getByLabelText('Montant de la ligne 1')).toHaveValue('1250000');
    expect(screen.getAllByText('5 000 000 FCFA').length).toBeGreaterThan(0); // solde de la caisse
    await user.click(screen.getByRole('button', { name: 'Confirmer la livraison' }));

    expect(await screen.findByRole('heading', { name: 'Transfert livré' })).toBeInTheDocument();
    expect(screen.getByText('Remis à Bénéficiaire Test')).toBeInTheDocument();
    expect(posts).toEqual([
      { path: 'deliver', body: { code: '482917', operationAt: null, payments: [{ treasuryAccountId: 'acc-dkr-cash', amount: 1250000 }], note: null } },
    ]);
  });

  it('vérifie le format du code avant de continuer', async () => {
    deliveryServer();
    const { user } = renderApp(`/transfers/${ID}/deliver`);

    await enterCode(user, '4829');

    expect(await screen.findByText('Saisissez les 6 chiffres du code.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Compte 1')).not.toBeInTheDocument();
  });

  it('un code faux ramène au code, efface la saisie et dit les essais restants', async () => {
    deliveryServer({
      deliver: () => HttpResponse.json({ code: 'Transfers.Delivery.WrongCode', remainingAttempts: 3 }, { status: 400 }),
    });
    const { user } = renderApp(`/transfers/${ID}/deliver`);

    await enterCode(user, '111111');
    await user.click(await screen.findByRole('button', { name: 'Confirmer la livraison' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Code incorrect. Il vous reste 3 essais avant le blocage du transfert.');
    expect(screen.getByText('3 essais restants sur 5')).toBeInTheDocument();
    expect(screen.getByLabelText('Code de retrait (6 chiffres)')).toHaveValue('');
  });

  it('au 5e code faux, le transfert est bloqué : plus de saisie', async () => {
    deliveryServer({
      deliver: () => HttpResponse.json({ code: 'Transfers.Delivery.CodeLocked', remainingAttempts: 0 }, { status: 409 }),
    });
    const { user } = renderApp(`/transfers/${ID}/deliver`);

    await enterCode(user, '111111');
    await user.click(await screen.findByRole('button', { name: 'Confirmer la livraison' }));

    expect(await screen.findByText(/Transfert bloqué après 5 codes faux/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Code de retrait (6 chiffres)')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Continuer' })).not.toBeInTheDocument();
  });

  it('compte les codes faux déjà saisis d’après l’historique', async () => {
    deliveryServer({
      history: [
        { type: 'WrongCode', at: '2026-10-22T15:00:00Z', authorId: 'a', authorName: 'A', reason: null, changes: [{ field: 'FailedCodeAttempts', before: '1', after: '2' }] },
      ],
    });
    renderApp(`/transfers/${ID}/deliver`);

    expect(await screen.findByText('3 essais restants sur 5')).toBeInTheDocument();
  });

  it('prévient sans bloquer quand une caisse passe en négatif', async () => {
    const posts = deliveryServer({
      balance: 800000,
      deliver: () =>
        HttpResponse.json({
          ...delivered,
          negativeBalances: [{ treasuryAccountId: 'acc-dkr-cash', name: 'Caisse Dakar', balance: -450000, currency: 'XOF' }],
        }),
    });
    const { user } = renderApp(`/transfers/${ID}/deliver`);

    await enterCode(user);
    expect(await screen.findByText(/Caisse Dakar passera en négatif : − 450 000 FCFA\. Vous pouvez continuer\./)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Confirmer quand même' }));

    expect(await screen.findByText(/Caisse Dakar est maintenant en négatif : − 450 000 FCFA/)).toBeInTheDocument();
    expect(posts).toHaveLength(1);
  });

  it('exige que le total remis égale la somme à remettre', async () => {
    const posts = deliveryServer();
    const { user } = renderApp(`/transfers/${ID}/deliver`);

    await enterCode(user);
    const line = await screen.findByLabelText('Montant de la ligne 1');
    await user.clear(line);
    await user.type(line, '1000000');
    await user.click(screen.getByRole('button', { name: 'Confirmer la livraison' }));

    expect(await screen.findByText('Le total remis doit être égal à la somme à remettre.')).toBeInTheDocument();
    expect(posts).toEqual([]);
  });

  it('rappelle de ne pas remettre l’argent quand le réseau coupe à la confirmation', async () => {
    deliveryServer({ deliver: () => HttpResponse.error() });
    const { user } = renderApp(`/transfers/${ID}/deliver`);

    await enterCode(user);
    await user.click(await screen.findByRole('button', { name: 'Confirmer la livraison' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/Aucune caisse n'a été modifiée.*ne remettez pas l'argent/);
  });
});

describe('Livraison sans code — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('exige la pièce d’identité et le motif, puis livre, même code bloqué', async () => {
    const posts = deliveryServer({ current: { ...transfer, isCodeLocked: true } });
    const { user } = renderApp(`/transfers/${ID}`);

    await user.click(await screen.findByRole('link', { name: 'Livrer sans code' }));
    expect(screen.queryByRole('link', { name: 'Livrer' })).not.toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: 'Continuer' }));
    expect(await screen.findByText("Saisissez la pièce d'identité du bénéficiaire.")).toBeInTheDocument();
    expect(screen.getByText('Le motif est obligatoire.')).toBeInTheDocument();

    await user.type(screen.getByLabelText("Pièce d'identité du bénéficiaire (type et numéro)"), 'Carte nationale 0000000000');
    await user.type(screen.getByLabelText('Motif (obligatoire)'), "Code perdu par l'expéditeur");
    await user.click(screen.getByRole('button', { name: 'Continuer' }));
    await user.click(await screen.findByRole('button', { name: 'Confirmer la livraison' }));

    expect(await screen.findByRole('heading', { name: 'Transfert livré' })).toBeInTheDocument();
    expect(posts).toEqual([
      {
        path: 'withoutCode',
        body: {
          idDocument: 'Carte nationale 0000000000',
          reason: "Code perdu par l'expéditeur",
          operationAt: null,
          payments: [{ treasuryAccountId: 'acc-dkr-cash', amount: 1250000 }],
          note: null,
        },
      },
    ]);
  });
});

describe('Livraison — droits', () => {
  it('l’agence d’envoi ne livre pas', async () => {
    signedInAs({ ...agentAccount, permissions: agentAccount.permissions });
    deliveryServer({ current: { ...transfer, direction: 'Sent', payments: [], credit: null } });
    renderApp(`/transfers/${ID}`);

    const region = await screen.findByRole('region', { name: 'Actions' });
    expect(within(region).queryByRole('link', { name: 'Livrer' })).not.toBeInTheDocument();
  });
});
