import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { CreateTransferRequest, TransferList } from '@/features/transfers/types';
import { createMemoryStore } from '@/shared/offline/offlineStore';
import { agentAccount, dkr } from '@/test/fixtures';
import { setNetwork } from '@/test/network';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';

// File des créations hors ligne, toute l'application (décisions du 24 octobre 2026) : un refus du serveur à l'envoi
// laisse l'opération « rejetée » avec son motif, « Corriger » rouvre le formulaire rempli, « Abandonner » la retire après
// confirmation ; se déconnecter avec des opérations en attente prévient d'abord. Données fictives uniquement.

const request: CreateTransferRequest = {
  sourceAgencyId: null,
  destinationAgencyId: dkr.id,
  amountXof: 1_250_000,
  senderInfo: 'Expéditeur Fictif',
  beneficiaryInfo: 'Bénéficiaire Fictif',
  note: null,
  operationAt: '2026-10-24T09:30:00Z',
  payments: [{ treasuryAccountId: 'acc-cash', amount: 912_500 }],
  creditPartyId: null,
};

function queueStore(status: 'pending' | 'rejected' = 'pending') {
  return createMemoryStore({
    operations: [
      {
        id: '0192f000-0000-7000-8000-000000000001',
        accountId: agentAccount.id,
        kind: 'transfer',
        request,
        label: 'Expéditeur Fictif → Bénéficiaire Fictif',
        enteredAt: '2026-10-24T09:30:00Z',
        status,
        error: null,
        result: null,
      },
    ],
  });
}

const emptyList: TransferList = { items: [], page: 1, pageSize: 50, totalCount: 0, allCount: 0 };

function listServer() {
  server.use(
    http.get(apiUrl('/transfers'), () => HttpResponse.json(emptyList)),
    http.get(apiUrl('/transfers/summary'), () => HttpResponse.json({ code: 'x' }, { status: 404 })),
  );
}

describe('File des créations hors ligne', () => {
  beforeEach(() => signedInAs(agentAccount));

  it('un refus du serveur à l’envoi : « Rejeté » avec le motif, puis « Abandonner » après confirmation', async () => {
    listServer();
    const posts: string[] = [];
    server.use(
      http.post(apiUrl('/transfers'), ({ request: req }) => {
        posts.push(req.headers.get('Idempotency-Key') ?? '');
        return HttpResponse.json(
          { status: 409, code: 'Transfers.Limits.PerTransfer', remaining: 500000, currency: 'MRO' },
          { status: 409 },
        );
      }),
    );
    const { user } = renderApp('/transfers', { queueStore: queueStore() });

    const queued = await screen.findByRole('region', { name: 'Transferts saisis hors ligne' });
    expect(await within(queued).findByText('Rejeté')).toBeInTheDocument();
    expect(posts).toEqual(['0192f000-0000-7000-8000-000000000001']);
    expect(within(queued).getByRole('link', { name: 'Corriger' })).toHaveAttribute(
      'href',
      '/transfers/new?fromQueue=0192f000-0000-7000-8000-000000000001',
    );
    expect(screen.getByText('1 opération rejetée')).toBeInTheDocument();

    await user.click(within(queued).getByRole('button', { name: 'Abandonner' }));
    const sheet = await screen.findByRole('dialog', { name: 'Abandonner cette opération ?' });
    await user.click(within(sheet).getByRole('button', { name: 'Abandonner' }));

    await waitFor(() => expect(screen.queryByRole('region', { name: 'Transferts saisis hors ligne' })).not.toBeInTheDocument());
  });

  it('« Corriger » rouvre le formulaire rempli, avec le motif du rejet', async () => {
    listServer();
    server.use(
      http.post(apiUrl('/transfers'), () => HttpResponse.json({ status: 409, code: 'Transfers.Transfer.RateNotSet' }, { status: 409 })),
      http.get(apiUrl('/rates/:date'), ({ params }) =>
        HttpResponse.json({ date: params.date, saleRate: 3650, purchaseRate: null, updatedAt: '2026-10-24T07:00:00Z' }),
      ),
      http.get(apiUrl('/treasury-accounts'), () => HttpResponse.json([])),
    );
    const { user } = renderApp('/transfers', { queueStore: queueStore() });

    const queued = await screen.findByRole('region', { name: 'Transferts saisis hors ligne' });
    await user.click(await within(queued).findByRole('link', { name: 'Corriger' }));

    expect(await screen.findByText(/Correction d’une opération rejetée/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Montant en FCFA/)).toHaveValue('1250000');
  });

  it('se déconnecter avec des opérations en attente prévient d’abord ; elles restent sur le téléphone', async () => {
    setNetwork(false);
    listServer();
    const store = queueStore();
    const { user } = renderApp('/more', { queueStore: store });

    const signOut = await screen.findAllByRole('button', { name: 'Se déconnecter' });
    await user.click(signOut[signOut.length - 1]!);

    const sheet = await screen.findByRole('dialog', { name: 'Opérations en attente' });
    expect(within(sheet).getByText(/1 opération saisie hors ligne reste sur ce téléphone/)).toBeInTheDocument();
    await user.click(within(sheet).getByRole('button', { name: 'Rester connecté' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect((store.data.get('operations') as unknown[]).length).toBe(1);
    setNetwork(true);
  });
});
