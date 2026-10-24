import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';

// Mot de passe temporaire (maquette 13) : affiché UNE seule fois, jamais gardé ni journalisé par l'app. Bouton
// « Copier », rappel qu'il ne sera plus affiché, et « J'ai transmis le mot de passe » pour fermer.

interface Props {
  password: string;
  /** Pour qui (nom du compte), écrit au-dessus. */
  owner: string;
  onDone: () => void;
}

export function TemporaryPasswordView({ password, owner, onDone }: Props) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(password);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  }

  return (
    <div className="flex flex-col gap-3" role="group" aria-label={t('users.password.title')}>
      <p className="m-0 font-semibold">{owner}</p>
      <span className="text-sm text-muted">{t('users.password.title')}</span>
      <div className="flex items-center gap-2 rounded-xl border-2 border-ink bg-surface-2 p-3">
        <span className="flex-1 font-mono text-2xl font-bold tracking-wider" dir="ltr" data-testid="temporary-password">
          {password}
        </span>
        <Button icon={<Icon name={copied ? 'check' : 'doc'} />} onClick={() => void copy()}>
          {copied ? t('users.password.copied') : t('users.password.copy')}
        </Button>
      </div>
      <Notice tone="warning">{t('users.password.once')}</Notice>
      <Button variant="primary" fullWidth onClick={onDone}>
        {t('users.password.done')}
      </Button>
    </div>
  );
}
