import { useTranslation } from 'react-i18next';
import type { Agency } from '@/features/organization/types';
import { formatAmount, formatPercent } from '@/shared/format/money';
import { AgencyBadge } from '@/shared/ui/AgencyBadge';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import type { BalanceView } from '../types';

// Une caisse du bilan (maquettes 12 / D5), dans sa devise, toujours dans le même ordre de lecture : budget, capital
// initial, apports nets du patron, capital pris en compte, puis le résultat net (le chiffre fort) et son rapport au
// capital. Filet du haut à la couleur de l'agence, en plus du badge écrit. Les bénéfices enregistrés (MRO) sont une
// information à part : ils ne s'ajoutent pas au résultat. Sans capital saisi : pas de résultat net.

interface Props {
  view: BalanceView;
  agency: Agency | undefined;
  /** balance.manage et en ligne : boutons « Enregistrer le bilan » et « Capital initial ». */
  manageable: boolean;
  onSave: () => void;
  onCapital: () => void;
}

function Row({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex flex-wrap items-baseline justify-between gap-2">
      <dt className={strong ? 'font-semibold' : 'text-sm text-muted'}>{label}</dt>
      <dd className={`amount m-0 ${strong ? 'text-2xl' : ''}`}>{value}</dd>
    </div>
  );
}

export function CaisseBalanceCard({ view, agency, manageable, onSave, onCapital }: Props) {
  const { t } = useTranslation();
  const name = agency ? t(`agencies.${agency.code}`, { defaultValue: agency.name }) : '';
  // Pleine précision au serveur ; l'affichage arrondit à l'unité (règle d'arrondi 4).
  const money = (value: number, signed = false) => formatAmount(value, view.currency, { signed, maxDecimals: 0 });
  return (
    <article
      className="flex flex-col gap-3 rounded-2xl border border-border border-t-[5px] bg-surface p-4"
      style={agency ? { borderTopColor: agency.color } : undefined}
      aria-label={t('balance.caisse', { name })}
    >
      <div className="flex items-center gap-2">
        {agency && <AgencyBadge code={agency.code} color={agency.color} />}
        <h2 className="m-0 text-lg font-bold">{t('balance.caisse', { name })}</h2>
      </div>
      <dl className="m-0 flex flex-col gap-2">
        <Row label={t('balance.budget')} value={money(view.budget)} />
        <Row label={t('balance.initialCapital')} value={view.initialCapital === null ? t('balance.notSet') : money(view.initialCapital)} />
        <Row label={t('balance.ownerContributions')} value={money(view.ownerContributions, true)} />
        {view.capitalUsed !== null && <Row label={t('balance.capitalUsed')} value={money(view.capitalUsed)} />}
        <div className="border-t border-border" />
        {view.netResult !== null ? (
          <>
            <Row label={t('balance.netResult')} value={money(view.netResult, true)} strong />
            {view.netResultPercent !== null && <Row label={t('balance.percent')} value={formatPercent(view.netResultPercent)} />}
          </>
        ) : (
          <p className="m-0 text-sm font-semibold">{t('balance.noCapital')}</p>
        )}
        <div className="border-t border-border" />
        <Row label={t('balance.savedProfits')} value={formatAmount(view.savedProfitsMro, 'MRO', { maxDecimals: 0 })} />
        <p className="m-0 text-xs text-muted">{t('balance.savedProfitsHint')}</p>
      </dl>
      {manageable && (
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
          <Button variant="primary" icon={<Icon name="check" />} onClick={onSave}>
            {t('balance.save.open')}
          </Button>
          <Button icon={<Icon name="pencil" />} onClick={onCapital}>
            {t('balance.capital.open')}
          </Button>
        </div>
      )}
    </article>
  );
}
