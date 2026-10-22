import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { TreasuryAccount } from '@/features/treasury/types';
import { agentAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { Transfer } from '../types';

const ID = '01900000-0000-7000-8000-0000000000f2';
const accounts: TreasuryAccount[] = [
  { id: 'acc-cash', name: 'Caisse principale', type: 'Cash', agencyId: nkc.id, currency: 'MRO', isActive: true, balance: null },
];

function transfer(overrides: Partial<Transfer> = {}): Transfer {
  return {
    id: ID,
    reference: 'TR-NKC-20261022-000050',
    status: 'NotDelivered',
    direction: 'Sent',
    sourceAgencyId: nkc.id,
    sourceCurrency: 'MRO',
    destinationAgencyId: dkr.id,
    destinationCurrency: 'XOF',
    amountXof: 1250000,
    amountMro: 912500,
    saleRate: 3650,
    purchaseRate: 3600,
    profitMro: 12500,
    senderInfo: 'Expéditeur Test',
    beneficiaryInfo: 'Bénéficiaire Test',
    note: null,
    operationAt: '2026-10-22T14:32:00Z',
    createdAt: '2026-10-22T14:32:05Z',
    payments: [{ treasuryAccountId: 'acc-cash', treasuryAccountName: 'Caisse principale', amount: 912500, currency: 'MRO' }],
    credit: null,
    deliveryOperationAt: null,
    isCodeLocked: false,
    deliveredWithoutCode: false,
    cancellationOperationAt: null,
    cancellationReason: null,
    version: 3,
    ...overrides,
  };
}

function amountsServer(initial: Transfer, put?: () => Response) {
  const bodies: unknown[] = [];
  server.use(
    http.get(apiUrl(`/transfers/${ID}`), () => HttpResponse.json(initial)),
    http.get(apiUrl(`/transfers/${ID}/history`), () => HttpResponse.json([])),
    http.get(apiUrl('/treasury-accounts'), () => HttpResponse.json(accounts)),
    http.put(apiUrl(`/transfers/${ID}/amounts`), async ({ request }) => {
      bodies.push(await request.json());
      return put ? put() : new HttpResponse(null, { status: 204 });
    }),
  );
  return bodies;
}

const rateField = () => screen.getByLabelText('Prix de vente de ce transfert');

describe('Modifier montant ou taux — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('s’ouvre depuis la fiche, déjà remplie avec l’état actuel', async () => {
    amountsServer(transfer());
    const { user, router } = renderApp(`/transfers/${ID}`);

    await user.click(await screen.findByRole('link', { name: 'Modifier montant ou taux' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/transfers/${ID}/amounts`));
    expect(await screen.findByLabelText('Montant payé en MRO')).toHaveValue('912500');
    expect(rateField()).toHaveValue('3650');
    expect(await screen.findByLabelText('Compte 1')).toHaveValue('acc-cash');
  });

  it('change le prix de vente : le MRO payé est gardé, le FCFA à livrer suit, motif obligatoire', async () => {
    const bodies = amountsServer(transfer());
    const { user, router } = renderApp(`/transfers/${ID}/amounts`);
    await screen.findByLabelText('Montant payé en MRO');

    await user.clear(rateField());
    await user.type(rateField(), '3640');
    const compare = screen.getByRole('region', { name: 'Montant à livrer, avant et après' });
    expect(within(compare).getByText('1 250 000 FCFA')).toBeInTheDocument();
    expect(within(compare).getByText('1 253 434 FCFA')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Enregistrer la modification' }));
    expect(await screen.findByText('Le motif est obligatoire.')).toBeInTheDocument();
    expect(bodies).toEqual([]);

    await user.type(screen.getByLabelText('Motif (obligatoire)'), 'Taux négocié avec le client');
    await user.click(screen.getByRole('button', { name: 'Enregistrer la modification' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/transfers/${ID}`));
    expect(bodies).toEqual([
      {
        version: 3,
        amountXof: 1253434,
        saleRate: 3640,
        operationAt: null,
        payments: [{ treasuryAccountId: 'acc-cash', amount: 912500 }],
        creditPartyId: null,
        reason: 'Taux négocié avec le client',
      },
    ]);
  });

  it('en FCFA, le montant à payer change et la répartition doit suivre', async () => {
    const bodies = amountsServer(transfer());
    const { user } = renderApp(`/transfers/${ID}/amounts`);
    await screen.findByLabelText('Montant payé en MRO');

    await user.click(within(screen.getByRole('group', { name: 'Saisir en :' })).getByRole('button', { name: 'FCFA' }));
    const xof = screen.getByLabelText('Montant en FCFA');
    await user.clear(xof);
    await user.type(xof, '1250000');
    await user.clear(rateField());
    await user.type(rateField(), '3640');
    await user.type(screen.getByLabelText('Motif (obligatoire)'), 'Taux négocié');
    await user.click(screen.getByRole('button', { name: 'Enregistrer la modification' }));

    expect(await screen.findByText('Le total réparti dépasse le montant à payer.')).toBeInTheDocument();
    const line = screen.getByLabelText('Montant de la ligne 1');
    await user.clear(line);
    await user.type(line, '910000');
    await user.click(screen.getByRole('button', { name: 'Enregistrer la modification' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ amountXof: null, saleRate: 3640, payments: [{ treasuryAccountId: 'acc-cash', amount: 910000 }] });
  });

  it('garde le crédit en cours et son tiers', async () => {
    const bodies = amountsServer(
      transfer({
        payments: [{ treasuryAccountId: 'acc-cash', treasuryAccountName: 'Caisse principale', amount: 800000, currency: 'MRO' }],
        credit: { partyId: 'party-1', partyName: 'Boutique Test', amount: 112500, currency: 'MRO' },
      }),
    );
    const { user } = renderApp(`/transfers/${ID}/amounts`);

    expect(await screen.findByRole('switch', { name: 'Activer le crédit' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByText('Boutique Test')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Motif (obligatoire)'), 'Correction de la date');
    await user.click(screen.getByRole('button', { name: 'Enregistrer la modification' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ creditPartyId: 'party-1', payments: [{ treasuryAccountId: 'acc-cash', amount: 800000 }] });
  });

  it('propose de recharger quand la fiche a changé entre-temps', async () => {
    amountsServer(transfer(), () => HttpResponse.json({ code: 'Transfers.Transfer.VersionConflict' }, { status: 409 }));
    const { user } = renderApp(`/transfers/${ID}/amounts`);
    await screen.findByLabelText('Montant payé en MRO');

    await user.type(screen.getByLabelText('Motif (obligatoire)'), 'Motif');
    await user.click(screen.getByRole('button', { name: 'Enregistrer la modification' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/vient d'être modifié par un autre utilisateur/);
    expect(screen.getByRole('link', { name: 'Recharger le transfert' })).toBeInTheDocument();
  });
});

describe('Modifier montant ou taux — droits', () => {
  it('un agent n’a ni le bouton ni l’écran', async () => {
    signedInAs(agentAccount);
    amountsServer(transfer({ purchaseRate: null, profitMro: null }));
    const { router } = renderApp(`/transfers/${ID}`);

    await screen.findByRole('region', { name: 'Actions' });
    expect(screen.queryByRole('link', { name: 'Modifier montant ou taux' })).not.toBeInTheDocument();

    await router.navigate({ to: '/transfers/$transferId/amounts', params: { transferId: ID } });
    expect(await screen.findByText("Votre compte n'a pas accès à cet écran.")).toBeInTheDocument();
  });
});
