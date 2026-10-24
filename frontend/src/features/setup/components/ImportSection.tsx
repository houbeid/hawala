import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { HttpClient } from '@/shared/api/http';
import { errorMessage } from '@/shared/api/errorMessage';
import { isApiError } from '@/shared/api/errors';
import { useSessionManager } from '@/shared/auth/hooks';
import { shareOrDownload } from '@/shared/files/shareFile';
import { isIsoDay, todayIso } from '@/shared/format/dates';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { Sheet } from '@/shared/ui/Sheet';
import { canImport, issueView, reportOfError } from '../importRules';
import type { ImportReport } from '../types';

// Une section d'import de la mise en service (tiers, transferts non livrés) : 1. télécharger le modèle ; 2. choisir le
// fichier rempli et la date de mise en service ; 3. « Vérifier » (rapport ligne par ligne, rien n'est créé) ; 4. « Importer »
// (tout ou rien), proposé seulement après une vérification sans problème du même fichier, avec confirmation. Les textes
// sont lus sous `setup.<kind>` ; un refus du serveur garde son rapport.

interface Props {
  kind: 'parties' | 'transfers';
  templateName: string;
  fetchTemplate: (http: HttpClient, lang: 'fr' | 'ar') => Promise<Blob>;
  check: (http: HttpClient, file: File) => Promise<ImportReport>;
  importFile: (http: HttpClient, file: File, operationDate: string | null) => Promise<{ created: number }>;
  /** Libellé d'une colonne (clé du serveur, éventuellement « NKC.balance »). */
  columnLabel: (key: string, agencyCode: string | null) => string;
  /** Une ligne de totaux du rapport (code d'agence → texte). */
  totalLine: (code: string, total: number) => { label: string; value: string };
}

