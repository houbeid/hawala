import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Icon } from './Icon';

// Boutons des maquettes : 56 px de haut, contour encre ; « primary » = fond encre.
// `busy` garde le bouton dans le formulaire mais empêche un second envoi, avec un texte de progression.

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary';
  busy?: boolean;
  busyLabel?: string;
  fullWidth?: boolean;
  icon?: ReactNode;
}

export function Button({
  variant = 'secondary',
  busy = false,
  busyLabel,
  fullWidth = false,
  icon,
  disabled,
  children,
  className = '',
  type = 'button',
  ...rest
}: ButtonProps) {
  const base =
    'inline-flex min-h-14 items-center justify-center gap-2 rounded-[14px] border-2 border-ink px-5 text-[17px] font-semibold ' +
    'focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-accent disabled:cursor-not-allowed disabled:opacity-55';
  const look = variant === 'primary' ? 'bg-ink text-on-ink' : 'bg-transparent text-fg';
  return (
    <button
      type={type}
      disabled={disabled || busy}
      aria-busy={busy || undefined}
      className={`${base} ${look} ${fullWidth ? 'w-full' : ''} ${className}`}
      {...rest}
    >
      {busy ? <Icon name="sync" spin /> : icon}
      {busy && busyLabel ? busyLabel : children}
    </button>
  );
}
