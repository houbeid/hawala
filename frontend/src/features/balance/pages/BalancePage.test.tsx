import { screen, waitFor, within } from '@testing-library/react';
import { agentAccount, dkr, nkc, readOnlyAccount, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { balanceServer } from './balanceTestServer';

// Bilan par caisse (maquettes 12 / D5) : deux caisses, deux capitaux, chacune dans sa devise. Le bilan général a sa page.

const card = (name: string) => screen.getByRole('article', { name });

describe('Bilan par caisse — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('ne calcule pas le bilan général, qui a sa propre page', async () => {
    const { generalRates } = balanceServer();
    const { user } = renderApp('/balance');
    await screen.findByRole('article', { name: 'Caisse de Nouakchott' });
    expect(screen.queryByRole('region', { name: 'Bilan général' })).not.toBeInTheDocument();
    expect(generalRates).toEqual([]);

    await user.click(screen.getByRole('link', { name: 'Voir le bilan général' }));
    expect(await screen.findByRole('region', { name: 'Bilan général' })).toBeInTheDocument();
  });

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
  });

  it('sans balance.view : écran refusé', async () => {
    signedInAs(agentAccount);
    renderApp('/balance');
    expect(await screen.findByText("Votre compte n'a pas accès à cet écran.")).toBeInTheDocument();
  });
});
