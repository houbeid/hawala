// Donner un fichier produit par le serveur (reçu PDF, export Excel) : pas d'imprimante en agence (décision du
// 24 octobre 2026), le fichier part par la feuille de partage du téléphone (WhatsApp…) quand elle accepte les
// fichiers ; sinon (ordinateur, navigateur sans partage de fichiers) il est téléchargé.

export type FileOutcome = 'shared' | 'downloaded' | 'cancelled';

interface Environment {
  navigator: Pick<Navigator, 'canShare' | 'share'>;
  document: Document;
  createObjectURL: (blob: Blob) => string;
  revokeObjectURL: (url: string) => void;
}

function browser(): Environment {
  return {
    navigator,
    document,
    createObjectURL: (blob) => URL.createObjectURL(blob),
    revokeObjectURL: (url) => URL.revokeObjectURL(url),
  };
}

/** Partage le fichier si le téléphone le permet, sinon le télécharge. Partage refermé par l'agent : « cancelled ». */
export async function shareOrDownload(blob: Blob, fileName: string, env: Environment = browser()): Promise<FileOutcome> {
  const file = new File([blob], fileName, { type: blob.type || 'application/octet-stream' });
  if (typeof env.navigator.canShare === 'function' && env.navigator.canShare({ files: [file] })) {
    try {
      await env.navigator.share({ files: [file], title: fileName });
      return 'shared';
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') return 'cancelled';
      // Partage refusé par le navigateur (autorisation, taille) : on retombe sur le téléchargement.
    }
  }

  const url = env.createObjectURL(file);
  const link = env.document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.rel = 'noopener';
  env.document.body.append(link);
  link.click();
  link.remove();
  // Laisser au navigateur le temps de lire le fichier avant de libérer l'adresse.
  setTimeout(() => env.revokeObjectURL(url), 60_000);
  return 'downloaded';
}
