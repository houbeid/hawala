import { act, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { agentAccount } from '@/test/fixtures';
import { renderApp, signedInAs } from '@/test/renderApp';
import { apiUrl, server } from '@/test/server';

// Application installée (PWA) : un faux service worker annonce une nouvelle version. On prouve : rien n'est affiché
// sans nouvelle version ; le bandeau apparaît, sur l'écran de connexion comme dans l'app ; « Mettre à jour » active
// la version (jamais de rechargement imposé) ; « Plus tard » referme le bandeau ; la version de l'app est écrite.

describe('Application installée', () => {
  it('propose une nouvelle version sans recharger d’elle-même, et l’active à la demande', async () => {
    signedInAs(agentAccount);
    server.use(http.get(apiUrl('/transfers'), () => HttpResponse.json({ items: [], page: 1, pageSize: 50, totalCount: 0, allCount: 0 })));
    const { user, updates } = renderApp('/more');

    await screen.findAllByText('Agent Test Nouakchott');
    expect(screen.queryByText('Nouvelle version de l’application disponible.')).not.toBeInTheDocument();

    act(() => updates.announce());
    expect(await screen.findByText('Nouvelle version de l’application disponible.')).toBeInTheDocument();
    expect(updates.applied()).toBe(0);

    await user.click(screen.getByRole('button', { name: 'Mettre à jour' }));
    expect(updates.applied()).toBe(1);
  });

  it('« Plus tard » referme le bandeau ; il apparaît aussi sur l’écran de connexion', async () => {
    server.use(http.post(apiUrl('/auth/refresh'), () => HttpResponse.json({ code: 'Identity.Auth.InvalidRefreshToken' }, { status: 401 })));
    const { user, updates } = renderApp('/login');

    await screen.findByRole('button', { name: 'Se connecter' });
    act(() => updates.announce());
    await user.click(await screen.findByRole('button', { name: 'Plus tard' }));

    expect(screen.queryByText('Nouvelle version de l’application disponible.')).not.toBeInTheDocument();
    expect(updates.applied()).toBe(0);
  });

  it('écrit la version de l’application dans « Plus »', async () => {
    signedInAs(agentAccount);
    renderApp('/more');

    // Barre latérale (ordinateur) et page « Plus » (téléphone) : les deux sont dans le DOM de test.
    expect(await screen.findAllByText(`Version ${__APP_VERSION__}`)).toHaveLength(2);
  });
});
