import type { ReactNode } from 'react';
import { Icon, type IconName } from './Icon';

// Bandeaux des maquettes : erreur (rouge, annoncée tout de suite), information (neutre), avertissement (ambre).

const LOOKS = {
  error: { icon: 'alert', className: 'border-[1.5px] border-red bg-red-bg font-semibold text-red' },
  info: { icon: 'info', className: 'bg-surface-2 text-fg' },
  warning: { icon: 'alert', className: 'bg-warn-bg font-semibold text-warn-fg' },
} satisfies Record<string, { icon: IconName; className: string }>;

export function Notice({ tone, children }: { tone: keyof typeof LOOKS; children: ReactNode }) {
  const look = LOOKS[tone];
  return (
    <div role={tone === 'error' ? 'alert' : 'status'} className={`flex gap-2.5 rounded-xl p-3 text-[15px] ${look.className}`}>
      <Icon name={look.icon} />
      <span>{children}</span>
    </div>
  );
}
