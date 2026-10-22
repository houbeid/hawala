import { useTranslation } from 'react-i18next';
import { Icon } from '@/shared/ui/Icon';

// Panneau de marque des écrans sans session, sur ordinateur seulement (décision du 22 octobre 2026) :
// pas de photo (poids en 3G, cache hors ligne, lisibilité), un dessin léger du corridor aux couleurs
// des deux agences. Les noms de villes sont du texte (traduits, lus par les lecteurs d'écran) ;
// en arabe, l'ordre s'inverse avec le sens de lecture.

function City({ name, currency, tone }: { name: string; currency: string; tone: 'nkc' | 'dkr' }) {
  const dot = tone === 'nkc' ? 'bg-nkc-on-panel' : 'bg-dkr-on-panel';
  return (
    <div className="flex flex-col items-center gap-2">
      <span className={`size-5 rounded-full ${dot} ring-4 ring-panel-line`} aria-hidden="true" />
      <span className="text-lg font-semibold">{name}</span>
      <span className="amount text-sm font-semibold text-panel-muted">{currency}</span>
    </div>
  );
}

export function BrandPanel() {
  const { t } = useTranslation();

  return (
    <aside
      aria-label={t('auth.brand.label', { name: t('app.name') })}
      className="hidden flex-col justify-between gap-10 bg-panel p-12 text-panel-fg lg:flex"
    >
      <div className="flex items-center gap-3">
        <span className="inline-flex size-12 items-center justify-center rounded-[14px] bg-panel-fg text-panel">
          <Icon name="transfer" />
        </span>
        <span className="text-2xl font-bold">{t('app.name')}</span>
      </div>

      <div className="flex flex-col gap-10">
        <p className="m-0 max-w-[22ch] text-[40px] font-bold leading-tight">{t('auth.brand.tagline')}</p>

        <div className="flex max-w-md items-start gap-4">
          <City name={t('auth.brand.nouakchott')} currency="MRO" tone="nkc" />
          <div className="mt-2.5 flex flex-1 items-center" aria-hidden="true">
            <span className="h-0.5 flex-1 rounded bg-nkc-on-panel" />
            <span className="mx-2 text-panel-muted">
              <Icon name="transfer" />
            </span>
            <span className="h-0.5 flex-1 rounded bg-dkr-on-panel" />
          </div>
          <City name={t('auth.brand.dakar')} currency="FCFA" tone="dkr" />
        </div>
      </div>

      <p className="m-0 max-w-md text-panel-muted">{t('auth.brand.caisses')}</p>
    </aside>
  );
}
