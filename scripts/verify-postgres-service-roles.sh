#!/usr/bin/env bash
# IAM-004: prove service A cannot CONNECT to database B.
# Usage (against running postgres):
#   POSTGRES_HOST=localhost POSTGRES_PORT=5433 ./scripts/verify-postgres-service-roles.sh
set -euo pipefail

HOST="${POSTGRES_HOST:-localhost}"
PORT="${POSTGRES_PORT:-5432}"
ADMIN_USER="${POSTGRES_USER:-hrmanagement}"
ADMIN_PASS="${POSTGRES_PASSWORD:?POSTGRES_PASSWORD required}"
APP_PASS="${POSTGRES_APP_PASSWORD:-${ADMIN_PASS}}"

export PGPASSWORD="$APP_PASS"

echo "Expect FAIL connecting payroll_app → hr_core_db..."
if psql -h "$HOST" -p "$PORT" -U payroll_app -d hr_core_db -c "SELECT 1" >/dev/null 2>&1; then
  echo "FAIL: payroll_app was able to connect to hr_core_db" >&2
  exit 1
fi
echo "OK: payroll_app denied on hr_core_db"

echo "Expect OK connecting payroll_app → payroll_db..."
psql -h "$HOST" -p "$PORT" -U payroll_app -d payroll_db -c "SELECT 1" >/dev/null
echo "OK: payroll_app can connect to payroll_db"

echo "Expect FAIL connecting hr_core_app → payroll_db..."
if psql -h "$HOST" -p "$PORT" -U hr_core_app -d payroll_db -c "SELECT 1" >/dev/null 2>&1; then
  echo "FAIL: hr_core_app was able to connect to payroll_db" >&2
  exit 1
fi
echo "OK: hr_core_app denied on payroll_db"

echo "All cross-DB CONNECT checks passed."

# TEN-002C: an app role that owns its tables, or holds BYPASSRLS, silently defeats
# every tenant_isolation policy. Both must be false for isolation to mean anything.
export PGPASSWORD="$ADMIN_PASS"

echo "Checking no application role holds BYPASSRLS..."
bypass=$(psql -h "$HOST" -p "$PORT" -U "$ADMIN_USER" -d postgres -tAc \
  "SELECT string_agg(rolname, ', ') FROM pg_roles WHERE rolbypassrls AND NOT rolsuper AND rolname LIKE '%\_app';")
if [ -n "${bypass}" ]; then
  echo "FAIL: application roles hold BYPASSRLS: ${bypass}" >&2
  exit 1
fi
echo "OK: no application role holds BYPASSRLS"

echo "Checking application roles do not own tables..."
for pair in "hr_core_app:hr_core_db" "hr_contracts_app:hr_contracts_db" "payroll_app:payroll_db" \
            "hr_health_app:hr_health_db" "hr_movements_app:hr_movements_db" \
            "hr_training_app:hr_training_db" "idcard_app:idcard_db" "archive_app:archive_db" \
            "admin_app:admin_db" "it_infra_app:it_infra_db" "analytics_app:analytics_db" \
            "compliance_app:compliance_db" "auth_directory_app:auth_directory_db" \
            "file_storage_app:file_storage_db"; do
  role="${pair%%:*}"
  db="${pair##*:}"
  owned=$(psql -h "$HOST" -p "$PORT" -U "$ADMIN_USER" -d "$db" -tAc \
    "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
     WHERE n.nspname = 'public' AND c.relkind = 'r'
       AND pg_catalog.pg_get_userbyid(c.relowner) = '${role}';" 2>/dev/null || echo "0")
  if [ "${owned}" != "0" ]; then
    echo "FAIL: ${role} owns ${owned} table(s) in ${db}; owners bypass RLS unless FORCE is set." >&2
    exit 1
  fi
done
echo "OK: no application role owns tables"

echo "Checking tenant RLS is enabled, forced, and has WITH CHECK..."
for db in hr_core_db hr_contracts_db payroll_db hr_health_db hr_movements_db hr_training_db \
          idcard_db archive_db admin_db it_infra_db analytics_db compliance_db \
          auth_directory_db file_storage_db; do
  bad=$(psql -h "$HOST" -p "$PORT" -U "$ADMIN_USER" -d "$db" -tAc \
    "SELECT string_agg(c.relname, ', ')
     FROM pg_class c
     JOIN pg_namespace n ON n.oid = c.relnamespace
     JOIN pg_attribute a ON a.attrelid = c.oid
     WHERE n.nspname = 'public' AND c.relkind = 'r'
       AND a.attname = 'TenantId' AND a.attnum > 0 AND NOT a.attisdropped
       AND c.relname NOT IN ('Tenants','TenantAssignmentRules','TenantQuarantineEntries',
                             'SodRules','ConflictingRolePairs','AuditLogs','EntityAuditLogs')
       AND c.relname NOT LIKE 'InboxState%'
       AND c.relname NOT LIKE 'OutboxState%'
       AND c.relname NOT LIKE 'OutboxMessage%'
       AND (NOT c.relrowsecurity OR NOT c.relforcerowsecurity
            OR NOT EXISTS (SELECT 1 FROM pg_policy p
                           WHERE p.polrelid = c.oid AND p.polname = 'tenant_isolation'
                             AND p.polwithcheck IS NOT NULL));" 2>/dev/null || echo "")
  if [ -n "${bad}" ]; then
    echo "FAIL: ${db} tenant isolation not enforced on: ${bad}" >&2
    exit 1
  fi
done
echo "OK: tenant RLS enforced (enabled + forced + WITH CHECK)"

echo "TEN-002C verification passed."
