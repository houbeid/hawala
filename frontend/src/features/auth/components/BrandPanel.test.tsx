import { render, screen, within } from '@testing-library/react';
import i18n from '@/i18n';
import { BrandPanel } from './BrandPanel';

describe('BrandPanel', () => {
  afterEach(async () => {
    await i18n.changeLanguage('fr');
  });

  it('présente le corridor avec les deux agences et leur devise, sans titre concurrent', () => {
    render(<BrandPanel />);

    const panel = screen.getByRole('complementary', { name: 'Présentation de ETS EL-VETH' });
    expect(within(panel).getByText("Transferts d'argent entre Nouakchott et Dakar")).toBeInTheDocument();
    expect(within(panel).getByText('Nouakchott')).toBeInTheDocument();
    expect(within(panel).getByText('Dakar')).toBeInTheDocument();
    expect(within(panel).getByText('MRO')).toBeInTheDocument();
    expect(within(panel).getByText('FCFA')).toBeInTheDocument();
    // Le seul titre de la page reste celui du formulaire (« Connexion »).
    expect(within(panel).queryByRole('heading')).not.toBeInTheDocument();
  });

  it('caché sur téléphone, affiché sur ordinateur', () => {
    render(<BrandPanel />);

    expect(screen.getByRole('complementary')).toHaveClass('hidden', 'lg:flex');
  });

  it('se traduit en arabe', async () => {
    await i18n.changeLanguage('ar');
    render(<BrandPanel />);

    const panel = screen.getByRole('complementary', { name: 'تعريف بـ ETS EL-VETH' });
    expect(within(panel).getByText('نواكشوط')).toBeInTheDocument();
    expect(within(panel).getByText('داكار')).toBeInTheDocument();
  });
});