export function ImportSection({ kind, templateName, fetchTemplate, check, importFile, columnLabel, totalLine }: Props) {
  const { t, i18n } = useTranslation();
  const session = useSessionManager();
  const today = todayIso();
  const [file, setFile] = useState<File | null>(null);
  const [date, setDate] = useState(today);
  const [report, setReport] = useState<ImportReport | null>(null);
  const [checkedFile, setCheckedFile] = useState<File | null>(null);
  const [busy, setBusy] = useState<'template' | 'check' | 'import' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [imported, setImported] = useState<number | null>(null);
  const k = (key: string) => `setup.${kind}.${key}`;

  async function run<T>(what: 'template' | 'check' | 'import', work: () => Promise<T>): Promise<T | undefined> {
    setBusy(what);
    setError(null);
    try {
      return await work();
    } catch (failure) {
      if (isApiError(failure)) {
        const rejected = reportOfError(failure.details);
        if (rejected) setReport(rejected);
        if (failure.code.endsWith('.RowFailed')) {
          const cause = String(failure.details.cause);
          setError(t('setup.rowFailed', { row: failure.details.row, reason: t(`errors.${cause}`, { defaultValue: cause }) }));
          return undefined;
        }
      }
      setError(errorMessage(i18n, failure));
      return undefined;
    } finally {
      setBusy(null);
    }
  }

  async function verify() {
    if (!file) return;
    setImported(null);
    const result = await run('check', () => check(session.http, file));
    if (result) {
      setReport(result);
      setCheckedFile(file);
    }
  }

  async function confirmImport() {
    if (!file) return;
    setConfirming(false);
    const result = await run('import', () => importFile(session.http, file, date === today ? null : date));
    if (result) {
      setImported(result.created);
      setReport(null);
      setCheckedFile(null);
      setFile(null);
    }
  }

  return (
    <section className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4" aria-label={t(k('title'))}>
      <h2 className="m-0 text-lg font-bold">{t(k('title'))}</h2>

      <ol className="m-0 flex flex-col gap-3 ps-5">
        <li>
          <div className="flex flex-wrap items-center gap-2">
            <span>{t(k('step1'))}</span>
            <Button
              icon={<Icon name="download" />}
              busy={busy === 'template'}
              busyLabel={t('setup.busy')}
              onClick={() =>
                void run('template', async () => {
                  const blob = await fetchTemplate(session.http, i18n.language === 'ar' ? 'ar' : 'fr');
                  await shareOrDownload(blob, templateName);
                })
              }
            >
              {t('setup.template')}
            </Button>
          </div>
        </li>
        <li className="flex flex-col gap-2">
          <span>{t('setup.step2')}</span>
          <label className="flex flex-col gap-1.5">
            <span className="text-sm font-semibold text-muted">{t(k('file'))}</span>
            <input
              type="file"
              accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              onChange={(e) => {
                setFile(e.target.files?.[0] ?? null);
                setReport(null);
                setImported(null);
              }}
              className="min-h-12"
            />
          </label>
          <label className="flex flex-col gap-1.5">
            <span className="text-sm font-semibold text-muted">{t(k('date'))}</span>
            <input
              type="date"
              value={date}
              max={today}
              onChange={(e) => setDate(isIsoDay(e.target.value) ? e.target.value : today)}
              className="min-h-14 rounded-xl border-[1.5px] border-border bg-surface px-3.5 text-[17px] text-fg"
            />
          </label>
        </li>
        <li>
          <Button icon={<Icon name="check" />} disabled={!file} busy={busy === 'check'} busyLabel={t('setup.busy')} onClick={() => void verify()}>
            {t(k('check'))}
          </Button>
        </li>
      </ol>

      {error && <Notice tone="error">{error}</Notice>}
      {imported !== null && <Notice tone="info">{t(k('imported'), { count: imported })}</Notice>}

      {report && (
        <section className="flex flex-col gap-2" aria-label={t(k('report'))}>
          <h3 className="m-0 text-base font-bold">{t(k('report'))}</h3>
          <p className="m-0">{t(k('summary'), { rows: report.rowCount, valid: report.validCount, issues: report.issues.length })}</p>
          {Object.entries(report.totals).length > 0 && (
            <dl className="m-0 grid grid-cols-[1fr_auto] gap-1.5">
              {Object.entries(report.totals).map(([code, total]) => {
                const line = totalLine(code, total);
                return (
                  <div key={code} className="contents">
                    <dt className="text-muted">{line.label}</dt>
                    <dd className="m-0 text-end">
                      <span className="amount">{line.value}</span>
                    </dd>
                  </div>
                );
              })}
            </dl>
          )}
          {report.issues.length === 0 ? (
            <Notice tone="info">{t('setup.noIssue')}</Notice>
          ) : (
            <ul className="m-0 flex list-none flex-col overflow-hidden rounded-2xl border border-border p-0" aria-label={t('setup.issues')}>
              {report.issues.map((issue, index) => {
                const view = issueView(issue);
                const column = view.column ? columnLabel(view.column.key, view.column.agencyCode) : null;
                return (
                  <li key={index} className="flex flex-col gap-0.5 border-b border-border bg-surface px-3.5 py-2.5 last:border-b-0">
                    <span className="font-semibold">
                      {view.row > 0 ? t('setup.row', { row: view.row }) : t(k('file'))}
                      {column && ` · ${column}`}
                    </span>
                    <span className="text-sm">{t(`errors.${view.code}`, { defaultValue: view.code })}</span>
                  </li>
                );
              })}
            </ul>
          )}
          {canImport(report, checkedFile, file) && (
            <Button variant="primary" icon={<Icon name="check" />} busy={busy === 'import'} busyLabel={t('setup.busy')} onClick={() => setConfirming(true)}>
              {t(k('import'), { count: report.validCount })}
            </Button>
          )}
        </section>
      )}

      {confirming && report && (
        <Sheet open onOpenChange={(open) => !open && setConfirming(false)} title={t(k('confirmTitle'), { count: report.validCount })}>
          <div className="flex flex-col gap-3">
            <Notice tone="warning">{t(k('confirmHint'), { date })}</Notice>
            <Button variant="primary" fullWidth onClick={() => void confirmImport()}>
              {t(k('import'), { count: report.validCount })}
            </Button>
            <Button fullWidth onClick={() => setConfirming(false)}>
              {t('setup.cancel')}
            </Button>
          </div>
        </Sheet>
      )}
    </section>
  );
}
