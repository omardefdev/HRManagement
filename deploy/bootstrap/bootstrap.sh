#!/usr/bin/env bash
# PLAN-38 (§1): the one-shot first-boot and every-deploy initialiser of the data plane (compose service hrm-bootstrap,
# image hr-migrate). Every step is idempotent, so a second run changes nothing but the passwords it sets again:
#   1. wait for PostgreSQL
#   2. ensure-databases.sql      the 16 databases, no CONNECT for PUBLIC
#   3. ensure-roles.sql          <svc>_app and <svc>_migrator per database, passwords set on every run
#   4. migrations                each EF bundle as <svc>_migrator (both postures), then TEN-002C as the table owner
#   5. ensure-backup-role.sql    hrm_backup (read-only, BYPASSRLS for pg_dump)
#   6. RabbitMQ users            mq_* from rabbitmq-users.json through the management API, passwords set on every run
#   7. MinIO user                only with the minio profile (D-38-7)
#   8. result file               $HRM_BOOTSTRAP_RESULT_DIR/result-<utc>.json (names and counts, no secret)
# Any failure exits non-zero, so no application starts (depends_on: condition: service_completed_successfully).
#
# Secrets: every password comes from the environment and reaches psql through PGPASSWORD / \getenv and the bundles
# through PGPASSWORD (Npgsql reads it); the RabbitMQ credentials go through a private curl config on stdin and a
# mode-600 body file. Nothing secret is on a command line or printed.
set -euo pipefail
umask 077

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUNDLE_DIR="${EFBUNDLE_DIR:-/app/efbundles}"
HARDEN_SQL="${HRM_HARDEN_SQL:-/app/scripts/sql/ten002c-harden-tenant-rls.sql}"
RESULT_DIR="${HRM_BOOTSTRAP_RESULT_DIR:-/var/hrm/bootstrap}"
ROLES_CONF="$HERE/postgres-roles.conf"
MQ_USERS="$HERE/rabbitmq-users.json"
WAIT_SECONDS="${HRM_BOOTSTRAP_WAIT_SECONDS:-180}"
export PGHOST="${PGHOST:-postgresql}" PGPORT="${PGPORT:-5432}"
ADMIN_USER="${POSTGRES_USER:?POSTGRES_USER required}"
: "${POSTGRES_PASSWORD:?POSTGRES_PASSWORD required}"

STARTED="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
T0="$(date +%s)"
STEP="start"
SERVICES_JSON="[]"
MQ_JSON="[]"
MINIO_RESULT="skipped"

log() { printf '[bootstrap] %s\n' "$*"; }
die() { printf '[bootstrap] ERROR (%s): %s\n' "$STEP" "$*" >&2; exit 1; }
on_exit() {
  local rc=$?
  if [ "$rc" -ne 0 ]; then printf '[bootstrap] FAILED in step %s (exit %s)\n' "$STEP" "$rc" >&2; fi
}
trap on_exit EXIT

secret() { # value of the variable named $1, or die naming the variable (never the value)
  local v="${!1:-}"
  [ -n "$v" ] || die "$1 is empty or not set"
  printf '%s' "$v"
}

admin_psql() { PGPASSWORD="$POSTGRES_PASSWORD" psql -X -q -v ON_ERROR_STOP=1 -U "$ADMIN_USER" "$@"; }

# ---- 0. every secret present (checked up front: a failing $(secret ...) inside an assignment would not stop set -e) ---
STEP="0-environment"
missing=""
need() { [ -n "${!1:-}" ] || missing="$missing $1"; }
while IFS='|' read -r bundle base db prefix; do
  case "$bundle" in ''|'#'*) continue;; esac
  need "${prefix}_APP_PASSWORD"; need "${prefix}_MIGRATOR_PASSWORD"
done < "$ROLES_CONF"
need POSTGRES_BACKUP_PASSWORD
if [ -f "$MQ_USERS" ]; then
  need RABBITMQ_DEFAULT_USER; need RABBITMQ_DEFAULT_PASS
  for v in $(jq -r '.users[].passwordEnv' "$MQ_USERS"); do need "$v"; done
fi
[ -z "$missing" ] || die "missing or empty variable(s):$missing"
log "step 0: all role and broker passwords present (values not shown)"

# ---- 1. wait for PostgreSQL ------------------------------------------------------------------------------------------
STEP="1-wait-postgresql"
deadline=$(( $(date +%s) + WAIT_SECONDS ))
until admin_psql -d postgres -Atc 'SELECT 1' >/dev/null 2>&1; do
  [ "$(date +%s)" -lt "$deadline" ] || die "PostgreSQL at $PGHOST:$PGPORT not reachable after ${WAIT_SECONDS} s"
  sleep 2
