#!/bin/bash
# IAM-004: per-service PostgreSQL roles (runs on first postgres init only).
# Mounted as /docker-entrypoint-initdb.d/02-init-service-roles.sh
set -euo pipefail

# Password fallback: service-specific env → POSTGRES_APP_PASSWORD → POSTGRES_PASSWORD
pass_for() {
  local specific="$1"
  if [ -n "${specific:-}" ]; then
    echo "$specific"
  elif [ -n "${POSTGRES_APP_PASSWORD:-}" ]; then
    echo "$POSTGRES_APP_PASSWORD"
  else
    echo "${POSTGRES_PASSWORD}"
  fi
}

create_service_roles() {
  local role_base="$1"
  local db="$2"
  local app_pass="$3"
  local mig_pass="$4"
  local app_role="${role_base}_app"
  local mig_role="${role_base}_migrator"

  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<-EOSQL
    DO \$\$
    BEGIN
      IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '${app_role}') THEN
        CREATE ROLE ${app_role} LOGIN PASSWORD '${app_pass}';
      END IF;
      IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '${mig_role}') THEN
        CREATE ROLE ${mig_role} LOGIN PASSWORD '${mig_pass}';
      END IF;
    END
    \$\$;
EOSQL

  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$db" <<-EOSQL
    GRANT CONNECT ON DATABASE ${db} TO ${app_role};
    GRANT CONNECT ON DATABASE ${db} TO ${mig_role};
    GRANT USAGE, CREATE ON SCHEMA public TO ${mig_role};
    GRANT USAGE ON SCHEMA public TO ${app_role};
    ALTER DEFAULT PRIVILEGES FOR ROLE ${mig_role} IN SCHEMA public
      GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ${app_role};
    ALTER DEFAULT PRIVILEGES FOR ROLE ${mig_role} IN SCHEMA public
      GRANT USAGE, SELECT ON SEQUENCES TO ${app_role};
    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO ${app_role};
    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO ${app_role};

    -- TEN-002C: the app role must NOT own tables. PostgreSQL exempts a table's owner from
    -- row level security, so an owning app role silently bypasses tenant_isolation.
    REVOKE CREATE ON SCHEMA public FROM ${app_role};
    ALTER ROLE ${app_role} NOBYPASSRLS;
    DO \$\$
    BEGIN
      IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '${app_role}')
         AND EXISTS (
           SELECT 1 FROM pg_class c
           JOIN pg_namespace n ON n.oid = c.relnamespace
           WHERE n.nspname = 'public'
             AND c.relkind IN ('r', 'S')
             AND pg_catalog.pg_get_userbyid(c.relowner) = '${app_role}')
      THEN
        EXECUTE 'REASSIGN OWNED BY ${app_role} TO ${mig_role}';
      END IF;
    END
    \$\$;
EOSQL

  # Development convenience: EF migrate-at-startup runs on the app connection and needs DDL.
  # Opt-in only — never enable in production, it re-grants table ownership to the app role.
  if [ "${POSTGRES_ALLOW_APP_DDL:-false}" = "true" ]; then
    echo "  WARNING: POSTGRES_ALLOW_APP_DDL=true — granting DDL to ${app_role} (RLS FORCE still applies)."
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$db" \
      -c "GRANT CREATE ON SCHEMA public TO ${app_role};"
  fi
}

# Explicitly revoke cross-DB connect from app roles after all are created (defense in depth).
revoke_cross_db() {
  local role="$1"
  local own_db="$2"
  for db in hr_core_db hr_contracts_db hr_health_db hr_movements_db hr_training_db \
            payroll_db idcard_db archive_db admin_db it_infra_db analytics_db \
            compliance_db auth_directory_db file_storage_db gateway_audit_db keycloak_db; do
    if [ "$db" != "$own_db" ]; then
      psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
        -c "REVOKE CONNECT ON DATABASE ${db} FROM ${role};" 2>/dev/null || true
    fi
  done
}

echo "IAM-004: creating per-service PostgreSQL roles..."

create_service_roles hr_core hr_core_db \
  "$(pass_for "${POSTGRES_HR_CORE_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_HR_CORE_MIGRATOR_PASSWORD:-}")"
create_service_roles hr_contracts hr_contracts_db \
  "$(pass_for "${POSTGRES_HR_CONTRACTS_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_HR_CONTRACTS_MIGRATOR_PASSWORD:-}")"
create_service_roles hr_health hr_health_db \
  "$(pass_for "${POSTGRES_HR_HEALTH_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_HR_HEALTH_MIGRATOR_PASSWORD:-}")"
create_service_roles hr_movements hr_movements_db \
  "$(pass_for "${POSTGRES_HR_MOVEMENTS_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_HR_MOVEMENTS_MIGRATOR_PASSWORD:-}")"
create_service_roles hr_training hr_training_db \
  "$(pass_for "${POSTGRES_HR_TRAINING_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_HR_TRAINING_MIGRATOR_PASSWORD:-}")"
create_service_roles payroll payroll_db \
  "$(pass_for "${POSTGRES_PAYROLL_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_PAYROLL_MIGRATOR_PASSWORD:-}")"
create_service_roles idcard idcard_db \
  "$(pass_for "${POSTGRES_IDCARD_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_IDCARD_MIGRATOR_PASSWORD:-}")"
create_service_roles archive archive_db \
  "$(pass_for "${POSTGRES_ARCHIVE_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_ARCHIVE_MIGRATOR_PASSWORD:-}")"
create_service_roles admin admin_db \
  "$(pass_for "${POSTGRES_ADMIN_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_ADMIN_MIGRATOR_PASSWORD:-}")"
create_service_roles it_infra it_infra_db \
  "$(pass_for "${POSTGRES_IT_INFRA_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_IT_INFRA_MIGRATOR_PASSWORD:-}")"
create_service_roles analytics analytics_db \
  "$(pass_for "${POSTGRES_ANALYTICS_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_ANALYTICS_MIGRATOR_PASSWORD:-}")"
create_service_roles compliance compliance_db \
  "$(pass_for "${POSTGRES_COMPLIANCE_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_COMPLIANCE_MIGRATOR_PASSWORD:-}")"
create_service_roles auth_directory auth_directory_db \
  "$(pass_for "${POSTGRES_AUTH_DIRECTORY_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_AUTH_DIRECTORY_MIGRATOR_PASSWORD:-}")"
create_service_roles file_storage file_storage_db \
  "$(pass_for "${POSTGRES_FILE_STORAGE_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_FILE_STORAGE_MIGRATOR_PASSWORD:-}")"
create_service_roles gateway_audit gateway_audit_db \
  "$(pass_for "${POSTGRES_GATEWAY_AUDIT_APP_PASSWORD:-}")" \
  "$(pass_for "${POSTGRES_GATEWAY_AUDIT_MIGRATOR_PASSWORD:-}")"

for pair in \
  "hr_core_app:hr_core_db" "hr_contracts_app:hr_contracts_db" "hr_health_app:hr_health_db" \
  "hr_movements_app:hr_movements_db" "hr_training_app:hr_training_db" "payroll_app:payroll_db" \
  "idcard_app:idcard_db" "archive_app:archive_db" "admin_app:admin_db" "it_infra_app:it_infra_db" \
  "analytics_app:analytics_db" "compliance_app:compliance_db" "auth_directory_app:auth_directory_db" \
  "file_storage_app:file_storage_db" "gateway_audit_app:gateway_audit_db"; do
  role="${pair%%:*}"
  db="${pair##*:}"
  revoke_cross_db "$role" "$db"
done

echo "IAM-004: PostgreSQL service roles ready."
