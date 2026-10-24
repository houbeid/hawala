import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { usePermission } from '@/shared/auth/hooks';
import { formatAmount } from '@/shared/format/money';
import { Notice } from '@/shared/ui/Notice';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { checkPartyImport, checkResumedTransfers, downloadPartyTemplate, downloadResumeTemplate, importParties, importResumedTransfers } from '../api';
import { ImportSection } from '../components/ImportSection';
import { SetupChecklist } from '../components/SetupChecklist';
import { StartDateSection } from '../components/StartDateSection';

// Mise en service (phase 8, décisions du 24 octobre 2026), Superviseur : chargement des données de l'ancienne app, une
// seule fois, le soir de la bascule. 1. Tiers et soldes initiaux (parties.manage) ; 2. transferts non livrés repris
// (transfers.edit_rate) : « Non livré », sans toucher aux caisses, exclus du bénéfice. La date de démarrage d'abord, la
// liste de contrôle à la fin. Exige le réseau.

export function SetupPage() {
  const { t } = useTranslation();
  const online = useOnlineStatus();
  const agencies = useAgencies();
  const canResume = usePermission('transfers.edit_rate');

  if (!online) return <Notice tone="info">{t('setup.offline')}</Notice>;

  const agencyName = (code: string) => t(`agencies.${code}`, { defaultValue: code });
  const currencyOf = (code: string) => agencies.data?.find((a) => a.code === code)?.currency ?? 'MRO';

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-4">
      <p className="m-0 text-muted">{t('setup.intro')}</p>

      {/* À fixer d'abord : les soldes initiaux et les reprises sont datés du jour de démarrage. */}
      <StartDateSection />

      <ImportSection
        kind="parties"
        templateName="Modele-import-tiers.xlsx"
        fetchTemplate={downloadPartyTemplate}
        check={checkPartyImport}
        importFile={importParties}
        columnLabel={(key, agency) => (agency ? `${t(`setup.columns.${key}`)} · ${agencyName(agency)}` : t(`setup.columns.${key}`))}
        totalLine={(code, total) => ({
          label: t('setup.parties.total', { agency: agencyName(code) }),
          value: formatAmount(total, currencyOf(code), { signed: true }),
        })}
      />

      {canResume && (
        <ImportSection
          kind="transfers"
          templateName="Modele-reprise-transferts.xlsx"
          fetchTemplate={downloadResumeTemplate}
          check={checkResumedTransfers}
          importFile={importResumedTransfers}
          columnLabel={(key) => t(`setup.transferColumns.${key}`)}
          totalLine={(code, total) => ({
            label: t('setup.transfers.total', { agency: agencyName(code) }),
            value: formatAmount(total, currencyOf(code)),
          })}
        />
      )}

      <SetupChecklist />
    </div>
  );
}
