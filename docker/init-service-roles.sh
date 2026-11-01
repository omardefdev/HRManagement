#!/bin/bash
# IAM-004 / PLAN-38: per-service PostgreSQL roles at the first initialisation of the postgres container (mounted as
# /docker-entrypoint-initdb.d/02-init-service-roles.sh). A thin wrapper over deploy/bootstrap/ensure-roles.sql, the
# same idempotent SQL the hrm-bootstrap init container runs on every deploy (mounted at /opt/hrm-bootstrap), so the
# development path and production create the roles the same way.
# Passwords: <PREFIX>_APP_PASSWORD / <PREFIX>_MIGRATOR_PASSWORD, falling back (development only) to
# POSTGRES_APP_PASSWORD, then POSTGRES_PASSWORD. The production chain passes every per-service password, and
# hrm-bootstrap sets them again on every deploy.
set -euo pipefail

BOOTSTRAP_DIR="${HRM_BOOTSTRAP_SQL_DIR:-/opt/hrm-bootstrap}"

pass_for() {
  local specific="${!1:-}"
  if [ -n "$specific" ]; then
    printf '%s' "$specific"
  elif [ -n "${POSTGRES_APP_PASSWORD:-}" ]; then
    printf '%s' "$POSTGRES_APP_PASSWORD"
  else
    printf '%s' "$POSTGRES_PASSWORD"
  fi
}

echo "IAM-004: creating per-service PostgreSQL roles (ensure-roles.sql)..."
psql -X -q -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres -f "$BOOTSTRAP_DIR/ensure-databases.sql"

while IFS='|' read -r bundle base db prefix; do
  case "$bundle" in ''|'#'*) continue;; esac
  HRM_APP_PASSWORD="$(pass_for "${prefix}_APP_PASSWORD")" HRM_MIGRATOR_PASSWORD="$(pass_for "${prefix}_MIGRATOR_PASSWORD")" \
    psql -X -q -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
      -v role_base="$base" -v db="$db" -f "$BOOTSTRAP_DIR/ensure-roles.sql"

  # Development convenience: EF migrate-at-startup runs on the app connection and needs DDL.
  # Opt-in only — never enable in production, it re-grants table ownership to the app role.
  if [ "${POSTGRES_ALLOW_APP_DDL:-false}" = "true" ]; then
    echo "  WARNING: POSTGRES_ALLOW_APP_DDL=true — granting DDL to ${base}_app (RLS FORCE still applies)."
    psql -X -q -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$db" \
      -c "GRANT CREATE ON SCHEMA public TO ${base}_app;"
  fi
done < "$BOOTSTRAP_DIR/postgres-roles.conf"

echo "IAM-004: PostgreSQL service roles ready."
