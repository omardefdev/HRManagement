#!/usr/bin/env bash
# PLAN-38 (section 3, P-1, D-38-5): fills only the MISSING keys of the single host's .env (/opt/hrm/shared/.env, mode
# 600, owner hrm) and never overwrites a value. Run on the server from a release directory, as hrm:
#
#   cd /opt/hrm/releases/<tag> && bash deploy/vps/init-secrets.sh [--env-file F] [--set KEY=VALUE ...] [--check]
#
# Required keys = every ${VAR:?} of the single-host compose chain + every key of deploy/vps/.env.vps.example + the role
# and broker matrices (deploy/bootstrap/postgres-roles.conf, rabbitmq-users.json). For each missing or empty key:
#   - a secret (name ends in PASSWORD, _PASS, SECRET, _KEY or TOKEN) is generated: 32 random bytes, base64url (openssl);
#   - a non-secret takes the example's non-empty default (e.g. HRM_DEPLOYMENT_MODE=demo);
#   - a non-secret without a default (APP_DOMAIN, SSO_DOMAIN, HRM_IMAGE_REPO) is REQUIRED, not generated: give it with
#     --set KEY=VALUE (refused for secret names, so no secret is ever on a command line) or the run fails, writing nothing.
# Prints key NAMES only, never a value. --check writes nothing and exits 1 when anything is missing.
set -euo pipefail
umask 077

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RELEASE="${HRM_RELEASE_DIR:-$(cd "$HERE/../.." && pwd)}"
ENV_FILE="${HRM_ENV_FILE:-/opt/hrm/shared/.env}"
EXAMPLE="$RELEASE/deploy/vps/.env.vps.example"
CHAIN="docker-compose.yml docker-compose.services.yml docker-compose.images.yml docker-compose.production.yml docker-compose.vps.yml docker-compose.vps-demo.yml"
ALLOW_EMPTY=" ACME_EMAIL ADMIN_ALLOWED_CIDRS IMAGE_TAG "
CHECK=0
declare -A SET=()

log() { printf '[init-secrets] %s\n' "$*"; }
die() { printf '[init-secrets] ERROR: %s\n' "$*" >&2; exit 2; }
is_secret() { case "$1" in *PASSWORD|*_PASS|*SECRET|*_KEY|*TOKEN) return 0;; *) return 1;; esac; }

while [ $# -gt 0 ]; do
  case "$1" in
    --env-file) ENV_FILE="${2:?--env-file needs a path}"; shift 2;;
    --set)
      kv="${2:?--set needs KEY=VALUE}"; k="${kv%%=*}"
      [[ "$k" =~ ^[A-Z][A-Z0-9_]*$ ]] || die "--set: invalid key name"
      is_secret "$k" && die "--set refuses secret key $k (secrets are generated, never passed on a command line)"
      SET["$k"]="${kv#*=}"; shift 2;;
    --check) CHECK=1; shift;;
    *) die "unknown option $1 (usage: init-secrets.sh [--env-file F] [--set KEY=VALUE ...] [--check])";;
  esac
done

# A target inside a git work tree must be git-ignored (never generate secrets into a tracked file).
env_dir="$(dirname "$ENV_FILE")"
if [ -d "$env_dir" ] && command -v git >/dev/null 2>&1 && git -C "$env_dir" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  git -C "$env_dir" check-ignore -q "$ENV_FILE" || die "$ENV_FILE is inside a git work tree and not git-ignored"
fi
[ -f "$EXAMPLE" ] || die "$EXAMPLE not found (run from a release directory)"

# ---- required key names -----------------------------------------------------------------------------------------
keys="$(
  {
    for f in $CHAIN; do [ -f "$RELEASE/$f" ] && grep -ohE '\$\{[A-Z_][A-Z0-9_]*:\?' "$RELEASE/$f" | sed -E 's/^\$\{//; s/:\?$//'; done
    grep -E '^[A-Z][A-Z0-9_]*=' "$EXAMPLE" | cut -d= -f1
    if [ -f "$RELEASE/deploy/bootstrap/postgres-roles.conf" ]; then
      grep -vE '^(#|$)' "$RELEASE/deploy/bootstrap/postgres-roles.conf" | cut -d'|' -f4 \
        | while read -r p; do echo "${p}_APP_PASSWORD"; echo "${p}_MIGRATOR_PASSWORD"; done
    fi
    if [ -f "$RELEASE/deploy/bootstrap/rabbitmq-users.json" ]; then
      grep -oE '"passwordEnv"[[:space:]]*:[[:space:]]*"[A-Z0-9_]+"' "$RELEASE/deploy/bootstrap/rabbitmq-users.json" | grep -oE '[A-Z0-9_]+"$' | tr -d '"'
    fi
  } | sort -u
)"

