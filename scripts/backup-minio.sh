#!/usr/bin/env bash
# MinIO bucket backup via mc mirror (OPS-003). Prefer backup-reader credentials.
# Env: MINIO_ENDPOINT, MINIO_ACCESS_KEY/MINIO_SECRET_KEY (or legacy MINIO_ROOT_*),
#      MINIO_BACKUP_DIR, BACKUP_RETENTION_DAYS, BACKUP_CATALOG_PATH
set -eu

ROOT="$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)"
# shellcheck disable=SC1091
[ -f "$ROOT/scripts/lib/backup-catalog.sh" ] && . "$ROOT/scripts/lib/backup-catalog.sh"

ALIAS="${MINIO_ALIAS:-hr-backup}"
ENDPOINT="${MINIO_ENDPOINT:-http://minio:9000}"
ACCESS_KEY="${MINIO_ACCESS_KEY:-${MINIO_BACKUP_ACCESS_KEY:-${MINIO_ROOT_USER:-}}}"
SECRET_KEY="${MINIO_SECRET_KEY:-${MINIO_BACKUP_SECRET_KEY:-${MINIO_ROOT_PASSWORD:-}}}"
DEST="${MINIO_BACKUP_DIR:-/backups/minio}"
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-30}"
export BACKUP_RETENTION_DAYS="$RETENTION_DAYS"

if [ -z "$ACCESS_KEY" ] || [ -z "$SECRET_KEY" ]; then
  echo "ERROR: set MINIO_ACCESS_KEY/MINIO_SECRET_KEY (prefer backup-reader) or MINIO_ROOT_*" >&2
  exit 1
fi

if ! command -v mc >/dev/null 2>&1; then
  echo "mc (MinIO Client) not found; skip MinIO backup."
  exit 0
fi

mc alias set "$ALIAS" "$ENDPOINT" "$ACCESS_KEY" "$SECRET_KEY"
mkdir -p "$DEST"
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
TARGET="$DEST/$STAMP"
mkdir -p "$TARGET"

for bucket in hr-core hr-contracts idcard records-archive hr-training file-storage file-storage-quarantine; do
  if mc ls "$ALIAS/$bucket" >/dev/null 2>&1; then
    mc mirror --overwrite "$ALIAS/$bucket" "$TARGET/$bucket"
    echo "Backed up bucket $bucket"
  fi
done

# Optional: also push to isolated hrm-backups bucket when configured
if [ -n "${BACKUP_S3_BUCKET:-}" ] && [ -n "${BACKUP_S3_ACCESS_KEY:-}" ]; then
  OFF=hr-offsite
  mc alias set "$OFF" "${BACKUP_S3_ENDPOINT:-$ENDPOINT}" "$BACKUP_S3_ACCESS_KEY" "$BACKUP_S3_SECRET_KEY"
  mc mb --ignore-existing "$OFF/${BACKUP_S3_BUCKET}" || true
  mc mirror --overwrite "$TARGET" "$OFF/${BACKUP_S3_BUCKET}/minio/$STAMP"
fi

if command -v backup_catalog_append >/dev/null 2>&1; then
  backup_catalog_append "minio.mirror" "$TARGET" "" "0" "ok"
fi

find "$DEST" -mindepth 1 -maxdepth 1 -type d -mtime +"$RETENTION_DAYS" -exec rm -rf {} +
echo "MinIO backup completed: $TARGET"