done
log "step 1: PostgreSQL $PGHOST:$PGPORT reachable"

# ---- 2. databases ----------------------------------------------------------------------------------------------------
STEP="2-databases"
admin_psql -d postgres -f "$HERE/ensure-databases.sql"
DB_COUNT="$(admin_psql -d postgres -Atc "SELECT count(*) FROM pg_database WHERE datname IN ('keycloak_db','gateway_audit_db','hr_core_db','hr_contracts_db','hr_health_db','hr_movements_db','hr_training_db','payroll_db','idcard_db','archive_db','admin_db','it_infra_db','analytics_db','compliance_db','auth_directory_db','file_storage_db')")"
[ "$DB_COUNT" = "16" ] || die "expected 16 databases, found $DB_COUNT"
log "step 2: 16 databases present"

# ---- 3. roles ---------------------------------------------------------------------------------------------------------
STEP="3-roles"
roles=0
while IFS='|' read -r bundle base db prefix; do
  case "$bundle" in ''|'#'*) continue;; esac
  HRM_APP_PASSWORD="$(secret "${prefix}_APP_PASSWORD")" HRM_MIGRATOR_PASSWORD="$(secret "${prefix}_MIGRATOR_PASSWORD")" \
    admin_psql -d postgres -v role_base="$base" -v db="$db" -f "$HERE/ensure-roles.sql"
  roles=$((roles + 2))
done < "$ROLES_CONF"
log "step 3: $roles roles ensured (${roles} passwords set)"

# ---- 4. migrations as <svc>_migrator ----------------------------------------------------------------------------------
STEP="4-migrations"
history_count() { # <db> <role> <password-variable>
  # Two statements: PostgreSQL resolves the table of a subquery even in a CASE branch that is never taken.
  local exists
  exists="$(PGPASSWORD="$(secret "$3")" psql -X -q -v ON_ERROR_STOP=1 -U "$2" -d "$1" -Atc \
    "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL")"
  if [ "$exists" = "t" ]; then
    PGPASSWORD="$(secret "$3")" psql -X -q -v ON_ERROR_STOP=1 -U "$2" -d "$1" -Atc 'SELECT count(*) FROM "__EFMigrationsHistory"'
  else
    echo 0
  fi
}
applied_total=0
while IFS='|' read -r bundle base db prefix; do
  case "$bundle" in ''|'#'*) continue;; esac
  STEP="4-migrations:$bundle"
  role="${base}_migrator"
  exe="$BUNDLE_DIR/$bundle"
  [ -x "$exe" ] || die "bundle $exe not found or not executable"
  before="$(history_count "$db" "$role" "${prefix}_MIGRATOR_PASSWORD")"
  # No password in the connection string: Npgsql takes it from PGPASSWORD (set for this process only).
  if ! PGPASSWORD="$(secret "${prefix}_MIGRATOR_PASSWORD")" "$exe" \
      --connection "Host=$PGHOST;Port=$PGPORT;Database=$db;Username=$role" > "/tmp/bundle-$bundle.log" 2>&1; then
    tail -40 "/tmp/bundle-$bundle.log" >&2
    die "migration bundle $bundle failed as $role"
  fi
  after="$(history_count "$db" "$role" "${prefix}_MIGRATOR_PASSWORD")"
  applied=$((after - before))
  applied_total=$((applied_total + applied))
  PGPASSWORD="$(secret "${prefix}_MIGRATOR_PASSWORD")" psql -X -q -v ON_ERROR_STOP=1 -U "$role" -d "$db" -f "$HARDEN_SQL" \
    > "/tmp/harden-$bundle.log" 2>&1 || { cat "/tmp/harden-$bundle.log" >&2; die "TEN-002C hardening failed for $db"; }
  log "step 4: $bundle migrated by $role: $applied migrations applied ($after in history); TEN-002C verified"
  SERVICES_JSON="$(jq -c --arg b "$bundle" --arg d "$db" --arg r "$role" --argjson a "$applied" --argjson t "$after" \
    '. + [{bundle:$b, database:$d, migrationRole:$r, migrationsApplied:$a, migrationsInHistory:$t}]' <<< "$SERVICES_JSON")"
done < "$ROLES_CONF"
STEP="4-migrations"
log "step 4: $applied_total migrations applied in total"

