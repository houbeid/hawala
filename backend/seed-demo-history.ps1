<#
.SYNOPSIS
  Ajoute un historique FICTIF de photos à une base de DÉMONSTRATION, pour que les courbes s'affichent.

.DESCRIPTION
  Le serveur date toujours une photo du jour même : après seed-demo.ps1, il n'y a qu'une photo par type, et une courbe
  demande au moins deux jours. Ce script écrit directement dans la base, pour les jours passés seulement, des photos
  dérivées de la photo du jour (variation régulière de quelques pour cent) :
    - budget de chaque caisse (cash.budget_snapshots) ;
    - bilan de chaque caisse (balance.balance_snapshots) ;
    - bilan général (balance.general_balance_snapshots).
  Ces chiffres sont INVENTÉS : ils ne correspondent pas aux opérations de la démo. Le grand livre, les transferts et la
  caisse ne sont jamais touchés ; un jour qui a déjà sa photo est laissé tel quel ; la photo du jour reste la vraie.

  Ne jamais lancer sur une base réelle : le script refuse une base dont le nom ne contient pas « demo ».

  Utilisation (PowerShell, après seed-demo.ps1, Docker démarré) :
    .\seed-demo-history.ps1                                  # base hawala_demo, 30 jours
    .\seed-demo-history.ps1 -Database hawala_demo -Days 30
#>
param(
    [string]$Database = 'hawala_demo',
    [string]$Container = 'hawala-postgres',
    [ValidateRange(2, 60)][int]$Days = 30
)

$ErrorActionPreference = 'Stop'

if ($Database -notlike '*demo*') {
    Write-Host "Base « $Database » refusée : ce script n'écrit que dans une base de démonstration (nom contenant « demo »)." -ForegroundColor Yellow
    exit 1
}

# Facteur du jour d (1 = aujourd'hui) : une pente douce plus une ondulation, décalée par caisse ; le résultat est arrondi
# à l'unité pour le FCFA (toujours entier), à deux décimales sinon.
$sql = @"
\set ON_ERROR_STOP on
BEGIN;

CREATE TEMP TABLE demo_days AS SELECT generate_series(1, $Days) AS d;

INSERT INTO cash.budget_snapshots
    ("Id", "AgencyId", "Date", "Currency", "Cash", "Banks", "Debtors", "Creditors", "Undelivered", "Budget",
     "SavedBy", "SavedAt", "Alerts", "CheckedSince")
SELECT gen_random_uuid(), s."AgencyId", s."Date" - x.d, s."Currency",
       round(s."Cash" * x.f, x.p), round(s."Banks" * x.f, x.p), round(s."Debtors" * x.f, x.p),
       round(s."Creditors" * x.f, x.p), round(s."Undelivered" * x.f, x.p),
       round(s."Cash" * x.f, x.p) + round(s."Banks" * x.f, x.p) + round(s."Debtors" * x.f, x.p)
         - round(s."Creditors" * x.f, x.p) - round(s."Undelivered" * x.f, x.p),
       s."SavedBy", s."SavedAt" - make_interval(days => x.d), '[]'::jsonb, NULL
FROM cash.budget_snapshots s
JOIN LATERAL (
    SELECT dd.d,
           (1 - 0.003 * dd.d + 0.012 * sin(dd.d * 0.7 + CASE s."Currency" WHEN 'XOF' THEN 1.3 ELSE 0 END))::numeric AS f,
           CASE s."Currency" WHEN 'XOF' THEN 0 ELSE 2 END AS p
    FROM demo_days dd) x ON true
WHERE s."Date" = CURRENT_DATE
ON CONFLICT ("AgencyId", "Date") DO NOTHING;

INSERT INTO balance.balance_snapshots
    ("Id", "AgencyId", "Date", "Currency", "Budget", "OwnerContributions", "InitialCapital", "CapitalUsed",
     "NetResult", "NetResultPercent", "SavedProfitsMro", "SavedBy", "SavedAt")
