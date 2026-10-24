import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { agentAccount, dkr, nkc, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';
import type { PartyImportReport } from '../types';

// Mise en service : import des tiers (phase 8). On prouve : la vérification montre chaque problème (ligne, colonne,
// message traduit) et le total des soldes ; l'import n'est proposé qu'après une vérification sans problème, avec
// confirmation ; seul le Superviseur a l'écran. Données fictives uniquement.

const withIssues: PartyImportReport = {
  rowCount: 3,
  validCount: 1,
  issues: [
    { row: 3, column: 'type', code: 'Parties.Import.TypeUnknown' },
    { row: 4, column: 'NKC.balance', code: 'Parties.Import.AmountInvalid' },
  ],
  totals: { NKC: 1500 },
};
const clean: PartyImportReport = { rowCount: 2, validCount: 2, issues: [], totals: { NKC: 1500, DKR: -20000 } };

/** Ce que lisent la date de démarrage et la liste de contrôle. `startDate` null : pas encore fixée. */
function baseServer(options: { startDate?: string | null } = {}) {
  const puts: unknown[] = [];
  let startDate = options.startDate ?? null;
  server.use(
    http.get(apiUrl('/setup/start-date'), () => HttpResponse.json({ startDate, setAt: null, setBy: null })),
    http.put(apiUrl('/setup/start-date'), async ({ request }) => {
      const body = (await request.json()) as { startDate: string };
      puts.push(body);
      startDate = body.startDate;
      return HttpResponse.json({ startDate, setAt: '2026-10-24T20:00:00Z', setBy: 'u' });
    }),
    http.get(apiUrl('/parties'), () => HttpResponse.json({ items: [], count: 0, totalCount: 73, totals: [] })),
    http.get(apiUrl('/rates/:date'), () => HttpResponse.json({ code: 'Rates.Rate.NotFound' }, { status: 404 })),
    http.get(apiUrl('/balance/capital'), () => HttpResponse.json([{ agencyId: nkc.id, current: 3_500_000 }, { agencyId: dkr.id, current: null }])),
    http.get(apiUrl('/balance/snapshots'), () => HttpResponse.json([])),
    http.get(apiUrl('/users'), () => HttpResponse.json([{ role: 'Agent', status: 'Active' }])),
    http.get(apiUrl('/treasury-accounts'), () => HttpResponse.json([{ isActive: true }, { isActive: true }])),
    http.get(apiUrl('/cash-budget'), () => HttpResponse.json({ budget: 2_400_000 })),
    http.get(apiUrl('/cash-budget/snapshots'), () => HttpResponse.json([])),
  );
  return puts;
}

function setupServer(reports: PartyImportReport[]) {
  const imports: { date: string | null }[] = [];
  let checks = 0;
  server.use(
    http.post(apiUrl('/setup/parties/check'), () => HttpResponse.json(reports[Math.min(checks++, reports.length - 1)])),
    http.post(apiUrl('/setup/parties/import'), async ({ request }) => {
      const form = await request.formData();
      imports.push({ date: (form.get('operationDate') as string | null) ?? null });
      return HttpResponse.json({ created: 2 });
    }),
  );
  return imports;
}

/** La section des tiers (la page en a deux : tiers, puis transferts non livrés). */
const tiers = () => screen.getByRole('region', { name: 'Tiers et soldes initiaux' });

const xlsx = () => new File(['x'], 'tiers.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });

describe('Mise en service — Superviseur', () => {
  beforeEach(() => {
    signedInAs(supervisorAccount);
    baseServer({ startDate: '2026-10-24' });
  });

  it('vérifie le fichier, montre les problèmes, puis importe un fichier sans problème après confirmation', async () => {
    const imports = setupServer([withIssues, clean]);
    const { user } = renderApp('/setup');

    const file = await screen.findByLabelText('Fichier Excel rempli');
    await user.upload(file, xlsx());
    await user.click(within(tiers()).getByRole('button', { name: 'Vérifier le fichier' }));

    const problems = await screen.findByRole('list', { name: 'Problèmes à corriger dans le fichier' });
    expect(within(problems).getByText('Ligne 3 · Type')).toBeInTheDocument();
    expect(within(problems).getByText('Type inconnu : Client, Fournisseur, Personnel ou Autre.')).toBeInTheDocument();
    expect(within(problems).getByText('Ligne 4 · Solde · Nouakchott')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Importer/ })).not.toBeInTheDocument();

    await user.upload(file, xlsx());
    await user.click(within(tiers()).getByRole('button', { name: 'Vérifier le fichier' }));
    expect(await screen.findByText('Aucun problème : le fichier peut être importé.')).toBeInTheDocument();
    expect(screen.getByText('2 tiers lus, 2 prêts à importer, 0 problème(s).')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Importer 2 tiers' }));
    const dialog = await screen.findByRole('dialog', { name: 'Importer 2 tiers ?' });
    await user.click(within(dialog).getByRole('button', { name: 'Importer 2 tiers' }));

    expect(await screen.findByText('2 tiers importés.')).toBeInTheDocument();
    expect(imports).toEqual([{ date: null }]);
  });

  it('un refus à l’import montre le rapport renvoyé par le serveur', async () => {
    setupServer([clean]);
    server.use(
      http.post(apiUrl('/setup/parties/import'), () =>
        HttpResponse.json({ status: 400, code: 'Parties.Import.Invalid', report: withIssues }, { status: 400 }),
      ),
    );
    const { user } = renderApp('/setup');

    await user.upload(await screen.findByLabelText('Fichier Excel rempli'), xlsx());
    await user.click(within(tiers()).getByRole('button', { name: 'Vérifier le fichier' }));
    await user.click(await screen.findByRole('button', { name: 'Importer 2 tiers' }));
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Importer 2 tiers' }));

    expect(await screen.findByText('Le fichier contient des erreurs : rien n’a été importé.')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText('Ligne 3 · Type')).toBeInTheDocument());
  });
});

