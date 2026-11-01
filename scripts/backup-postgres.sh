#!/usr/bin/env bash
# Logical backup of all HRManagement PostgreSQL databases (OPS-003).
# Inventory must match deploy/bootstrap/ensure-databases.sql (includes file_storage_db).
# Prefer role hrm_backup (see scripts/sql/create-hrm-backup-role.sql).
# Usage: ./scripts/backup-postgres.sh [output_dir]
# Env: POSTGRES_USER/POSTGRES_PASSWORD or PGUSER/PGPASSWORD; BACKUP_CATALOG_PATH; BACKUP_RETENTION_DAYS

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=lib/backup-catalog.sh
source "${ROOT}/scripts/lib/backup-catalog.sh"

OUTPUT_DIR="${1:-./backups/postgres}"
TIMESTAMP="$(date -u +%Y%m%dT%H%M%SZ)"
BACKUP_ROOT="${OUTPUT_DIR}/${TIMESTAMP}"
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-30}"
export BACKUP_RETENTION_DAYS="${RETENTION_DAYS}"

PGHOST="${PGHOST:-localhost}"
PGPORT="${PGPORT:-5433}"
# Prefer dedicated backup role when set
PGUSER="${BACKUP_PGUSER:-${POSTGRES_BACKUP_USER:-${POSTGRES_USER:-hrm_backup}}}"
export PGPASSWORD="${BACKUP_PGPASSWORD:-${POSTGRES_BACKUP_PASSWORD:-${POSTGRES_PASSWORD:-${PGPASSWORD:-}}}}"

if [[ -z "${PGPASSWORD}" ]]; then
  echo "ERROR: Set BACKUP_PGPASSWORD / POSTGRES_BACKUP_PASSWORD / POSTGRES_PASSWORD or PGPASSWORD." >&2
  exit 1
fi

# Full inventory — keep in sync with deploy/bootstrap/ensure-databases.sql
DATABASES=(
  keycloak_db
  gateway_audit_db
  hr_core_db
  hr_contracts_db
  hr_health_db
  hr_movements_db
  hr_training_db
  payroll_db
  idcard_db
  archive_db
  admin_db
  it_infra_db
  analytics_db
  compliance_db
  auth_directory_db
  file_storage_db
)

mkdir -p "${BACKUP_ROOT}"

echo "Backing up ${#DATABASES[@]} databases to ${BACKUP_ROOT} as ${PGUSER}@${PGHOST}:${PGPORT} ..."

sha_cmd() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | awk '{print $1}'
  else
    echo ""
  fi
}

MANIFEST_SHA=""
: > "${BACKUP_ROOT}/checksums.sha256"

for db in "${DATABASES[@]}"; do
  echo "  -> ${db}"
  outfile="${BACKUP_ROOT}/${db}.dump"
  pg_dump -h "${PGHOST}" -p "${PGPORT}" -U "${PGUSER}" -Fc -f "${outfile}" "${db}"
  sum="$(sha_cmd "${outfile}")"
  bytes="$(wc -c < "${outfile}" | tr -d ' ')"
  echo "${sum}  ${db}.dump" >> "${BACKUP_ROOT}/checksums.sha256"
  backup_catalog_append "postgres.${db}" "${outfile}" "${sum}" "${bytes}" "ok"
done

cat > "${BACKUP_ROOT}/manifest.txt" <<EOF
timestamp=${TIMESTAMP}
host=${PGHOST}
port=${PGPORT}
user=${PGUSER}
databases=${DATABASES[*]}
count=${#DATABASES[@]}
ops=OPS-003
EOF

echo "Pruning backups older than ${RETENTION_DAYS} days in ${OUTPUT_DIR} ..."
find "${OUTPUT_DIR}" -mindepth 1 -maxdepth 1 -type d -mtime "+${RETENTION_DAYS}" -exec rm -rf {} + 2>/dev/null || true

echo "Backup complete: ${BACKUP_ROOT}"
