// Icônes au trait des maquettes (24 × 24, trait 1,8, couleur du texte). Décoratives par défaut :
// le sens est toujours porté aussi par un texte (règle « rien par l'icône ou la couleur seule »).

const PATHS = {
  transfer: 'M7 8h13M17 5l3 3-3 3M17 16H4M7 13l-3 3 3 3',
  eye: 'M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6',
  eyeOff: 'M3 3l18 18M10.6 5.1A10 10 0 0 1 12 5c6.5 0 10 7 10 7a17 17 0 0 1-3 3.9M6.6 6.6C3.9 8.3 2 12 2 12s3.5 7 10 7a9.6 9.6 0 0 0 5.4-1.6M9.9 9.9a3 3 0 0 0 4.2 4.2',
  moon: 'M20 14.5A8 8 0 0 1 9.5 4 8 8 0 1 0 20 14.5',
  sun: 'M12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4',
  alert: 'M12 3.5l9.5 16.5h-19zM12 10v4.5M12 17.5h.01',
  info: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18M12 11v5.5M12 7.8h.01',
  wifiOff: 'M3 3l18 18M5.5 12.5a10 10 0 0 1 3.2-2M10 6.2A14 14 0 0 1 22 9M8.5 16a5 5 0 0 1 4-1.3M12 20h.01',
  check: 'M5 12.5l4.5 4.5L19 7',
  x: 'M6 6l12 12M18 6L6 18',
  lock: 'M6 11h12v9H6zM8.5 11V8a3.5 3.5 0 0 1 7 0v3',
  sync: 'M20 11a8 8 0 0 0-14.5-3M4 4v4h4M4 13a8 8 0 0 0 14.5 3M20 20v-4h-4',
  signOut: 'M10 4H5v16h5M15 8l4 4-4 4M19 12H9',
  plus: 'M12 5v14M5 12h14',
  users:
    'M16 20v-1.5a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4V20M9.5 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7M21 20v-1.5a4 4 0 0 0-3-3.9M15.5 4.2a3.5 3.5 0 0 1 0 6.6',
  user: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8M4 21c0-4 3.6-7 8-7s8 3 8 7',
  wallet: 'M4 7h14a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h11M16 14h2',
  chart: 'M4 20v-9M10 20V4M16 20v-6M21 20H3',
  trend: 'M3 17l6-6 4 4 8-9M15 6h6v6',
  cash: 'M3 7h18v10H3zM12 14.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5M6.5 10v.01M17.5 14v.01',
  bank: 'M3 10l9-6 9 6M5 10v8M9.5 10v8M14.5 10v8M19 10v8M3 20h18',
  rate: 'M4 7h12M13 4l3 3-3 3M20 17H8M11 14l-3 3 3 3',
  doc: 'M7 3h7l5 5v13H7zM14 3v5h5M10 13h6M10 17h6',
  menu: 'M4 6h16M4 12h16M4 18h16',
  globe: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18M3 12h18M12 3c3 2.8 3 15.2 0 18M12 3c-3 2.8-3 15.2 0 18',
  chevron: 'M9 6l6 6-6 6',
  chevronBack: 'M15 6l-6 6 6 6',
  down: 'M6 9l6 6 6-6',
  shield: 'M12 3l8 3v6c0 4.5-3.4 8.3-8 9-4.6-.7-8-4.5-8-9V6zM9 12l2 2 4-4',
  devices: 'M4 5h12v9H4zM2 18h16M19 8h3v11h-3z',
  clock: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18M12 7v5l3 2',
  search: 'M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14M20 20l-4-4',
  filter: 'M4 5h16l-6 7.5V19l-4-2v-4.5z',
  arrow: 'M5 12h14M13 6l6 6-6 6',
  up: 'M12 19V5M6 11l6-6 6 6',
  downArrow: 'M12 5v14M6 13l6 6 6-6',
  ban: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18M5.6 5.6l12.8 12.8',
  pencil: 'M4 20l1-4.5L16.5 4a2 2 0 0 1 3 3L8 18.5zM14.5 6l3.5 3.5',
  unlock: 'M6 11h12v9H6zM8.5 11V8a3.5 3.5 0 0 1 6.8-1.2',
  calendar: 'M5 5h14a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1M4 10h16M8 3v4M16 3v4',
} as const;

export type IconName = keyof typeof PATHS;

/** Icônes qui indiquent un sens de lecture : retournées en RTL. */
const DIRECTIONAL: ReadonlySet<IconName> = new Set(['signOut', 'chevron', 'chevronBack', 'arrow']);

export function Icon({ name, className = '', spin = false }: { name: IconName; className?: string; spin?: boolean }) {
  const classes = ['size-6 shrink-0', DIRECTIONAL.has(name) ? 'flip-rtl' : '', spin ? 'animate-spin' : '', className]
    .filter(Boolean)
    .join(' ');
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={classes}
    >
      <path d={PATHS[name]} />
    </svg>
  );
}
