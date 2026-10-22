import { screen, waitFor, within } from '@testing-library/react';
import i18n from '@/i18n';
import { agentAccount, cashierAccount, readOnlyAccount, supervisorAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';

const bottomNav = () => screen.getByRole('navigation', { name: 'Navigation principale' });
const linkNames = (nav: HTMLElement) => within(nav).getAllByRole('link').map((link) => link.textContent);

describe('AppShell', () => {
  afterEach(async () => {
    await i18n.changeLanguage('fr');
  });

  it("ouvre « Transferts » par défaut, avec le badge verrouillé de l'agence de l'Agent", async () => {
    signedInAs(agentAccount);
    const { router } = renderApp('/');

    expect(await screen.findByRole('heading', { level: 1, name: 'Transferts' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/transfers');
    expect(screen.getByText('NKC')).toBeInTheDocument();
    // Verrouillé : pas de sélecteur d'agence.
    expect(screen.queryByRole('combobox', { name: 'Agence affichée' })).not.toBeInTheDocument();
    await waitFor(() => expect(document.documentElement.dataset.agency).toBe('NKC'));
    expect(document.documentElement.style.getPropertyValue('--agency')).toBe('#1E8E3E');
  });

  it("donne à l'Agent « Nouveau » au centre de la barre du bas", async () => {
    signedInAs(agentAccount);
    renderApp('/transfers');

    await screen.findByRole('heading', { level: 1, name: 'Transferts' });
    expect(linkNames(bottomNav())).toEqual(['Transferts', 'Nouveau', 'Tiers', 'Plus']);
  });

  it('donne au Caissier la caisse et le budget', async () => {
    signedInAs(cashierAccount);
    renderApp('/transfers');

    await screen.findByRole('heading', { level: 1, name: 'Transferts' });
    expect(linkNames(bottomNav())).toEqual(['Transferts', 'Caisse', 'Budget de la caisse', 'Plus']);
    expect(screen.getByText('DKR')).toBeInTheDocument();
  });

  it('signale la Consultation en lecture seule', async () => {
    signedInAs(readOnlyAccount);
    renderApp('/transfers');

    expect(await screen.findByText('Lecture seule')).toBeInTheDocument();
    expect(linkNames(bottomNav())).toEqual(['Transferts', 'Tiers', 'Plus']);
  });

  it("laisse le Superviseur choisir l'agence, « les deux » par défaut avec un filet neutre", async () => {
    signedInAs(supervisorAccount);
    const { user } = renderApp('/transfers');

    const select = await screen.findByRole('combobox', { name: 'Agence affichée' });
    await waitFor(() => expect(within(select).getAllByRole('option')).toHaveLength(3));
    expect(select).toHaveValue('all');
    expect(document.documentElement.dataset.agency).toBe('ALL');

    await user.selectOptions(select, 'DKR · Dakar');

    await waitFor(() => expect(document.documentElement.dataset.agency).toBe('DKR'));
    expect(document.documentElement.style.getPropertyValue('--agency')).toBe('#1565C0');
    expect(window.localStorage.getItem('hawala.supervisorAgency')).toBe('01900000-0000-7000-8000-0000000000a2');
  });

  it("refuse d'afficher un écran sans la permission, même en tapant l'adresse", async () => {
    signedInAs(agentAccount);
    renderApp('/profit');

    expect(await screen.findByText("Votre compte n'a pas accès à cet écran.")).toBeInTheDocument();
    expect(screen.queryByText('Cet écran arrive bientôt.')).not.toBeInTheDocument();
  });

  it("liste dans « Plus » seulement les écrans permis, puis se déconnecte", async () => {
    signedInAs(agentAccount);
    const { user } = renderApp('/more');

    const operations = await screen.findByRole('heading', { name: 'Opérations' });
    expect(operations).toBeInTheDocument();
    const main = screen.getByRole('main');
    expect(within(main).getByRole('link', { name: 'Taux du jour' })).toBeInTheDocument();
    expect(within(main).getByRole('link', { name: 'Comptes de trésorerie' })).toBeInTheDocument();
    expect(within(main).queryByRole('link', { name: 'Bénéfice' })).not.toBeInTheDocument();
    expect(within(main).queryByRole('link', { name: 'Comptes' })).not.toBeInTheDocument();

    await user.click(within(main).getByRole('button', { name: 'Se déconnecter' }));
    expect(await screen.findByRole('heading', { name: 'Connexion' })).toBeInTheDocument();
  });

  it('change la langue depuis la feuille « Langue et thème », puis se ferme avec Échap', async () => {
    signedInAs(agentAccount);
    const { user } = renderApp('/transfers');
    await screen.findByRole('heading', { level: 1, name: 'Transferts' });

    await user.click(screen.getByRole('button', { name: 'Langue et thème' }));
    const sheet = await screen.findByRole('dialog', { name: 'Langue et thème' });
    await user.click(within(sheet).getByRole('button', { name: 'العربية' }));

    expect(document.documentElement).toHaveAttribute('dir', 'rtl');

    // La feuille est modale : le reste de la page redevient accessible une fois fermée.
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(await screen.findByRole('heading', { level: 1, name: 'التحويلات' })).toBeInTheDocument();
  });

  it('montre le compte connecté dans la barre latérale du Superviseur, avec toutes ses entrées', async () => {
    signedInAs(supervisorAccount);
    renderApp('/transfers');

    const side = await screen.findByRole('navigation', { name: "Menu de l'application" });
    expect(within(side).getByRole('link', { name: 'Bénéfice' })).toBeInTheDocument();
    expect(within(side).getByRole('link', { name: "Journal d'audit" })).toBeInTheDocument();
    expect(screen.getByText('Superviseur Test')).toBeInTheDocument();
  });
});
