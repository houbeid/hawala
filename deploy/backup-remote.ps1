<#
.SYNOPSIS
  Sauvegarde de la base Hawala hébergée (Render) sur ce PC, hors de l'hébergeur (1er novembre 2026).

.DESCRIPTION
  Copie complète de la base par pg_dump (format personnalisé), lancé dans un conteneur Docker : rien d'autre à installer
  que Docker Desktop. Le fichier est relu par pg_restore --list avant d'être gardé (une copie illisible n'est jamais
  gardée sous un nom final). Les copies de plus de KeepDays jours sont effacées. Une ligne par sauvegarde dans
  sauvegardes.log, à côté des fichiers.

  L'adresse de la base (avec son mot de passe) n'est jamais écrite dans ce fichier : elle vient du paramètre
  -DatabaseUrl, de la variable d'environnement HAWALA_BACKUP_DATABASE_URL (pour la tâche planifiée), sinon elle est
  demandée. Sur Render : base hawala-db -> Connect -> External (External Database URL).

  Une sauvegarde ne suffit pas seule : la clé Encryption__Key (gardée à part) est nécessaire pour relire les téléphones,
  NNI et pièces d'identité chiffrés. Copier aussi le dossier des sauvegardes ailleurs (disque externe, cloud).

.EXAMPLE
  .\backup-remote.ps1
  .\backup-remote.ps1 -Destination D:\hawala-sauvegardes -KeepDays 60

.NOTES
  Restaurer (API suspendue sur Render, voir docs/deploiement.md) :
  docker run --rm -v "<dossier>:/backup" postgres:17 pg_restore --clean --if-exists --no-owner --dbname="<adresse>" /backup/<fichier>.dump
#>
param(
    [string]$DatabaseUrl = $env:HAWALA_BACKUP_DATABASE_URL,
    [string]$Destination = (Join-Path $env:USERPROFILE 'Documents\hawala-sauvegardes'),
    [ValidateRange(1, 3650)][int]$KeepDays = 30,
    [string]$PostgresImage = 'postgres:17'
)

$ErrorActionPreference = 'Stop'

function Write-Log([string]$message) {
    $line = "$([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')) $message"
    Write-Host $line
    Add-Content -Path (Join-Path $Destination 'sauvegardes.log') -Value $line -Encoding UTF8
}

if (-not $DatabaseUrl) {
    $secure = Read-Host 'Adresse de la base (postgresql://…)' -AsSecureString
    $DatabaseUrl = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
if ($DatabaseUrl -notmatch '^postgres(ql)?://') { throw 'L''adresse de la base doit commencer par postgresql://' }

docker info *> $null
if ($LASTEXITCODE -ne 0) { throw 'Docker ne répond pas : ouvrir Docker Desktop puis relancer.' }

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$name = "hawala-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmm')).dump"
$temporary = "$name.tmp"
$mount = "${Destination}:/backup"

docker run --rm -v $mount $PostgresImage pg_dump "--dbname=$DatabaseUrl" --format=custom --no-owner "--file=/backup/$temporary"
if ($LASTEXITCODE -ne 0) {
    Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $Destination $temporary)
    Write-Log "ÉCHEC : pg_dump a échoué (code $LASTEXITCODE)."
    exit 1
}

docker run --rm -v $mount $PostgresImage pg_restore --list "/backup/$temporary" *> $null
if ($LASTEXITCODE -ne 0) {
    Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $Destination $temporary)
    Write-Log 'ÉCHEC : la copie ne se relit pas (pg_restore --list).'
    exit 1
}

Move-Item -Force (Join-Path $Destination $temporary) (Join-Path $Destination $name)
$size = [Math]::Round((Get-Item (Join-Path $Destination $name)).Length / 1KB)
Write-Log "sauvegarde $name ($size Ko), relue sans erreur."

$limit = (Get-Date).AddDays(-$KeepDays)
Get-ChildItem -Path $Destination -Filter 'hawala-*.dump' |
    Where-Object { $_.LastWriteTime -lt $limit } |
    ForEach-Object { Remove-Item -Force $_.FullName; Write-Log "effacée (plus de $KeepDays jours) : $($_.Name)" }
