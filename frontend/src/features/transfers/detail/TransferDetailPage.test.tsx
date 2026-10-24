import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { agentAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { stubDownloads } from '@/test/downloads';
import { apiUrl, server } from '@/test/server';
import type { Transfer, TransferEvent } from '../types';

const ID = '01900000-0000-7000-8000-0000000000f1';

function transfer(overrides: Partial<Transfer> = {}): Transfer {
  return {
    id: ID,
    reference: 'TR-NKC-20261022-000043',
    status: 'NotDelivered',
    direction: 'Sent',
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
    note: 'Frais de scolarité',
    operationAt: '2026-10-22T14:32:00Z',
    createdAt: '2026-10-22T14:32:05Z',
    payments: [
      { treasuryAccountId: 'acc-cash', treasuryAccountName: 'Caisse principale', amount: 500000, currency: 'MRO' },
      { treasuryAccountId: 'acc-bankily', treasuryAccountName: 'Bankily', amount: 300000, currency: 'MRO' },
    ],
    credit: { partyId: 'p1', partyName: 'Boutique Test', amount: 112500, currency: 'MRO' },
    deliveryOperationAt: null,
    isCodeLocked: false,
    deliveredWithoutCode: false,
    cancellationOperationAt: null,
    cancellationReason: null,
    version: 3,
    ...overrides,
  };
}

const history: TransferEvent[] = [
  { type: 'Created', at: '2026-10-22T14:32:05Z', authorId: 'a1', authorName: 'Agent Test Nouakchott', reason: null, changes: [] },
  {
    type: 'Edited',
    at: '2026-10-22T14:40:00Z',
    authorId: 'a1',
    authorName: 'Agent Test Nouakchott',
    reason: 'Faute de frappe',
    changes: [{ field: 'BeneficiaryInfo', before: 'Bénéficiaire', after: 'Bénéficiaire Test' }],
  },
  {
    type: 'WrongCode',
    at: '2026-10-22T15:06:00Z',
    authorId: 'a2',
    authorName: 'Agent Test Dakar',
    reason: null,
    changes: [{ field: 'FailedCodeAttempts', before: '1', after: '2' }],
  },
];

/** Serveur d'un transfert : fiche (modifiable par les actions), historique, et les requêtes reçues. */
function detailServer(initial: Transfer) {
  let current = initial;
  const calls: { path: string; body: unknown }[] = [];
  const record = async (path: string, request: Request) => {
    const text = await request.text();
    calls.push({ path, body: text ? JSON.parse(text) : null });
  };
  server.use(
    http.get(apiUrl(`/transfers/${ID}`), () => HttpResponse.json(current)),
    http.get(apiUrl(`/transfers/${ID}/history`), () => HttpResponse.json(history)),
    http.put(apiUrl(`/transfers/${ID}`), async ({ request }) => {
      await record('texts', request);
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(apiUrl(`/transfers/${ID}/cancel`), async ({ request }) => {
      await record('cancel', request);
      current = { ...current, status: 'Cancelled', cancellationReason: 'Erreur de saisie', cancellationOperationAt: '2026-10-22T16:00:00Z' };
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(apiUrl(`/transfers/${ID}/new-code`), async ({ request }) => {
      await record('newCode', request);
      return HttpResponse.json({ id: ID, reference: current.reference, withdrawalCode: '654321' });
    }),
    http.get(apiUrl(`/transfers/${ID}/receipt`), () => {
      calls.push({ path: 'receipt', body: null });
      return new HttpResponse(new Uint8Array([0x25, 0x50, 0x44, 0x46]), { headers: { 'Content-Type': 'application/pdf' } });
    }),
    http.post(apiUrl(`/transfers/${ID}/receipt`), async ({ request }) => {
      await record('receiptWithCode', request);
      return new HttpResponse(new Uint8Array([0x25, 0x50, 0x44, 0x46]), { headers: { 'Content-Type': 'application/pdf' } });
    }),
    http.post(apiUrl(`/transfers/${ID}/unlock-code`), async ({ request }) => {
      await record('unlock', request);
      current = { ...current, isCodeLocked: false };
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return calls;
}

const actions = () => screen.queryByRole('region', { name: 'Actions' });

describe('Fiche d’un transfert — Agent de l’agence d’envoi', () => {
  beforeEach(() => signedInAs(agentAccount));

  it('montre le détail, la répartition, le crédit et l’auteur de la création', async () => {
    detailServer(transfer());
    renderApp(`/transfers/${ID}`);

    expect(await screen.findByText('TR-NKC-20261022-000043')).toBeInTheDocument();
    expect(screen.getByText('1 250 000 FCFA')).toBeInTheDocument();
    expect(screen.getByText('À livrer à Dakar')).toBeInTheDocument();
    expect(screen.getByText('912 500 MRO')).toBeInTheDocument();
    const payment = screen.getByRole('region', { name: 'Paiement' });
    expect(within(payment).getByText('Caisse principale')).toBeInTheDocument();
    expect(within(payment).getByText('À crédit · Boutique Test')).toBeInTheDocument();
    // « Créé par » : l'auteur de la ligne « Ajouté » de l'historique (le nom figure aussi dans la barre latérale).
    const createdBy = await screen.findByText('Créé par');
    expect(createdBy.nextElementSibling).toHaveTextContent('Agent Test Nouakchott');
    expect(screen.queryByText('Bénéfice')).not.toBeInTheDocument();
  });

  it('peut modifier les textes et demander un nouveau code, mais pas annuler', async () => {
    detailServer(transfer());
    renderApp(`/transfers/${ID}`);

    const region = await screen.findByRole('region', { name: 'Actions' });
    expect(within(region).getByRole('button', { name: 'Modifier les textes' })).toBeInTheDocument();
    expect(within(region).getByRole('button', { name: 'Nouveau code' })).toBeInTheDocument();
    expect(within(region).queryByRole('button', { name: 'Annuler le transfert' })).not.toBeInTheDocument();
  });

  it('modifie les textes avec la version lue sur la fiche', async () => {
    const calls = detailServer(transfer());
    const { user } = renderApp(`/transfers/${ID}`);

    await user.click(await screen.findByRole('button', { name: 'Modifier les textes' }));
    const sheet = await screen.findByRole('dialog', { name: 'Modifier les textes' });
    const beneficiary = within(sheet).getByLabelText('Bénéficiaire');
    await user.clear(beneficiary);
    await user.type(beneficiary, 'Autre bénéficiaire');
    await user.type(within(sheet).getByLabelText('Motif (facultatif)'), 'Faute de frappe');
    await user.click(within(sheet).getByRole('button', { name: 'Enregistrer' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(calls).toEqual([
      {
        path: 'texts',
        body: {
          version: 3,
          senderInfo: 'Expéditeur Test',
          beneficiaryInfo: 'Autre bénéficiaire',
          note: 'Frais de scolarité',
          reason: 'Faute de frappe',
        },
      },
    ]);
  });

  it('propose de recharger quand le transfert a été modifié entre-temps', async () => {
    detailServer(transfer());
    server.use(
      http.put(apiUrl(`/transfers/${ID}`), () => HttpResponse.json({ code: 'Transfers.Transfer.VersionConflict' }, { status: 409 })),
    );
    const { user } = renderApp(`/transfers/${ID}`);

    await user.click(await screen.findByRole('button', { name: 'Modifier les textes' }));
    const sheet = await screen.findByRole('dialog', { name: 'Modifier les textes' });
    await user.click(within(sheet).getByRole('button', { name: 'Enregistrer' }));

    expect(await within(sheet).findByText(/vient d'être modifié par un autre utilisateur/)).toBeInTheDocument();
    expect(within(sheet).getByRole('button', { name: 'Recharger le transfert' })).toBeInTheDocument();
  });

  it('envoie le duplicata du reçu, sans code', async () => {
    const downloads = stubDownloads();
    const calls = detailServer(transfer());
    const { user } = renderApp(`/transfers/${ID}`);

    await user.click(await screen.findByRole('button', { name: 'Reçu (duplicata)' }));

    expect(await screen.findByText('Reçu téléchargé : envoyez-le au client.')).toBeInTheDocument();
    expect(calls.map((c) => c.path)).toEqual(['receipt']);
    expect(downloads.files).toEqual(['Recu-TR-NKC-20261022-000043.pdf']);
    downloads.restore();
  });

  it('après un nouveau code, le reçu envoyé porte ce code', async () => {
    const downloads = stubDownloads();
    const calls = detailServer(transfer());
    const { user } = renderApp(`/transfers/${ID}`);

    await user.click(await screen.findByRole('button', { name: 'Nouveau code' }));
    const sheet = await screen.findByRole('dialog', { name: 'Nouveau code' });
    await user.click(within(sheet).getByRole('button', { name: 'Créer un nouveau code' }));
    await user.click(await within(sheet).findByRole('button', { name: 'Envoyer le reçu avec le code' }));

    expect(await within(sheet).findByText('Reçu téléchargé : envoyez-le au client.')).toBeInTheDocument();
    expect(calls.at(-1)).toEqual({ path: 'receiptWithCode', body: { code: '654321' } });
    downloads.restore();
  });

  it('explique un refus du serveur pour le reçu', async () => {
    detailServer(transfer());
    server.use(
      http.get(apiUrl(`/transfers/${ID}/receipt`), () =>
        HttpResponse.json({ status: 403, code: 'Transfers.Receipt.NotSendingAgency' }, { status: 403 }),
      ),
    );
    const { user } = renderApp(`/transfers/${ID}`);

    await user.click(await screen.findByRole('button', { name: 'Reçu (duplicata)' }));
    expect(await screen.findByText('Le reçu se demande à l’agence d’envoi.')).toBeInTheDocument();
  });

  it('affiche le nouveau code une seule fois', async () => {
    const calls = detailServer(transfer());
    const { user } = renderApp(`/transfers/${ID}`);

    await user.click(await screen.findByRole('button', { name: 'Nouveau code' }));
    const sheet = await screen.findByRole('dialog', { name: 'Nouveau code' });
    await user.click(within(sheet).getByRole('button', { name: 'Créer un nouveau code' }));

    expect(await within(sheet).findByText('654321')).toHaveClass('sr-only');
    expect(within(sheet).getByText(/Ce code ne sera plus jamais affiché/)).toBeInTheDocument();
    expect(calls.map((c) => c.path)).toEqual(['newCode']);
  });

  it('raconte l’historique : auteur, motif, avant / après, numéro du code faux', async () => {
    detailServer(transfer());
    const { user } = renderApp(`/transfers/${ID}`);

    await user.click(await screen.findByRole('tab', { name: 'Historique' }));

    const panel = screen.getByRole('tabpanel');
    expect(await within(panel).findByText('Ajouté')).toBeInTheDocument();
    expect(within(panel).getByText('Motif : Faute de frappe')).toBeInTheDocument();
    expect(within(panel).getByRole('rowheader', { name: 'Bénéficiaire' })).toBeInTheDocument();
    expect(within(panel).getByRole('cell', { name: 'Bénéficiaire Test' })).toBeInTheDocument();
    expect(within(panel).getByText('Code faux')).toBeInTheDocument();
    expect(within(panel).getByText(/Agent Test Dakar · .* · essai 2 sur 5/)).toBeInTheDocument();
  });
});

describe('Fiche d’un transfert — Agent de l’agence de destination', () => {
  it('ne montre ni la répartition, ni le crédit, ni les actions de l’agence d’envoi', async () => {
    signedInAs({ ...agentAccount, agency: { id: dkr.id, code: 'DKR', name: 'Dakar', color: '#1565C0' } });
    detailServer(transfer({ direction: 'Received', payments: null, credit: null }));
    renderApp(`/transfers/${ID}`);

    await screen.findByText('TR-NKC-20261022-000043');
    expect(screen.queryByRole('region', { name: 'Paiement' })).not.toBeInTheDocument();
    expect(screen.getByText(/Le code de retrait est détenu par l'expéditeur/)).toBeInTheDocument();
    // Seule action : livrer (agence de destination).
    const region = actions()!;
    expect(within(region).getByRole('link', { name: 'Livrer' })).toHaveAttribute('href', `/transfers/${ID}/deliver`);
    expect(within(region).queryByRole('button')).not.toBeInTheDocument();
  });
});

describe('Fiche d’un transfert — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('voit le prix d’achat et le bénéfice, et annule avec un motif obligatoire', async () => {
    const calls = detailServer(transfer({ purchaseRate: 3600, profitMro: 12500.4 }));
    const { user } = renderApp(`/transfers/${ID}`);

    expect(await screen.findByText('+ 12 500 MRO')).toBeInTheDocument();
    expect(screen.getByText('Achat · 5 000 FCFA = 3 600 MRO')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Annuler le transfert' }));
    const sheet = await screen.findByRole('dialog', { name: 'Annuler ce transfert ?' });
    expect(within(sheet).getByText(/La dette de Boutique Test née de ce transfert sera effacée/)).toBeInTheDocument();
    await user.click(within(sheet).getByRole('button', { name: 'Annuler le transfert' }));
    expect(await within(sheet).findByText('Le motif est obligatoire.')).toBeInTheDocument();
    expect(calls).toEqual([]);

    await user.type(within(sheet).getByLabelText('Motif (obligatoire)'), 'Erreur de saisie');
    await user.click(within(sheet).getByRole('button', { name: 'Annuler le transfert' }));

    expect(await screen.findByText('Annulé : Erreur de saisie')).toBeInTheDocument();
    expect(calls).toEqual([{ path: 'cancel', body: { reason: 'Erreur de saisie', operationAt: null } }]);
    // Annulé : plus aucune action, sauf le duplicata du reçu.
    expect(within(actions()!).getAllByRole('button').map((b) => b.textContent)).toEqual(['Reçu (duplicata)']);
  });

  it('débloque un code bloqué', async () => {
    const calls = detailServer(transfer({ isCodeLocked: true }));
    const { user } = renderApp(`/transfers/${ID}`);

    expect(await screen.findByText(/Code bloqué après 5 essais faux/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Débloquer le code' }));
    const sheet = await screen.findByRole('dialog', { name: 'Débloquer le code' });
    await user.click(within(sheet).getByRole('button', { name: 'Débloquer le code' }));

    await waitFor(() => expect(screen.queryByRole('button', { name: 'Débloquer le code' })).not.toBeInTheDocument());
    expect(calls).toEqual([{ path: 'unlock', body: { reason: null } }]);
  });
});

describe('Fiche d’un transfert — introuvable', () => {
  it('dit seulement que le transfert est introuvable (hors périmètre compris)', async () => {
    signedInAs(agentAccount);
    server.use(http.get(apiUrl(`/transfers/${ID}`), () => HttpResponse.json({ code: 'Transfers.Transfer.NotFound' }, { status: 404 })));
    server.use(http.get(apiUrl(`/transfers/${ID}/history`), () => HttpResponse.json({ code: 'Transfers.Transfer.NotFound' }, { status: 404 })));
    renderApp(`/transfers/${ID}`);

    expect(await screen.findByRole('alert')).toHaveTextContent('Transfert introuvable.');
  });
});
