// Badge d'agence des maquettes : pastille de couleur + code (NKC, DKR), fond teinté de la couleur.
// La couleur vient du serveur (choisie par le Superviseur) ; le code reste écrit, jamais la couleur seule.

export function AgencyBadge({ code, color, label }: { code: string; color: string; label?: string }) {
  return (
    <span
      className="inline-flex h-7 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-full border-[1.5px] px-2.5 text-[13px] font-bold tracking-wide text-fg"
      style={{ borderColor: color, backgroundColor: `color-mix(in srgb, ${color} 14%, var(--surface))` }}
      title={label}
    >
      <i className="size-2.5 rounded-full" style={{ backgroundColor: color }} aria-hidden="true" />
      {code}
      {label && <span className="sr-only"> — {label}</span>}
    </span>
  );
}