# ---- 5. backup role ---------------------------------------------------------------------------------------------------
STEP="5-backup-role"
HRM_BACKUP_PASSWORD="$(secret POSTGRES_BACKUP_PASSWORD)" admin_psql -d postgres -f "$HERE/ensure-backup-role.sql"
log "step 5: hrm_backup ensured (read-only, BYPASSRLS, CONNECT on the 16 databases)"

# ---- 6. RabbitMQ users ------------------------------------------------------------------------------------------------
STEP="6-rabbitmq-users"
MQ_API="${HRM_RABBITMQ_API:-http://rabbitmq:15672/api}"
mq_call() { # <method> <path> [body-file]; admin credentials through a curl config on stdin, never on the command line
  local method="$1" path="$2" body="${3:-}" esc_user esc_pass code
  esc_user="$(secret RABBITMQ_DEFAULT_USER | sed 's/\\/\\\\/g; s/"/\\"/g')"
  esc_pass="$(secret RABBITMQ_DEFAULT_PASS | sed 's/\\/\\\\/g; s/"/\\"/g')"
  local args=(-sS -o /tmp/mq-response -w '%{http_code}' -K - -X "$method" -H 'content-type: application/json' "$MQ_API$path")
  [ -n "$body" ] && args+=(--data-binary "@$body")
  code="$(printf 'user = "%s:%s"\n' "$esc_user" "$esc_pass" | curl "${args[@]}")" || return 1
  case "$code" in 2*) return 0;; *) printf '[bootstrap] RabbitMQ API %s %s -> HTTP %s\n' "$method" "$path" "$code" >&2; return 1;; esac
}
if [ -f "$MQ_USERS" ]; then
  deadline=$(( $(date +%s) + WAIT_SECONDS ))
  until mq_call GET /overview 2>/dev/null; do
    [ "$(date +%s)" -lt "$deadline" ] || die "RabbitMQ management API $MQ_API not reachable after ${WAIT_SECONDS} s"
    sleep 2
  done
  vhost="$(jq -r '.vhost' "$MQ_USERS")"
  vhost_enc="$(jq -rn --arg v "$vhost" '$v|@uri')"
  count="$(jq '.users | length' "$MQ_USERS")"
  for i in $(seq 0 $((count - 1))); do
    user="$(jq -r ".users[$i].user" "$MQ_USERS")"
    pass_var="$(jq -r ".users[$i].passwordEnv" "$MQ_USERS")"
    body=/tmp/mq-body.json
    jq -n --arg p "$(secret "$pass_var")" '{password:$p, tags:""}' > "$body"
    mq_call PUT "/users/$user" "$body" || { rm -f "$body"; die "could not create or update RabbitMQ user $user"; }
    rm -f "$body"
    jq -c ".users[$i].permissions | {configure, write, read}" "$MQ_USERS" > "$body"
    mq_call PUT "/permissions/$vhost_enc/$user" "$body" || die "could not set permissions of $user"
    rm -f "$body"
    MQ_JSON="$(jq -c --arg u "$user" '. + [$u]' <<< "$MQ_JSON")"
  done
  log "step 6: $count RabbitMQ service users ensured on vhost $vhost (passwords set, permissions from rabbitmq-users.json)"
else
  log "step 6: skipped ($MQ_USERS not present)"
fi

# ---- 7. MinIO user (D-38-7) -------------------------------------------------------------------------------------------
STEP="7-minio"
if [ "${HRM_MINIO_ENABLED:-false}" = "true" ]; then
  # The image carries no MinIO client: no MINIO_IMAGE is vetted yet (D-38-7, decision-log). Refuse rather than start
  # File.Storage on MinIO without its own user.
  die "HRM_MINIO_ENABLED=true but MinIO user provisioning is not available in this release (D-38-7)"
fi
log "step 7: skipped (minio profile not enabled)"

# ---- 8. result file ---------------------------------------------------------------------------------------------------
STEP="8-result"
FINISHED="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
mkdir -p "$RESULT_DIR"
result="$RESULT_DIR/result-$(date -u +%Y%m%dT%H%M%SZ).json"
jq -n --arg s "$STARTED" --arg f "$FINISHED" --argjson d "$(( $(date +%s) - T0 ))" \
  --argjson svc "$SERVICES_JSON" --argjson mq "$MQ_JSON" --arg minio "$MINIO_RESULT" --argjson total "$applied_total" \
  '{status:"ok", startedUtc:$s, finishedUtc:$f, durationSeconds:$d, databases:16, migrationsAppliedTotal:$total,
    services:$svc, backupRole:"hrm_backup", rabbitmqUsers:$mq, minio:$minio}' > "$result"
log "step 8: result $result"
cat "$result"
log "done in $(( $(date +%s) - T0 )) s"
