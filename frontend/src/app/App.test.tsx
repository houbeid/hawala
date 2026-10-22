import { screen, waitFor } from '@testing-library/react';
import type userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import i18n from '@/i18n';
import { agentAccount, dkr, nkc, tokens } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';

const noSession = () => HttpResponse.json({ code: 'Identity.RefreshToken.Invalid' }, { status: 401 });

async function fillLogin(user: ReturnType<typeof userEvent.setup>, login: string, password: string) {
  await user.type(await screen.findByLabelText('Email ou téléphone'), login);
  await user.type(screen.getByLabelText('Mot de passe'), password);
  await user.click(screen.getByRole('button', { name: 'Se connecter' }));
}

describe('Parcours de connexion', () => {
  beforeEach(() => {
    server.use(http.get(apiUrl('/agencies'), () => HttpResponse.json([nkc, dkr])));
  });

  afterEach(async () => {
    await i18n.changeLanguage('fr');
    delete document.documentElement.dataset.agency;
  });

  it('sans session, envoie vers la connexion puis ouvre la session', async () => {
    let body: unknown = null;
    server.use(
      http.post(apiUrl('/auth/refresh'), noSession),
      http.post(apiUrl('/auth/login'), async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(tokens('t1'));
      }),
      http.get(apiUrl('/auth/me'), () => HttpResponse.json(agentAccount)),
    );
    const { router, user } = renderApp('/');

    expect(await screen.findByRole('heading', { name: 'Connexion' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/login');

    await fillLogin(user, '  agent.nkc@example.com ', 'motdepasse1');

    expect(await screen.findByRole('heading', { level: 1, name: 'Transferts' })).toBeInTheDocument();
    expect(body).toEqual({ login: 'agent.nkc@example.com', password: 'motdepasse1' });
    expect(router.state.location.pathname).toBe('/transfers');
    await waitFor(() => expect(document.documentElement.dataset.agency).toBe('NKC'));
  });

  it('reprend une session existante sans passer par la connexion', async () => {
    server.use(
      http.post(apiUrl('/auth/refresh'), () => HttpResponse.json(tokens('t1'))),
      http.get(apiUrl('/auth/me'), () => HttpResponse.json(agentAccount)),
    );
    renderApp('/');

    expect(await screen.findByRole('heading', { level: 1, name: 'Transferts' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Connexion' })).not.toBeInTheDocument();
  });

  it('vérifie les champs avant tout envoi', async () => {
    let logins = 0;
    server.use(
      http.post(apiUrl('/auth/refresh'), noSession),
      http.post(apiUrl('/auth/login'), () => {
        logins += 1;
        return HttpResponse.json(tokens('t1'));
      }),
    );
    const { user } = renderApp('/login');

    await user.click(await screen.findByRole('button', { name: 'Se connecter' }));

    expect(await screen.findByText('Saisissez un email ou un numéro de téléphone.')).toBeInTheDocument();
    expect(screen.getByText('Saisissez le mot de passe.')).toBeInTheDocument();
    expect(screen.getByLabelText('Email ou téléphone')).toHaveAttribute('aria-invalid', 'true');
    expect(logins).toBe(0);
  });

  it('affiche le message unique du serveur pour un identifiant faux, et reste sur la connexion', async () => {
    server.use(
      http.post(apiUrl('/auth/refresh'), noSession),
      http.post(apiUrl('/auth/login'), () =>
        HttpResponse.json({ code: 'Identity.Auth.InvalidCredentials', detail: 'texte serveur' }, { status: 401 }),
      ),
    );
    const { router, user } = renderApp('/login');

    await fillLogin(user, 'inconnu@example.com', 'faux12345');

    expect(await screen.findByRole('alert')).toHaveTextContent('Identifiant ou mot de passe incorrect.');
    expect(router.state.location.pathname).toBe('/login');
    expect(screen.getByLabelText('Email ou téléphone')).toHaveValue('inconnu@example.com');
  });

  it('donne le délai à attendre après trop de tentatives', async () => {
    server.use(
      http.post(apiUrl('/auth/refresh'), noSession),
      http.post(apiUrl('/auth/login'), () =>
        HttpResponse.json({ code: 'Api.RateLimited' }, { status: 429, headers: { 'Retry-After': '45' } }),
      ),
    );
    const { user } = renderApp('/login');

    await fillLogin(user, 'agent.nkc@example.com', 'motdepasse1');

    expect(await screen.findByRole('alert')).toHaveTextContent('Trop de tentatives. Réessayez dans 45 s.');
  });

  it('impose le changement du mot de passe temporaire, puis ouvre la session', async () => {
    let changeBody: unknown = null;
    server.use(
      http.post(apiUrl('/auth/refresh'), noSession),
      http.post(apiUrl('/auth/login'), () => HttpResponse.json(tokens('temp', { mustChangePassword: true }))),
      http.post(apiUrl('/auth/change-password'), async ({ request }) => {
        changeBody = await request.json();
        return HttpResponse.json(tokens('t2'));
      }),
      http.get(apiUrl('/auth/me'), () => HttpResponse.json(agentAccount)),
    );
    const { router, user } = renderApp('/login');

    await fillLogin(user, 'agent.nkc@example.com', 'abc-def-gh2');
    expect(await screen.findByRole('heading', { name: 'Choisissez un nouveau mot de passe' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/change-password');

    // Aucune autre page n'est accessible avant le changement.
    await router.navigate({ to: '/' });
    expect(await screen.findByRole('heading', { name: 'Choisissez un nouveau mot de passe' })).toBeInTheDocument();

    await user.type(screen.getByLabelText('Mot de passe temporaire'), 'abc-def-gh2');
    await user.type(screen.getByLabelText('Nouveau mot de passe'), 'nouveau');
    expect(screen.getByText('8 caractères minimum')).toHaveTextContent('non respectée');
    expect(screen.getByText('Au moins un chiffre')).toHaveTextContent('non respectée');

    await user.type(screen.getByLabelText('Nouveau mot de passe'), 'pass1');
    expect(screen.getByText('8 caractères minimum')).toHaveTextContent('— respectée');
    expect(screen.getByText('Au moins un chiffre')).toHaveTextContent('— respectée');

    await user.type(screen.getByLabelText('Confirmer le nouveau mot de passe'), 'autre');
    await user.click(screen.getByRole('button', { name: 'Enregistrer et continuer' }));
    expect(await screen.findByText('Les deux mots de passe ne sont pas identiques.')).toBeInTheDocument();
    expect(changeBody).toBeNull();

    await user.clear(screen.getByLabelText('Confirmer le nouveau mot de passe'));
    await user.type(screen.getByLabelText('Confirmer le nouveau mot de passe'), 'nouveaupass1');
    await user.click(screen.getByRole('button', { name: 'Enregistrer et continuer' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Transferts' })).toBeInTheDocument();
    expect(changeBody).toEqual({ currentPassword: 'abc-def-gh2', newPassword: 'nouveaupass1' });
  });

  it('traduit le refus du mot de passe temporaire', async () => {
    server.use(
      http.post(apiUrl('/auth/refresh'), noSession),
      http.post(apiUrl('/auth/login'), () => HttpResponse.json(tokens('temp', { mustChangePassword: true }))),
      http.post(apiUrl('/auth/change-password'), () =>
        HttpResponse.json({ code: 'Identity.Password.Incorrect' }, { status: 400 }),
      ),
    );
    const { user } = renderApp('/login');
    await fillLogin(user, 'agent.nkc@example.com', 'abc-def-gh2');

    await user.type(await screen.findByLabelText('Mot de passe temporaire'), 'mauvais');
    await user.type(screen.getByLabelText('Nouveau mot de passe'), 'nouveaupass1');
    await user.type(screen.getByLabelText('Confirmer le nouveau mot de passe'), 'nouveaupass1');
    await user.click(screen.getByRole('button', { name: 'Enregistrer et continuer' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Le mot de passe actuel est incorrect.');
  });

  it('propose de réessayer quand le serveur ne répond pas au chargement', async () => {
    server.use(http.post(apiUrl('/auth/refresh'), () => HttpResponse.error()));
    const { user } = renderApp('/');

    expect(await screen.findByRole('heading', { name: 'Serveur injoignable' })).toBeInTheDocument();

    server.use(http.post(apiUrl('/auth/refresh'), noSession));
    await user.click(screen.getByRole('button', { name: 'Réessayer' }));

    expect(await screen.findByRole('heading', { name: 'Connexion' })).toBeInTheDocument();
  });

  it('se déconnecte et revient à la connexion', async () => {
    let loggedOut = false;
    server.use(
      http.post(apiUrl('/auth/refresh'), () => HttpResponse.json(tokens('t1'))),
      http.get(apiUrl('/auth/me'), () => HttpResponse.json(agentAccount)),
      http.post(apiUrl('/auth/logout'), () => {
        loggedOut = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { user } = renderApp('/');

    await user.click(await screen.findByRole('button', { name: 'Se déconnecter' }));

    expect(await screen.findByRole('heading', { name: 'Connexion' })).toBeInTheDocument();
    expect(loggedOut).toBe(true);
    await waitFor(() => expect(document.documentElement.dataset.agency).toBeUndefined());
  });

  it('explique pourquoi la session a été fermée par le serveur', async () => {
    server.use(
      http.post(apiUrl('/auth/refresh'), () => HttpResponse.json(tokens('t1'))),
      http.get(apiUrl('/auth/me'), () => HttpResponse.json(agentAccount)),
    );
    const { session } = renderApp('/');
    await screen.findByRole('heading', { level: 1, name: 'Transferts' });

    session.signOut('signedOut');

    expect(await screen.findByText(/Votre session a été fermée par le Superviseur./)).toBeInTheDocument();
  });

  it("s'affiche en arabe, de droite à gauche", async () => {
    server.use(http.post(apiUrl('/auth/refresh'), noSession));
    const { user } = renderApp('/login');

    await user.click(await screen.findByRole('button', { name: 'العربية' }));

    expect(screen.getByRole('heading', { name: 'تسجيل الدخول' })).toBeInTheDocument();
    expect(screen.getByLabelText('البريد الإلكتروني أو الهاتف')).toBeInTheDocument();
    expect(document.documentElement).toHaveAttribute('dir', 'rtl');
  });

  it('montre et masque le mot de passe', async () => {
    server.use(http.post(apiUrl('/auth/refresh'), noSession));
    const { user } = renderApp('/login');
    const field = await screen.findByLabelText('Mot de passe');

    expect(field).toHaveAttribute('type', 'password');
    await user.click(screen.getByRole('button', { name: 'Afficher le mot de passe' }));
    expect(field).toHaveAttribute('type', 'text');
    await user.click(screen.getByRole('button', { name: 'Masquer le mot de passe' }));
    expect(field).toHaveAttribute('type', 'password');
  });
});