describe('Mise en service — transferts non livrés', () => {
  beforeEach(() => {
    signedInAs(supervisorAccount);
    baseServer({ startDate: '2026-10-24' });
  });

  it('reprend les transferts non livrés après vérification : total à livrer par agence, puis confirmation', async () => {
    const imports: string[] = [];
    server.use(
      http.post(apiUrl('/setup/transfers/check'), () =>
        HttpResponse.json({ rowCount: 1, validCount: 1, issues: [], totals: { DKR: 50000 } }),
      ),
      http.post(apiUrl('/setup/transfers/import'), () => {
        imports.push('import');
        return HttpResponse.json({ created: 1 });
      }),
    );
    const { user } = renderApp('/setup');

    const section = await screen.findByRole('region', { name: 'Transferts non livrés repris' });
    await user.upload(within(section).getByLabelText('Fichier Excel des non livrés'), xlsx());
    await user.click(within(section).getByRole('button', { name: 'Vérifier le fichier' }));

    expect(await within(section).findByText('1 transferts lus, 1 prêts à reprendre, 0 problème(s).')).toBeInTheDocument();
    expect(within(section).getByText('À livrer à Dakar').nextElementSibling).toHaveTextContent('50 000 FCFA');
    await user.click(within(section).getByRole('button', { name: 'Reprendre 1 transferts' }));
    const dialog = await screen.findByRole('dialog', { name: 'Reprendre 1 transferts ?' });
    expect(within(dialog).getByText(/sans toucher aux caisses/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Reprendre 1 transferts' }));

    expect(await within(section).findByText('1 transferts repris.')).toBeInTheDocument();
    expect(imports).toEqual(['import']);
  });
});

describe('Mise en service — date de démarrage et liste de contrôle', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('fixe la date de démarrage après une confirmation qui dit qu’elle ne se change plus', async () => {
    const puts = baseServer();
    const { user } = renderApp('/setup');

    const section = await screen.findByRole('region', { name: 'Date de démarrage' });
    fireEvent.change(await within(section).findByLabelText('Jour de mise en service'), { target: { value: '2026-10-20' } });
    await user.click(within(section).getByRole('button', { name: 'Fixer la date de démarrage' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/ne se change plus ensuite/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Fixer la date de démarrage' }));

    expect(await within(section).findByText(/Toute opération datée avant est refusée/)).toBeInTheDocument();
    expect(puts).toEqual([{ startDate: '2026-10-20' }]);
  });

  it('la liste de contrôle dit ce qui est fait et ce qui reste, avec le budget actuel de chaque caisse', async () => {
    baseServer({ startDate: '2026-10-24' });
    renderApp('/setup');

    const list = await screen.findByRole('list', { name: 'Liste de contrôle' });
    const row = (label: string) => within(list).getByText(label).closest('li') as HTMLElement;
    await waitFor(() => expect(row('Tiers importés')).toHaveTextContent('Fait'));
    expect(row('Tiers importés')).toHaveTextContent('73');
    expect(row('Date de démarrage fixée')).toHaveTextContent('Fait');
    await waitFor(() => expect(row('Taux du jour fixé')).toHaveTextContent('À faire'));
    await waitFor(() => expect(row('Capital initial saisi · Dakar')).toHaveTextContent('À faire'));
    expect(row('Capital initial saisi · Nouakchott')).toHaveTextContent('Fait');
    await waitFor(() => expect(row('Budget du jour enregistré · Nouakchott')).toHaveTextContent('Budget actuel : 2 400 000 MRO'));
    expect(within(row('Taux du jour fixé')).getByRole('link', { name: 'Ouvrir' })).toHaveAttribute('href', '/rates');
  });
});

describe('Mise en service — autres comptes', () => {
  it('un agent n’a pas accès à l’écran', async () => {
    signedInAs(agentAccount);
    renderApp('/setup');

    expect(await screen.findByText("Votre compte n'a pas accès à cet écran.")).toBeInTheDocument();
  });
});
