#!/usr/bin/env bash
# Linux equivalent of backup-database.ps1 (cron: 30 2 * * *)
set -euo pipefail
STAMP=$(date +%Y%m%d-%H%M%S); OUT=${BACKUP_ROOT:-/var/backups/nijmitra}; mkdir -p "$OUT"
pg_dump -h "${PGHOST:-localhost}" -p "${PGPORT:-5432}" -U "${PGUSER:-postgres}" -F c -b -f "$OUT/db-$STAMP.dump" "${PGDATABASE:-nijmitraapp}"
tar -czf "$OUT/files-$STAMP.tgz" -C "${SITE_ROOT:-/srv/nijmitra}" App_Data
find "$OUT" -type f -mtime +"${KEEP_DAYS:-30}" -delete
