#!/usr/bin/env bash
# PLAN-36 (section 6): deploys one release of the single-host chain. The only deploy path: scripts/vps/deploy.ps1 runs
# it over ssh, its -Local mode runs it on the owner's machine, and .github/workflows/deploy.yml calls it too.
#
#   bash remote-deploy.sh deploy <tag>     run from the release directory $HRM_HOME/releases/<tag> (bundle extracted)
#   bash remote-deploy.sh rollback         redeploy the previous tag recorded in $HRM_HOME/previous-tag
#   bash remote-deploy.sh check-env        only the .env pre-check (key names and lengths, never values)
#
# Environment:
#   HRM_HOME                 default /opt/hrm (releases/, shared/.env, current-tag, current -> releases/<tag>, previous-tag, deployments.log)
#   HRM_PROJECT              compose project name, default hrm (constant, so the volumes survive releases)
#   HRM_EXTRA_COMPOSE_FILES  extra compose files, space separated (the local variant adds docker-compose.vps-local.yml)
#   HRM_HEALTH_TIMEOUT       seconds to wait for every container to be healthy, default 600
#   HRM_SKIP_PULL=1          use the images already present (local registry run, or a rollback without the registry)
#
# Secrets: the .env is passed to docker compose with --env-file and never printed; the pre-check reads key names and
# value lengths only. Timings are printed as "timing <name>=<seconds>" for the evidence.
set -euo pipefail
export MSYS_NO_PATHCONV=1   # Git Bash on Windows (deploy.ps1 -Local): keep container paths as they are.

HRM_HOME="${HRM_HOME:-/opt/hrm}"
HRM_PROJECT="${HRM_PROJECT:-hrm}"
HEALTH_TIMEOUT="${HRM_HEALTH_TIMEOUT:-600}"
ENV_FILE="$HRM_HOME/shared/.env"
ALLOW_EMPTY="ACME_EMAIL ADMIN_ALLOWED_CIDRS IMAGE_TAG"

log() { printf '[remote-deploy] %s\n' "$*"; }
die() { printf '[remote-deploy] ERROR: %s\n' "$*" >&2; exit 1; }

# Value of KEY in the env file without printing it (last assignment wins, quotes stripped).
env_value() {
  awk -v k="$1" -F= '$1==k { v=substr($0, length(k)+2) } END { gsub(/^"|"$/, "", v); printf "%s", v }' "$ENV_FILE"
}

check_env() {
  local example="$1"
  [ -f "$ENV_FILE" ] || die "$ENV_FILE not found (generate it on the server: docs/OPS_VPS_SINGLE_HOST.md)"
  local mode
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) ;;  # Windows file modes are not POSIX modes (local variant only)
    *)
      mode="$(stat -c '%a' "$ENV_FILE")"
      case "$mode" in 600|400) ;; *) die "$ENV_FILE has mode $mode; expected 600 (chmod 600 $ENV_FILE)";; esac
      ;;
  esac
  [ -f "$example" ] || die "$example not found in the release"

  local missing="" empty="" key
  for key in $(grep -E '^[A-Z][A-Z0-9_]*=' "$example" | cut -d= -f1); do
    if ! grep -qE "^${key}=" "$ENV_FILE"; then missing="$missing $key"; continue; fi
    case " $ALLOW_EMPTY " in *" $key "*) continue;; esac
    [ -n "$(env_value "$key")" ] || empty="$empty $key"
  done
  [ -z "$missing" ] || die ".env is missing key(s):$missing"
  [ -z "$empty" ] || die ".env has empty value(s) for:$empty"

  local len
  len="$(env_value DEMO_USERS_PASSWORD | wc -c)"
  [ "$len" -ge 16 ] || die "DEMO_USERS_PASSWORD must be at least 16 characters"
  for key in GATEWAY_CALLER_PROOF_SECRET HR_CORE_OTP_HMAC_KEY; do
    len="$(env_value "$key" | wc -c)"
    [ "$len" -ge 32 ] || die "$key must be at least 32 characters"
  done
  case "$(env_value IMAGE_TAG)" in latest|*:latest) die "IMAGE_TAG=latest is refused";; esac
  log "env check: all $(grep -cE '^[A-Z][A-Z0-9_]*=' "$example") keys of .env.vps.example present (values not shown)"
}

compose_files() {
  local files="-f docker-compose.yml -f docker-compose.services.yml -f docker-compose.images.yml -f docker-compose.production.yml -f docker-compose.vps.yml"
  if [ "$(env_value HRM_DEPLOYMENT_MODE)" = "demo" ]; then files="$files -f docker-compose.vps-demo.yml"; fi
  local extra
  for extra in ${HRM_EXTRA_COMPOSE_FILES:-}; do files="$files -f $extra"; done
  printf '%s' "$files"
}

dc() {
  # shellcheck disable=SC2046
  docker compose -p "$HRM_PROJECT" --env-file "$ENV_FILE" --profile full-stack $(compose_files) "$@"
}

