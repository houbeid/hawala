import { screen, waitFor, within } from '@testing-library/react';
import { agentAccount, readOnlyAccount, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { balanceServer } from './balanceTestServer';

// Bilan général (décisions du 31 octobre 2026), sur sa propre page : Nouakchott + Dakar converti au taux du bilan.

const generalSection = () => screen.findByRole('region', { name: 'Bilan général' });

describe('Bilan général — Superviseur', () => {
  beforeEach(() => signedInAs(supervisorAccount));

  it('calcule au prix d’achat du jour : total, résultat net, bilan de chaque caisse et détail des composants', async () => {
    const { generalRates } = balanceServer();
    renderApp('/balance/general');

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
    const { user } = renderApp('/balance/general');
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
    const { user } = renderApp('/balance/general');
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
    const { user } = renderApp('/balance/general');
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
    const { user } = renderApp('/balance/general');
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
    const { user } = renderApp('/balance/general');
    const section = await generalSection();
    await user.click(await within(section).findByRole('button', { name: 'Expliquer la variation' }));

    const dialog = await screen.findByRole('dialog', { name: 'D’où vient la variation ?' });
    expect(await within(dialog).findByText(/Aucun bilan général sauvegardé/)).toBeInTheDocument();
  });

  it('liste les bilans généraux sauvegardés, chacun dépliable sur ses totaux', async () => {
    balanceServer();
    const { user } = renderApp('/balance/general');

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

describe('Bilan général — autres comptes', () => {
  it('avec balance.view seulement : lecture, sans sauvegarde ni capital général', async () => {
    signedInAs({ ...readOnlyAccount, permissions: [...readOnlyAccount.permissions, 'balance.view'] });
    balanceServer();
    renderApp('/balance/general');
    expect(await screen.findByText('167 871 591 MRO')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Sauvegarder le bilan général' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Capital général' })).not.toBeInTheDocument();
  });

  it('sans balance.view : écran refusé', async () => {
    signedInAs(agentAccount);
    renderApp('/balance/general');
    expect(await screen.findByText("Votre compte n'a pas accès à cet écran.")).toBeInTheDocument();
  });
});
