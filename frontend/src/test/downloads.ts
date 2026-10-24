// Téléchargements dans jsdom (pas de URL.createObjectURL, pas de navigation) : on garde le nom des fichiers
// téléchargés. Le partage de fichiers n'existe pas dans jsdom : les écrans téléchargent donc toujours.

export function stubDownloads() {
  const files: string[] = [];
  const original = { create: URL.createObjectURL, revoke: URL.revokeObjectURL };
  URL.createObjectURL = () => 'blob:test';
  URL.revokeObjectURL = () => {};
  const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    files.push(this.download);
  });
  return {
    files,
    restore() {
      click.mockRestore();
      URL.createObjectURL = original.create;
      URL.revokeObjectURL = original.revoke;
    },
  };
}
