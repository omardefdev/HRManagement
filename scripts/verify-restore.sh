#!/usr/bin/env bash
# OPS-003: prove a backup can actually be restored, not merely that a file exists.
#
# verify-backup-catalog.sh only checks catalogue freshness and checksums, which says nothing about
# whether the dump restores or whether the restored data is intact. A backup that has never been
# restored is not a backup. This script restores into a throwaway database and asserts the result.
#
# Usage:
#   ./scripts/verify-restore.sh --file backups/postgres/payroll_db-2026-08-01.dump --expect-table "PayrollRuns"
#
# Env: PGHOST PGPORT PGUSER PGPASSWORD (an admin role able to CREATE/DROP DATABASE).
set -euo pipefail

HOST="${PGHOST:-localhost}"
PORT="${PGPORT:-5432}"
USER_NAME="${PGUSER:-hrmanagement}"
DUMP_FILE=""
EXPECT_TABLES=()
MIN_ROWS=1
KEEP=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --file) DUMP_FILE="$2"; shift 2 ;;
    --expect-table) EXPECT_TABLES+=("$2"); shift 2 ;;
    --min-rows) MIN_ROWS="$2"; shift 2 ;;
    --keep) KEEP=1; shift ;;
    *) echo "Unknown arg: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "${DUMP_FILE}" ]]; then
  echo "Usage: $0 --file DUMP [--expect-table T]... [--min-rows N] [--keep]" >&2
  exit 2
fi
if [[ ! -f "${DUMP_FILE}" ]]; then
  echo "FAIL: dump not found: ${DUMP_FILE}" >&2
  exit 1
fi
if [[ -z "${PGPASSWORD:-}" ]]; then
  echo "FAIL: PGPASSWORD required" >&2
  exit 1
fi

DRILL_DB="restore_verify_$(date -u +%Y%m%d%H%M%S)_$$"
started_at=$(date -u +%s)

cleanup() {
  if [[ "${KEEP}" -eq 0 ]]; then
    psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d postgres -q \
      -c "DROP DATABASE IF EXISTS \"${DRILL_DB}\" WITH (FORCE);" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

echo "Restoring ${DUMP_FILE} into ${DRILL_DB} ..."
psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d postgres -q \
  -c "CREATE DATABASE \"${DRILL_DB}\";"

if ! pg_restore --no-owner --no-privileges \
  -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d "${DRILL_DB}" "${DUMP_FILE}"; then
  echo "FAIL: pg_restore reported errors for ${DUMP_FILE}" >&2
  exit 1
fi

# A restore that produces an empty schema is a failed restore, however cleanly it exited.
table_count=$(psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d "${DRILL_DB}" -tAc \
  "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';")
if [[ "${table_count}" -eq 0 ]]; then
  echo "FAIL: restored database has no tables in schema public." >&2
  exit 1
fi
echo "OK: ${table_count} table(s) restored."

for table in "${EXPECT_TABLES[@]:-}"; do
  [[ -z "${table}" ]] && continue
  exists=$(psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d "${DRILL_DB}" -tAc \
    "SELECT to_regclass('public.\"${table}\"') IS NOT NULL;")
  if [[ "${exists}" != "t" ]]; then
    echo "FAIL: expected table '${table}' missing from restored database." >&2
    exit 1
  fi

  rows=$(psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d "${DRILL_DB}" -tAc \
    "SELECT count(*) FROM public.\"${table}\";")
  if [[ "${rows}" -lt "${MIN_ROWS}" ]]; then
    echo "FAIL: table '${table}' restored with ${rows} row(s); expected at least ${MIN_ROWS}." >&2
    exit 1
  fi
  echo "OK: ${table} restored with ${rows} row(s)."
done

finished_at=$(date -u +%s)
rto_seconds=$(( finished_at - started_at ))

# RTO is an output of the drill, not something an operator types in afterwards.
echo "PASS: restore verified from ${DUMP_FILE}"
echo "measured_restore_seconds=${rto_seconds}"
