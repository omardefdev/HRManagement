#!/usr/bin/env bash
# OPS-003: sync local backup trees to S3-compatible hrm-backups prefix.
# Requires: mc OR aws cli. Env from cnpg-backup-store style credentials.
#
#   BACKUP_S3_ENDPOINT=http://minio:9000 \
#   BACKUP_S3_ACCESS_KEY=... BACKUP_S3_SECRET_KEY=... \
#   BACKUP_S3_BUCKET=hrm-backups \
#   ./scripts/backup-offsite-sync.sh ./backups
set -euo pipefail

ROOT_DIR="${1:-./backups}"
ENDPOINT="${BACKUP_S3_ENDPOINT:?BACKUP_S3_ENDPOINT required}"
ACCESS="${BACKUP_S3_ACCESS_KEY:?BACKUP_S3_ACCESS_KEY required}"
SECRET="${BACKUP_S3_SECRET_KEY:?BACKUP_S3_SECRET_KEY required}"
BUCKET="${BACKUP_S3_BUCKET:-hrm-backups}"
PREFIX="${BACKUP_S3_PREFIX:-postgres-dump}"
ALIAS="hrm-backup-offsite"

if [[ ! -d "${ROOT_DIR}" ]]; then
  echo "ERROR: source dir not found: ${ROOT_DIR}" >&2
  exit 1
fi

if command -v mc >/dev/null 2>&1; then
  mc alias set "${ALIAS}" "${ENDPOINT}" "${ACCESS}" "${SECRET}" >/dev/null
  mc mb --ignore-existing "${ALIAS}/${BUCKET}" || true
  # Object Lock / versioning notes: enable on bucket out-of-band for WORM
  mc mirror --overwrite "${ROOT_DIR}" "${ALIAS}/${BUCKET}/${PREFIX}/"
  echo "Offsite sync complete via mc -> ${BUCKET}/${PREFIX}/"
elif command -v aws >/dev/null 2>&1; then
  export AWS_ACCESS_KEY_ID="${ACCESS}" AWS_SECRET_ACCESS_KEY="${SECRET}"
  aws --endpoint-url "${ENDPOINT}" s3 sync "${ROOT_DIR}" "s3://${BUCKET}/${PREFIX}/"
  echo "Offsite sync complete via aws -> ${BUCKET}/${PREFIX}/"
else
  echo "ERROR: need mc or aws CLI for offsite sync" >&2
  exit 2
fi