current() { # value of KEY in the env file (last assignment wins), without printing it
  [ -f "$ENV_FILE" ] || return 0
  awk -v k="$1" -F= '$1==k { v=substr($0, length(k)+2) } END { gsub(/^"|"$/, "", v); printf "%s", v }' "$ENV_FILE"
}
has_line() { [ -f "$ENV_FILE" ] && grep -qE "^$1=" "$ENV_FILE"; }
example_default() { awk -v k="$1" -F= '$1==k { v=substr($0, length(k)+2) } END { printf "%s", v }' "$EXAMPLE"; }

declare -A NEW=()
generated=() defaults=() set_keys=() empties=() missing=() kept=0
for k in $keys; do
  if [ -n "$(current "$k")" ]; then kept=$((kept + 1)); continue; fi
  if [ -n "${SET[$k]+x}" ]; then NEW["$k"]="${SET[$k]}"; set_keys+=("$k"); continue; fi
  if is_secret "$k"; then
    NEW["$k"]="$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=\n')"; generated+=("$k"); continue
  fi
  d="$(example_default "$k")"
  if [ -n "$d" ]; then NEW["$k"]="$d"; defaults+=("$k"); continue; fi
  case "$ALLOW_EMPTY" in *" $k "*) has_line "$k" || { NEW["$k"]=""; empties+=("$k"); }; continue;; esac
  missing+=("$k")
done

[ ${#missing[@]} -eq 0 ] || die "required non-secret key(s) without a value (give them with --set KEY=VALUE): ${missing[*]}"
if [ "$CHECK" = "1" ]; then
  [ ${#NEW[@]} -eq 0 ] && { log "check: all $(echo "$keys" | wc -w) keys present (values not shown)"; exit 0; }
  log "check: missing: ${!NEW[*]}"; exit 1
fi

if [ ${#NEW[@]} -gt 0 ]; then
  [ -f "$ENV_FILE" ] || { mkdir -p "$env_dir"; : > "$ENV_FILE"; }
  chmod 600 "$ENV_FILE"
  tmp="$(mktemp "$ENV_FILE.XXXXXX")"
  # Keep every existing line; fill an existing empty KEY= line in place; append the rest. Values travel through the
  # environment of awk (ENVIRON), never through its command line.
  for k in "${!NEW[@]}"; do printf '%s\n' "$k"; done > "$tmp.keys"
  export_vals() { for k in "${!NEW[@]}"; do export "HRM_INIT_$k=${NEW[$k]}"; done; }
  export_vals
  awk -v keysfile="$tmp.keys" -F= '
    BEGIN { while ((getline k < keysfile) > 0) want[k] = 1 }
    { if (($1 in want) && substr($0, length($1) + 2) == "") { print $1 "=" ENVIRON["HRM_INIT_" $1]; done[$1] = 1; next } print }
    END { for (k in want) if (!(k in done)) print k "=" ENVIRON["HRM_INIT_" k] }
  ' "$ENV_FILE" > "$tmp"
  rm -f "$tmp.keys"
  chmod 600 "$tmp"
  mv "$tmp" "$ENV_FILE"
fi
chmod 600 "$ENV_FILE"

log "env file $ENV_FILE (mode $(stat -c '%a' "$ENV_FILE" 2>/dev/null || echo 600)): $kept key(s) kept unchanged"
[ ${#generated[@]} -eq 0 ] || log "generated (${#generated[@]}): ${generated[*]}"
[ ${#defaults[@]} -eq 0 ] || log "example defaults (${#defaults[@]}): ${defaults[*]}"
[ ${#set_keys[@]} -eq 0 ] || log "set from --set (${#set_keys[@]}): ${set_keys[*]}"
[ ${#empties[@]} -eq 0 ] || log "added empty (allowed): ${empties[*]}"
[ ${#NEW[@]} -gt 0 ] || log "nothing to add"
