#!/usr/bin/env bash
# Apply EF Core migrations for all HRManagement services (run before production deploy).
# Prefers pre-built efbundles under EFBUNDLE_DIR (default: artifacts/efbundles) when present.
# Requires: dotnet SDK (fallback path), PostgreSQL reachable via connection strings.
# DEP-001 / DB-001: advisory lock prevents concurrent migration runs; includes FileStorage.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}"

EFBUNDLE_DIR="${EFBUNDLE_DIR:-${ROOT}/artifacts/efbundles}"

# Advisory lock key (stable for HRManagement migrations; shared with TryMigrateAsync).
LOCK_CONN="${MIGRATION_LOCK_CONNECTION:-${ConnectionStrings__GatewayAuditDb:-${ConnectionStrings__HRCoreDb:-}}}"
LOCK_KEY=8675309
LOCK_HOLDER_PID=""

release_lock() {
  if [ -n "${LOCK_HOLDER_PID}" ]; then
    kill "${LOCK_HOLDER_PID}" 2>/dev/null || true
    wait "${LOCK_HOLDER_PID}" 2>/dev/null || true
    LOCK_HOLDER_PID=""
    echo "Migration advisory lock released."
  fi
}

acquire_lock() {
  if [ -z "${LOCK_CONN}" ]; then
    echo "WARNING: No lock connection string set; skipping advisory lock."
    return 0
  fi
  if ! command -v psql >/dev/null 2>&1; then
    echo "WARNING: psql not available; skipping advisory lock."
    return 0
  fi

  echo "Acquiring migration advisory lock (${LOCK_KEY}) ..."
  # Hold the session-scoped advisory lock in a background psql until EXIT.
  split_conn "${LOCK_CONN}"
  PGPASSWORD="${CONN_PW}" psql "${CONN_INFO}" -v ON_ERROR_STOP=1 -q <<SQL &
SELECT pg_advisory_lock(${LOCK_KEY});
SELECT pg_sleep(86400);
SQL
  LOCK_HOLDER_PID=$!
  trap release_lock EXIT

  # Wait briefly and confirm the holder is still alive (lock acquired).
  sleep 2
  if ! kill -0 "${LOCK_HOLDER_PID}" 2>/dev/null; then
    echo "ERROR: Could not acquire migration advisory lock (holder exited)."
    LOCK_HOLDER_PID=""
    exit 1
  fi
  echo "Migration advisory lock acquired (holder pid=${LOCK_HOLDER_PID})."
}

# name|project|startup|ConnectionStrings__ENV
declare -a MIGRATIONS=(
  "gateway|src/Gateway/HRManagement.Gateway|src/Gateway/HRManagement.Gateway|ConnectionStrings__GatewayAuditDb"
  "hr-core|src/Services/HRCore/HR.Core.Infrastructure|src/Services/HRCore/HR.Core.Web|ConnectionStrings__HRCoreDb"
  "hr-contracts|src/Services/HRContracts/HR.Contracts.Infrastructure|src/Services/HRContracts/HR.Contracts.Web|ConnectionStrings__ContractsDb"
  "payroll|src/Services/Payroll/Payroll.Infrastructure|src/Services/Payroll/Payroll.Web|ConnectionStrings__PayrollDb"
  "hr-health|src/Services/HRHealth/HR.Health.Infrastructure|src/Services/HRHealth/HR.Health.Web|ConnectionStrings__HealthDb"
  "hr-movements|src/Services/HRMovements/HR.Movements.Infrastructure|src/Services/HRMovements/HR.Movements.Web|ConnectionStrings__MovementsDb"
  "hr-training|src/Services/HRTraining/HR.Training.Infrastructure|src/Services/HRTraining/HR.Training.Web|ConnectionStrings__TrainingDb"
  "idcard|src/Services/IDCard/IDCard.Infrastructure|src/Services/IDCard/IDCard.Web|ConnectionStrings__IDCardDb"
  "records-archive|src/Services/RecordsArchive/Records.Archive.Infrastructure|src/Services/RecordsArchive/Records.Archive.Web|ConnectionStrings__ArchiveDb"
  "admin-logistics|src/Services/AdminLogistics/Admin.Logistics.Infrastructure|src/Services/AdminLogistics/Admin.Logistics.Web|ConnectionStrings__AdminDb"
  "it-infra|src/Services/ITInfrastructure/IT.Infra.Infrastructure|src/Services/ITInfrastructure/IT.Infra.Web|ConnectionStrings__ITInfraDb"
  "dev-analytics|src/Services/DevAnalytics/Dev.Analytics.Infrastructure|src/Services/DevAnalytics/Dev.Analytics.Web|ConnectionStrings__AnalyticsDb"
  "compliance|src/Services/Compliance/Compliance.Infrastructure|src/Services/Compliance/Compliance.Web|ConnectionStrings__ComplianceDb"
  "auth-directory|src/Services/AuthDirectory/Auth.Directory.Infrastructure|src/Services/AuthDirectory/Auth.Directory.Web|ConnectionStrings__AuthDirectoryDb"
  "file-storage|src/Services/FileStorage/File.Storage.Infrastructure|src/Services/FileStorage/File.Storage.Web|ConnectionStrings__FileStorageDb"
)

