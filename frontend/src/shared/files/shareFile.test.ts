import { shareOrDownload } from './shareFile';

// Reçu ou export : partagé par le téléphone quand il accepte les fichiers, sinon téléchargé.

function environment(share: { can: boolean; result?: () => Promise<void> }) {
  const shared: File[] = [];
  const clicks: { href: string; download: string }[] = [];
  const env = {
    navigator: {
      canShare: () => share.can,
      share: async (data?: ShareData) => {
        shared.push(...(data?.files ?? []));
        await (share.result?.() ?? Promise.resolve());
      },
    },
    document,
    createObjectURL: () => 'blob:recu',
    revokeObjectURL: () => {},
  };
  const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    clicks.push({ href: this.getAttribute('href') ?? '', download: this.download });
  });
  return { env, shared, clicks, restore: () => click.mockRestore() };
}

const pdf = new Blob(['%PDF'], { type: 'application/pdf' });

describe('shareOrDownload', () => {
  it('partage le fichier quand le téléphone le permet', async () => {
    const { env, shared, clicks, restore } = environment({ can: true });
    expect(await shareOrDownload(pdf, 'Recu-TR-NKC-20261024-000001.pdf', env)).toBe('shared');
    expect(shared.map((f) => [f.name, f.type])).toEqual([['Recu-TR-NKC-20261024-000001.pdf', 'application/pdf']]);
    expect(clicks).toEqual([]);
    restore();
  });

  it('le télécharge sinon (ordinateur)', async () => {
    const { env, shared, clicks, restore } = environment({ can: false });
    expect(await shareOrDownload(pdf, 'Recu.pdf', env)).toBe('downloaded');
    expect(shared).toEqual([]);
    expect(clicks).toEqual([{ href: 'blob:recu', download: 'Recu.pdf' }]);
    restore();
  });

  it('partage refermé : rien d’autre ne se passe', async () => {
    const { env, clicks, restore } = environment({ can: true, result: () => Promise.reject(new DOMException('fermé', 'AbortError')) });
    expect(await shareOrDownload(pdf, 'Recu.pdf', env)).toBe('cancelled');
    expect(clicks).toEqual([]);
    restore();
  });

  it('partage refusé par le navigateur : le fichier est téléchargé', async () => {
    const { env, clicks, restore } = environment({ can: true, result: () => Promise.reject(new DOMException('non', 'NotAllowedError')) });
    expect(await shareOrDownload(pdf, 'Recu.pdf', env)).toBe('downloaded');
    expect(clicks).toHaveLength(1);
    restore();
  });
});