# Waits until every container of the project is running and healthy (or has no healthcheck), and one-shot containers
# exited 0. Prints the unhealthy ones on timeout.
wait_healthy() {
  local start now ids line bad
  start="$(date +%s)"
  while :; do
    bad=""
    ids="$(dc ps -a -q)"
    [ -n "$ids" ] || bad="no containers"
    for id in $ids; do
      line="$(docker inspect -f '{{.Name}} {{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}} {{.State.ExitCode}} {{index .Config.Labels "com.docker.compose.service"}}' "$id")"
      set -- $line
      case "$2:$3" in
        running:healthy|running:none) ;;
        exited:none) [ "$4" = "0" ] && [ "$5" = "volume-init" ] || bad="$bad $5($2/$3/exit $4)";;
        *) bad="$bad $5($2/$3)";;
      esac
    done
    now="$(date +%s)"
    if [ -z "$bad" ]; then
      log "all containers healthy after $((now - start)) s"
      echo "timing healthy_seconds=$((now - start))"
      return 0
    fi
    if [ $((now - start)) -ge "$HEALTH_TIMEOUT" ]; then
      log "not healthy after ${HEALTH_TIMEOUT} s:$bad"
      return 1
    fi
    sleep 10
  done
}

deploy() {
  local tag="$1" release
  [[ "$tag" =~ ^[0-9a-f]{12}$ ]] || die "tag '$tag' is not a 12-character commit SHA"
  release="$HRM_HOME/releases/$tag"
  [ -d "$release" ] || die "release directory $release not found (deploy.ps1 copies and extracts the bundle there)"
  cd "$release"
  check_env "$release/deploy/vps/.env.vps.example"

  export IMAGE_TAG="$tag"
  log "release $tag, project $HRM_PROJECT, files: $(compose_files)"
  dc config -q

  local t0 t1
  if [ "${HRM_SKIP_PULL:-0}" != "1" ]; then
    t0="$(date +%s)"
    dc pull --quiet
    t1="$(date +%s)"; echo "timing pull_seconds=$((t1 - t0))"
  fi

  local previous=""
  if [ -f "$HRM_HOME/current-tag" ]; then previous="$(tr -d '[:space:]' < "$HRM_HOME/current-tag")"; fi

  t0="$(date +%s)"
  dc up -d --remove-orphans
  t1="$(date +%s)"; echo "timing up_seconds=$((t1 - t0))"

  if ! wait_healthy; then
    dc ps -a || true
    # current-tag still names the last healthy release; rollback returns there (not to previous-tag).
    printf '%s\n' "$tag" > "$HRM_HOME/failed-tag"
    echo "$(date -u +%Y-%m-%dT%H:%M:%SZ)|tag=$tag|action=deploy|result=unhealthy" >> "$HRM_HOME/deployments.log"
    die "release $tag did not become healthy (rollback to ${previous:-nothing}: bash remote-deploy.sh rollback)"
  fi

  # PLAN-37: keep the realm's service clients, scopes and secrets in step with deploy/wiring/service-wiring.json
  # (idempotent; never drops or re-imports the realm). Older releases have no such script.
  if [ -f "$release/deploy/vps/keycloak-sync-clients.sh" ]; then
    t0="$(date +%s)"
    if ! HRM_PROJECT="$HRM_PROJECT" bash "$release/deploy/vps/keycloak-sync-clients.sh"; then
      printf '%s\n' "$tag" > "$HRM_HOME/failed-tag"
      echo "$(date -u +%Y-%m-%dT%H:%M:%SZ)|tag=$tag|action=deploy|result=keycloak-sync-failed" >> "$HRM_HOME/deployments.log"
      die "Keycloak client sync failed for $tag (rollback to ${previous:-nothing}: bash remote-deploy.sh rollback)"
    fi
    t1="$(date +%s)"; echo "timing keycloak_sync_seconds=$((t1 - t0))"
  fi

  rm -f "$HRM_HOME/failed-tag"
  if [ -n "$previous" ] && [ "$previous" != "$tag" ]; then
    printf '%s\n' "$previous" > "$HRM_HOME/previous-tag"
  fi
  printf '%s\n' "$tag" > "$HRM_HOME/current-tag"
  # The current symlink is a convenience on Linux; Git Bash would copy the directory instead (local variant).
  case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) ;; *) ln -sfn "$release" "$HRM_HOME/current";; esac
  echo "$(date -u +%Y-%m-%dT%H:%M:%SZ)|tag=$tag|action=deploy|result=healthy|previous=${previous:-none}" >> "$HRM_HOME/deployments.log"
  log "deployed $tag (previous: ${previous:-none})"
}

rollback() {
  local prev current=""
  if [ -f "$HRM_HOME/current-tag" ]; then current="$(tr -d '[:space:]' < "$HRM_HOME/current-tag")"; fi
  if [ -f "$HRM_HOME/failed-tag" ] && [ -n "$current" ]; then
    # The last deploy failed and left its containers running: go back to the last healthy release.
    prev="$current"
    current="$(tr -d '[:space:]' < "$HRM_HOME/failed-tag")"
  else
    [ -f "$HRM_HOME/previous-tag" ] || die "no previous tag recorded in $HRM_HOME/previous-tag"
    prev="$(tr -d '[:space:]' < "$HRM_HOME/previous-tag")"
  fi
  [ -d "$HRM_HOME/releases/$prev" ] || die "release $prev is not on the server"
  log "rolling back from ${current:-none} to $prev"
  deploy "$prev"
  echo "$(date -u +%Y-%m-%dT%H:%M:%SZ)|tag=$prev|action=rollback|from=${current:-none}" >> "$HRM_HOME/deployments.log"
}

case "${1:-}" in
  deploy) [ $# -eq 2 ] || die "usage: remote-deploy.sh deploy <tag>"; deploy "$2";;
  rollback) rollback;;
  check-env) check_env "${2:-$(pwd)/deploy/vps/.env.vps.example}";;
  *) die "usage: remote-deploy.sh deploy <tag> | rollback | check-env [example]";;
esac