# PLAN-38 (review F4): no password on a command line. split_conn takes an Npgsql connection string ("Host=..;Password=..")
# and sets CONN_NO_PW (the same string without Password), CONN_INFO (libpq keywords for psql) and CONN_PW. Callers pass
# CONN_PW as PGPASSWORD, which both Npgsql (the bundles) and libpq read from the environment. Generated passwords carry
# no ';' (a quoted Npgsql value with ';' is not supported here).
split_conn() {
  CONN_NO_PW=""; CONN_INFO=""; CONN_PW=""
  local part key val
  local -a parts
  IFS=';' read -ra parts <<< "$1"
  for part in "${parts[@]}"; do
    [ -n "${part//[[:space:]]/}" ] || continue
    key="${part%%=*}"; key="${key//[[:space:]]/}"; key="${key,,}"
    val="${part#*=}"
    case "${key}" in
      password|pwd) CONN_PW="${val}"; continue;;
      host|server) CONN_INFO+=" host=${val}";;
      port) CONN_INFO+=" port=${val}";;
      database|db) CONN_INFO+=" dbname=${val}";;
      username|userid|user) CONN_INFO+=" user=${val}";;
      sslmode) val="${val//[[:space:]]/}"; val="${val,,}"; [ "${val}" = verifyfull ] && val=verify-full; [ "${val}" = verifyca ] && val=verify-ca
               CONN_INFO+=" sslmode=${val}";;
    esac
    CONN_NO_PW+="${part};"
  done
  CONN_INFO="${CONN_INFO# }"
}

acquire_lock

HARDEN_SQL="${ROOT}/scripts/sql/ten002c-harden-tenant-rls.sql"

# TEN-002C: RLS must be applied by the table owner, so it runs on the migrator connection
# immediately after each context migrates. Verification failure aborts the whole run.
harden_tenant_rls() {
  local name="$1"
  local conn="$2"

  if [ -z "${conn}" ]; then
    echo "ERROR: cannot harden tenant RLS for ${name} (no connection string)."
    exit 1
  fi
  if ! command -v psql >/dev/null 2>&1; then
    echo "ERROR: psql is required to apply TEN-002C tenant RLS hardening."
    exit 1
  fi

  echo "Hardening tenant RLS for ${name} ..."
  split_conn "${conn}"
  if ! PGPASSWORD="${CONN_PW}" psql "${CONN_INFO}" -v ON_ERROR_STOP=1 -q -f "${HARDEN_SQL}"; then
    echo "ERROR: tenant RLS hardening failed for ${name}"
    exit 1
  fi
}

for entry in "${MIGRATIONS[@]}"; do
  IFS='|' read -r name project startup conn_env <<< "${entry}"

  # Prefer the migrator role: it owns the tables, so it can ALTER ... FORCE ROW LEVEL SECURITY.
  migrator_env="${conn_env}Migrator"
  migrate_conn="${!migrator_env:-${!conn_env:-}}"

  bundle="${EFBUNDLE_DIR}/${name}"
  if [ -x "${bundle}" ] || [ -f "${bundle}.exe" ]; then
    exe="${bundle}"
    [ -f "${bundle}.exe" ] && exe="${bundle}.exe"
    conn_val="${migrate_conn}"
    echo "Migrating ${name} via efbundle ..."
    if [ -n "${conn_val}" ]; then
      split_conn "${conn_val}"
      if ! PGPASSWORD="${CONN_PW}" "${exe}" --connection "${CONN_NO_PW}"; then
        echo "ERROR: Bundle migration failed for ${name}"
        exit 1
      fi
    else
      if ! "${exe}"; then
        echo "ERROR: Bundle migration failed for ${name} (no ${conn_env})"
        exit 1
      fi
    fi
  else
    echo "Migrating ${project} via dotnet ef (no bundle at ${bundle}) ..."
    split_conn "${migrate_conn}"
    if ! PGPASSWORD="${CONN_PW}" dotnet ef database update --project "${project}" --startup-project "${startup}" --configuration Release \
         ${CONN_NO_PW:+--connection "${CONN_NO_PW}"}; then
      echo "ERROR: Migration failed for ${project}"
      exit 1
    fi
  fi

  harden_tenant_rls "${name}" "${migrate_conn}"
done

echo "All migrations applied and tenant RLS verified."
