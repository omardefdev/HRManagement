#!/usr/bin/env bash
# OPS-003: restore a single logical dump into an isolated database.
# Usage:
#   ./scripts/restore-postgres.sh --host H --port P --user U --db TARGET_DB --file path/to/db.dump
# Env: PGPASSWORD required. Does not drop unrelated databases.
set -euo pipefail

HOST="${PGHOST:-localhost}"
PORT="${PGPORT:-5433}"
USER_NAME="${PGUSER:-hrmanagement}"
DB_NAME=""
DUMP_FILE=""
CREATE_DB=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --host) HOST="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --user) USER_NAME="$2"; shift 2 ;;
    --db) DB_NAME="$2"; shift 2 ;;
    --file) DUMP_FILE="$2"; shift 2 ;;
    --create-db) CREATE_DB=1; shift ;;
    *) echo "Unknown arg: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "${DB_NAME}" || -z "${DUMP_FILE}" ]]; then
  echo "Usage: $0 --db NAME --file dump [--host H] [--port P] [--user U] [--create-db]" >&2
  exit 2
fi
if [[ ! -f "${DUMP_FILE}" ]]; then
  echo "ERROR: dump not found: ${DUMP_FILE}" >&2
  exit 1
fi
if [[ -z "${PGPASSWORD:-}" && -z "${POSTGRES_PASSWORD:-}" ]]; then
  echo "ERROR: set PGPASSWORD or POSTGRES_PASSWORD" >&2
  exit 1
fi
export PGPASSWORD="${PGPASSWORD:-${POSTGRES_PASSWORD}}"

# Optional checksum verify if sibling checksums.sha256 exists
DUMP_DIR="$(dirname "${DUMP_FILE}")"
BASE="$(basename "${DUMP_FILE}")"
if [[ -f "${DUMP_DIR}/checksums.sha256" ]]; then
  if command -v sha256sum >/dev/null 2>&1; then
    expected="$(awk -v f="${BASE}" '$2==f {print $1}' "${DUMP_DIR}/checksums.sha256")"
    actual="$(sha256sum "${DUMP_FILE}" | awk '{print $1}')"
    if [[ -n "${expected}" && "${expected}" != "${actual}" ]]; then
      echo "ERROR: checksum mismatch for ${BASE}" >&2
      exit 1
    fi
    echo "Checksum OK for ${BASE}"
  fi
fi

if [[ "${CREATE_DB}" -eq 1 ]]; then
  psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d postgres -v ON_ERROR_STOP=1 \
    -c "SELECT 1 FROM pg_database WHERE datname='${DB_NAME}'" | grep -q 1 \
    || psql -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d postgres -v ON_ERROR_STOP=1 \
      -c "CREATE DATABASE \"${DB_NAME}\""
fi

echo "Restoring ${DUMP_FILE} -> ${DB_NAME} @ ${HOST}:${PORT} ..."
pg_restore -h "${HOST}" -p "${PORT}" -U "${USER_NAME}" -d "${DB_NAME}" \
  --clean --if-exists --no-owner --no-acl "${DUMP_FILE}"
echo "Restore complete: ${DB_NAME}"
