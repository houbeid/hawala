#!/usr/bin/env sh
# Sauvegarde de la base Hawala (à lancer depuis deploy/, par exemple chaque nuit avec cron) :
#   0 2 * * * cd /opt/hawala/deploy && ./backup.sh >> backups/backup.log 2>&1
# Écrit backups/hawala-AAAAMMJJ-HHMM.dump (format personnalisé de pg_dump) et garde les KEEP_DAYS derniers jours.
# Une sauvegarde ne vaut que copiée HORS du serveur (et avec la clé de chiffrement, gardée à part).
# Restaurer (base vide, API arrêtée) :
#   docker compose stop api
#   docker compose exec -T db pg_restore -U hawala -d hawala --clean --if-exists < backups/hawala-….dump
#   docker compose start api
set -eu
KEEP_DAYS="${KEEP_DAYS:-14}"
mkdir -p backups
file="backups/hawala-$(date -u +%Y%m%d-%H%M).dump"
docker compose exec -T db pg_dump -U hawala -d hawala -Fc > "$file.tmp"
mv "$file.tmp" "$file"
echo "$(date -u +%FT%TZ) sauvegarde $file ($(du -h "$file" | cut -f1))"
find backups -name 'hawala-*.dump' -mtime "+$KEEP_DAYS" -delete
