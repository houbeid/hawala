import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { HttpClient } from '../api/http';
import { errorMessage } from '../api/errorMessage';
import { usePermission, useSessionManager } from '../auth/hooks';
import { Button } from '../ui/Button';
import { Icon } from '../ui/Icon';
import { Notice } from '../ui/Notice';
import { useOnlineStatus } from '../useOnlineStatus';
import { shareOrDownload, type FileOutcome } from './shareFile';

// « Exporter » d'une liste (Excel .xlsx produit par le serveur, décision du 24 octobre 2026) : absent sans la
// permission export ; le fichier reprend les filtres de l'écran et la langue de l'interface (en-têtes en arabe,
// feuille de droite à gauche). Partagé par le téléphone ou téléchargé. Hors ligne : une explication.

export type ExportLanguage = 'fr' | 'ar';

interface Props {
  /** Nom du fichier proposé (« Transferts-20261024-20261024.xlsx »). */
  fileName: string;
  fetchFile: (http: HttpClient, lang: ExportLanguage) => Promise<Blob>;
}

export function ExportButton({ fileName, fetchFile }: Props) {
  const { t, i18n } = useTranslation();
  const allowed = usePermission('export');
  const session = useSessionManager();
  const online = useOnlineStatus();
  const [busy, setBusy] = useState(false);
  const [outcome, setOutcome] = useState<FileOutcome | null>(null);
  const [error, setError] = useState<string | null>(null);

  if (!allowed) return null;
  if (!online) return <p className="m-0 text-sm text-muted">{t('export.offline')}</p>;

  async function run() {
    setBusy(true);
    setError(null);
    setOutcome(null);
    try {
      const file = await fetchFile(session.http, i18n.language === 'ar' ? 'ar' : 'fr');
      setOutcome(await shareOrDownload(file, fileName));
    } catch (failure) {
      setError(errorMessage(i18n, failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col items-end gap-2">
      <Button icon={<Icon name="download" />} busy={busy} busyLabel={t('export.busy')} onClick={() => void run()}>
        {t('export.button')}
      </Button>
      {outcome === 'shared' && <Notice tone="info">{t('export.shared')}</Notice>}
      {outcome === 'downloaded' && <Notice tone="info">{t('export.downloaded')}</Notice>}
      {error && <Notice tone="error">{error}</Notice>}
    </div>
  );
}
