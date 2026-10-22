import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n from '@/i18n';
import { App } from './App';

describe('App (squelette)', () => {
  afterEach(async () => {
    await i18n.changeLanguage('fr');
  });

  it('démarre en français, de gauche à droite', () => {
    render(<App />);

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Hawala');
    expect(document.documentElement).toHaveAttribute('lang', 'fr');
    expect(document.documentElement).toHaveAttribute('dir', 'ltr');
  });

  it("passe en arabe et en RTL, et garde le choix pour l'appareil", async () => {
    const user = userEvent.setup();
    render(<App />);

    await user.click(screen.getByRole('button', { name: 'العربية' }));

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('حوالة');
    expect(document.documentElement).toHaveAttribute('lang', 'ar');
    expect(document.documentElement).toHaveAttribute('dir', 'rtl');
    expect(window.localStorage.getItem('hawala.language')).toBe('ar');
  });

  it('garde le taux en chiffres occidentaux, isolé de gauche à droite, même en arabe', async () => {
    const user = userEvent.setup();
    render(<App />);

    await user.click(screen.getByRole('button', { name: 'العربية' }));

    const rate = screen.getByText(/FCFA = .* MRO/);
    expect(rate).toHaveClass('amount');
    expect(rate.textContent).toMatch(/^[0-9\u202F\u00A0,= A-Z]+$/);
  });

  it('applique le thème sombre choisi', async () => {
    const user = userEvent.setup();
    render(<App />);

    await user.click(screen.getByRole('button', { name: 'Sombre' }));

    expect(document.documentElement).toHaveAttribute('data-theme', 'dark');
    expect(screen.getByRole('button', { name: 'Sombre' })).toHaveAttribute('aria-pressed', 'true');
    expect(window.localStorage.getItem('hawala.theme')).toBe('dark');
  });
});
