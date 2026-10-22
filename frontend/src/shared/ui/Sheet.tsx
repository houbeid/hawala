import * as Dialog from '@radix-ui/react-dialog';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Icon } from './Icon';

// Fenêtre modale des maquettes : feuille en bas sur téléphone, fenêtre centrée sur ordinateur. Radix gère
// le focus (piégé dedans, rendu au bouton d'ouverture), la touche Échap et l'annonce au lecteur d'écran.

interface SheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: string;
  children: ReactNode;
}

export function Sheet({ open, onOpenChange, title, description, children }: SheetProps) {
  const { t, i18n } = useTranslation();
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-black/55" />
        <Dialog.Content
          dir={i18n.dir()}
          className="fixed inset-x-0 bottom-0 z-50 flex max-h-[90dvh] flex-col gap-3 overflow-y-auto rounded-t-3xl border-t border-border bg-surface p-4 pb-6 text-fg lg:inset-x-auto lg:bottom-auto lg:start-1/2 lg:top-1/2 lg:w-[460px] lg:-translate-x-1/2 lg:-translate-y-1/2 lg:rounded-3xl lg:border rtl:lg:translate-x-1/2"
        >
          <div className="mx-auto h-1.5 w-11 rounded-full bg-border lg:hidden" aria-hidden="true" />
          <div className="flex items-center">
            <Dialog.Title className="m-0 flex-1 text-lg font-bold">{title}</Dialog.Title>
            <Dialog.Close className="inline-flex size-11 items-center justify-center rounded-xl" aria-label={t('common.close')}>
              <Icon name="x" />
            </Dialog.Close>
          </div>
          {description ? (
            <Dialog.Description className="m-0 text-sm text-muted">{description}</Dialog.Description>
          ) : (
            <Dialog.Description className="sr-only">{title}</Dialog.Description>
          )}
          {children}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