SELECT gen_random_uuid(), s."AgencyId", s."Date" - x.d, s."Currency",
       round(s."Budget" * x.f, x.p), s."OwnerContributions", s."InitialCapital", s."CapitalUsed",
       CASE WHEN s."CapitalUsed" IS NULL THEN NULL ELSE round(s."Budget" * x.f, x.p) - s."CapitalUsed" END,
       CASE WHEN s."CapitalUsed" IS NULL OR s."CapitalUsed" = 0 THEN NULL
            ELSE (round(s."Budget" * x.f, x.p) - s."CapitalUsed") / s."CapitalUsed" * 100 END,
       s."SavedProfitsMro", s."SavedBy", s."SavedAt" - make_interval(days => x.d)
FROM balance.balance_snapshots s
JOIN LATERAL (
    SELECT dd.d,
           (1 - 0.003 * dd.d + 0.012 * sin(dd.d * 0.7 + CASE s."Currency" WHEN 'XOF' THEN 1.3 ELSE 0 END))::numeric AS f,
           CASE s."Currency" WHEN 'XOF' THEN 0 ELSE 2 END AS p
    FROM demo_days dd) x ON true
WHERE s."Date" = CURRENT_DATE
ON CONFLICT ("AgencyId", "Date") DO NOTHING;

INSERT INTO balance.general_balance_snapshots
    ("Id", "Date", "Rate",
     "NouakchottCash", "NouakchottBanks", "NouakchottDebtors", "NouakchottCreditors", "NouakchottUndelivered", "NouakchottOwnerContributions",
     "DakarCash", "DakarBanks", "DakarDebtors", "DakarCreditors", "DakarUndelivered", "DakarOwnerContributions",
     "GeneralBalance", "Capital", "CapitalUsed", "NetResult", "NetResultPercent", "SavedProfitsMro", "SavedBy", "SavedAt")
SELECT gen_random_uuid(), s."Date" - x.d, s."Rate",
       round(s."NouakchottCash" * x.f, 2), round(s."NouakchottBanks" * x.f, 2), round(s."NouakchottDebtors" * x.f, 2),
       round(s."NouakchottCreditors" * x.f, 2), round(s."NouakchottUndelivered" * x.f, 2), s."NouakchottOwnerContributions",
       round(s."DakarCash" * x.f, 0), round(s."DakarBanks" * x.f, 0), round(s."DakarDebtors" * x.f, 0),
       round(s."DakarCreditors" * x.f, 0), round(s."DakarUndelivered" * x.f, 0), s."DakarOwnerContributions",
       s."GeneralBalance" * x.f, s."Capital", s."CapitalUsed",
       CASE WHEN s."CapitalUsed" IS NULL THEN NULL ELSE s."GeneralBalance" * x.f - s."CapitalUsed" END,
       CASE WHEN s."CapitalUsed" IS NULL OR s."CapitalUsed" = 0 THEN NULL
            ELSE (s."GeneralBalance" * x.f - s."CapitalUsed") / s."CapitalUsed" * 100 END,
       s."SavedProfitsMro", s."SavedBy", s."SavedAt" - make_interval(days => x.d)
FROM balance.general_balance_snapshots s
JOIN LATERAL (
    SELECT dd.d, (1 - 0.003 * dd.d + 0.012 * sin(dd.d * 0.7))::numeric AS f FROM demo_days dd) x ON true
WHERE s."Date" = CURRENT_DATE
ON CONFLICT ("Date") DO NOTHING;

SELECT 'budget' AS photos, count(*) FROM cash.budget_snapshots
UNION ALL SELECT 'bilan par caisse', count(*) FROM balance.balance_snapshots
UNION ALL SELECT 'bilan general', count(*) FROM balance.general_balance_snapshots;

COMMIT;
"@

Write-Host "Historique fictif de $Days jours dans « $Database » (conteneur $Container)…"
$sql | docker exec -i $Container psql -U hawala -d $Database -q
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Échec : rien n''a été écrit (transaction annulée).' -ForegroundColor Red
    exit 1
}
Write-Host 'Terminé : les courbes du budget, du bilan par caisse et du bilan général ont maintenant plusieurs jours.' -ForegroundColor Green
Write-Host 'Rappel : ces photos passées sont inventées ; seule la photo du jour vient des opérations de la démo.'
