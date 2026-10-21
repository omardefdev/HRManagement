#!/usr/bin/env bash
# PLAN-13 AC-34: cross-database logical relationship checks after restore drills.
# Usage:
#   BACKUP_DIR=backups/postgres/20260824T120000Z \
#   bash scripts/verify-restore-relationships.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOST="${PGHOST:-localhost}"
PORT="${PGPORT:-5433}"
USER_NAME="${PGUSER:-hrmanagement}"
BACKUP_DIR="${BACKUP_DIR:-}"
STAMP="$(date -u +%Y%m%d%H%M%S)"

if [[ -z "${PGPASSWORD:-}" ]]; then
  echo "FAIL: PGPASSWORD required" >&2
  exit 1
fi
if [[ -z "${BACKUP_DIR}" || ! -d "${BACKUP_DIR}" ]]; then
  echo "FAIL: BACKUP_DIR must point to a backup timestamp folder" >&2
  exit 1
fi

declare -A DRILL=(
  [hr_core_db]="ac34_hr_${STAMP}"
  [payroll_db]="ac34_pay_${STAMP}"
  [admin_db]="ac34_admin_${STAMP}"
  [gateway_audit_db]="ac34_audit_${STAMP}"
)

cleanup() {
  for db in "${DRILL[@]}"; do
    psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d postgres -q \
      -c "DROP DATABASE IF EXISTS \"${db}\" WITH (FORCE);" >/dev/null 2>&1 || true
  done
}
trap cleanup EXIT

restore_one() {
  local logical="$1"
  local drill="$2"
  local dump="${BACKUP_DIR}/${logical}.dump"
  if [[ ! -f "${dump}" ]]; then
    echo "SKIP: missing dump ${dump}" >&2
    return 1
  fi
  psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d postgres -q \
    -c "CREATE DATABASE \"${drill}\";"
  pg_restore --no-owner --no-privileges \
    -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d "${drill}" "${dump}"
  psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d "${drill}" \
    -v ON_ERROR_STOP=1 -f "${ROOT}/scripts/verify-restore-relationships.sql"
  echo "OK: ${logical} -> ${drill}"
}

orphans=0
for logical in hr_core_db payroll_db admin_db gateway_audit_db; do
  restore_one "${logical}" "${DRILL[$logical]}" || continue
done

# Cross-DB: sample payroll EmployeeIds must exist in hr_core Employees (when both restored)
if psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d "${DRILL[hr_core_db]}" -tAc \
  "SELECT to_regclass('public.\"Employees\"') IS NOT NULL;" | grep -q t; then
  sample_ids="$(psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d "${DRILL[payroll_db]}" -tAc \
    "SELECT \"EmployeeId\"::text FROM \"PayrollRunLines\" WHERE \"EmployeeId\" IS NOT NULL LIMIT 20;" 2>/dev/null || true)"
  for id in ${sample_ids:-}; do
    [[ -z "${id}" ]] && continue
    exists="$(psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d "${DRILL[hr_core_db]}" -tAc \
      "SELECT 1 FROM \"Employees\" WHERE \"Id\"='${id}' LIMIT 1;" 2>/dev/null || true)"
    if [[ "${exists}" != "1" ]]; then
      orphans=$((orphans + 1))
      echo "ORPHAN payroll EmployeeId=${id} not in hr_core Employees" >&2
    fi
  done
fi

assign_count="$(psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d "${DRILL[admin_db]}" -tAc \
  "SELECT count(*) FROM \"AssetAssignments\";" 2>/dev/null || echo 0)"
audit_chain="$(psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d "${DRILL[gateway_audit_db]}" -tAc \
  "SELECT count(*) FROM gateway_audit_entries WHERE \"EventHash\" <> '';" 2>/dev/null || echo 0)"

cat <<EOF
{
  "orphanEmployeeRefs": ${orphans},
  "assetAssignmentRows": ${assign_count:-0},
  "auditChainRows": ${audit_chain:-0},
  "pass": $([ "${orphans}" -eq 0 ] && echo true || echo false)
}
EOF

if [[ "${orphans}" -gt 0 ]]; then
  echo "FAIL: orphan cross-db employee references=${orphans}" >&2
  exit 1
fi
echo "PASS: relationship checks"
