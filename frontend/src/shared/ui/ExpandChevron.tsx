import { Icon } from './Icon';

// Flèche d'une ligne dépliable (<details>) : vers le bas fermée, vers le haut ouverte. Le <details> porte la classe
// « group » ; le <summary> cache le triangle natif (« list-none ») pour que la flèche soit la seule marque. Décorative :
// le lecteur d'écran annonce déjà « réduit / développé ».

export function ExpandChevron({ className = '' }: { className?: string }) {
  return <Icon name="down" className={`size-5 shrink-0 text-muted transition-transform group-open:rotate-180 ${className}`} />;
}
