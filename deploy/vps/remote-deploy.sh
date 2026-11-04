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
  len="$(env_value HR_CORE_OTP_HMAC_KEY | wc -c)"
  [ "$len" -ge 32 ] || die "HR_CORE_OTP_HMAC_KEY must be at least 32 characters"
  case "$(env_value IMAGE_TAG)" in latest|*:latest) die "IMAGE_TAG=latest is refused";; esac
  # PLAN-38 (D-38-1): the database posture. demo = the applications use the PostgreSQL administrator (vps-demo file);
  # least-privilege = the *_app roles of the production file. Demo mode with least-privilege needs wave 2 (OPS-D:
  # enforced row level security under the demo seeds), so it is refused here; the posture is proven on the
  # hrm-leastpriv acceptance stack (docker-compose.acceptance-leastpriv.yml).
  local db_posture
  db_posture="$(env_value HRM_DB_POSTURE)"
  case "${db_posture:-demo}" in demo|least-privilege) ;; *) die "HRM_DB_POSTURE must be demo or least-privilege";; esac
  if [ "$(env_value HRM_DEPLOYMENT_MODE)" = "demo" ] && [ "$db_posture" = "least-privilege" ]; then
    die "HRM_DB_POSTURE=least-privilege is not available in demo mode before wave 2 (OPS-D)"
  fi
  if [ "$(env_value HRM_DEPLOYMENT_MODE)" != "demo" ] && [ "$db_posture" = "demo" ]; then
    die "HRM_DB_POSTURE=demo (administrator connections) exists only with HRM_DEPLOYMENT_MODE=demo"
  fi
  case "$(env_value HRM_MQ_POSTURE)" in ""|least-privilege|admin) ;; *) die "HRM_MQ_POSTURE must be least-privilege or admin";; esac
  # PLAN-38 (section 5, review F6): the pool knobs of the compose files. lint-compose.ps1 checks the defaults; a value in
  # .env changes the total, so the same rule runs here: 14 pools of HRM_PG_POOL_MAX (the gateway and 13 modules), HR.Core
  # HRM_PG_POOL_MAX_HR_CORE, Keycloak 20 and a reserve of 5 stay below 90 % of max_connections.
  local pool hc_pool max_conn total
  pool="$(env_value HRM_PG_POOL_MAX)"; pool="${pool:-10}"
  hc_pool="$(env_value HRM_PG_POOL_MAX_HR_CORE)"; hc_pool="${hc_pool:-20}"
  case "$pool:$hc_pool" in *[!0-9:]*|:*|*:) die "HRM_PG_POOL_MAX and HRM_PG_POOL_MAX_HR_CORE must be whole numbers";; esac
  max_conn="$(grep -ho 'max_connections=[0-9]*' "$(dirname "$example")/../../docker-compose.vps.yml" 2>/dev/null | head -1 | cut -d= -f2)"
  max_conn="${max_conn:-250}"
  total=$(( 14 * pool + hc_pool + 20 + 5 ))
  [ $(( total * 10 )) -lt $(( max_conn * 9 )) ] \
    || die "connection pools: $total of max_connections $max_conn (90 % or more); lower HRM_PG_POOL_MAX or HRM_PG_POOL_MAX_HR_CORE"
  log "env check: all $(grep -cE '^[A-Z][A-Z0-9_]*=' "$example") keys of .env.vps.example present (values not shown)"
}

compose_files() {
  local files="-f docker-compose.yml -f docker-compose.services.yml -f docker-compose.images.yml -f docker-compose.production.yml -f docker-compose.vps.yml"
  if [ "$(env_value HRM_DEPLOYMENT_MODE)" = "demo" ]; then files="$files -f docker-compose.vps-demo.yml"; fi
  # PLAN-38 (D-38-2): the broker administrator for the applications only on request (least-privilege is the default).
  if [ "$(env_value HRM_MQ_POSTURE)" = "admin" ]; then files="$files -f docker-compose.posture-mq-admin.yml"; fi
  local extra
  for extra in ${HRM_EXTRA_COMPOSE_FILES:-}; do files="$files -f $extra"; done
  printf '%s' "$files"
}

dc() {
  # shellcheck disable=SC2046
  docker compose -p "$HRM_PROJECT" --env-file "$ENV_FILE" --profile full-stack $(compose_files) "$@"
}

# PLAN-38: hrm-bootstrap (databases, roles, migrations as <svc>_migrator, broker users) ran before any application
# started; its log carries step lines and the result JSON, never a secret. Prints them and its duration.
bootstrap_report() {
  local id state
  id="$(dc ps -a -q hrm-bootstrap 2>/dev/null | head -1)"
  [ -n "$id" ] || { log "hrm-bootstrap: no container (release without the init container)"; return 0; }
  state="$(docker inspect -f '{{.State.Status}} exit={{.State.ExitCode}} started={{.State.StartedAt}} finished={{.State.FinishedAt}}' "$id")"
  log "hrm-bootstrap: $state"
  docker logs "$id" 2>&1 | tail -n 60 | sed 's/^/[hrm-bootstrap] /'
  local s f
  s="$(date -d "$(docker inspect -f '{{.State.StartedAt}}' "$id")" +%s 2>/dev/null || echo 0)"
  f="$(date -d "$(docker inspect -f '{{.State.FinishedAt}}' "$id")" +%s 2>/dev/null || echo 0)"
  [ "$s" -gt 0 ] && [ "$f" -ge "$s" ] && echo "timing bootstrap_seconds=$((f - s))"
  return 0
}

# PLAN-39 (section 2): the rendered realm and its reconcile plan (placeholders and names only, from the bundle's
# deploy/vps/keycloak-import) go into the named volume keycloak_import, which keycloak (--import-realm, first boot
# only) and keycloak-reconcile read. The volume is refilled from the release being deployed on EVERY deploy, a rollback
# included, so a release never reconciles with another release's plan (review F1); the release keeps its own copy
# (placeholders and names only). The plan names its realm profile, which must match HRM_DEPLOYMENT_MODE (review F3:
# a Demo realm, with its demo users, is refused outside demo mode and the Production one inside it).
fill_keycloak_import() {
  local release="$1" src="$1/deploy/vps/keycloak-import" volume="${HRM_PROJECT}_keycloak_import"
  if [ ! -f "$release/deploy/keycloak/reconcile.sh" ]; then
    log "keycloak import: release from before PLAN-39 (its compose bind-mounts $src), volume not used"
    return 0
  fi
  [ -f "$src/realm-hrmanagement.json" ] && [ -f "$src/reconcile-plan.tsv" ] \
    || die "keycloak import: $src lacks realm-hrmanagement.json or reconcile-plan.tsv (extract the release again: deploy.ps1 -Tag <tag> -PrepareOnly)"
  local profile wanted
  profile="$(grep -E $'^profile\t' "$src/reconcile-plan.tsv" | cut -f2 | head -1)"
  if [ "$(env_value HRM_DEPLOYMENT_MODE)" = "demo" ]; then wanted=Demo; else wanted=Production; fi
  if [ -z "$profile" ]; then
    # 817304f5bd88, the first PLAN-39 release, rendered its plan before the profile line existed.
    log "keycloak import: the plan names no profile (rendered before review F3); not compared with HRM_DEPLOYMENT_MODE"
  elif [ "$profile" != "$wanted" ]; then
    die "keycloak import: the release's realm was rendered for profile '$profile', HRM_DEPLOYMENT_MODE needs $wanted (render it with -Profile $wanted)"
  fi
  docker volume create "$volume" > /dev/null
  # tar on stdin: works the same from Git Bash on Windows (-Local), where host paths do not bind-mount reliably.
  tar -C "$src" -cf - realm-hrmanagement.json reconcile-plan.tsv \
    | docker run --rm -i --network none -v "$volume:/import" alpine:3.20.3 \
        sh -c 'rm -f /import/*; tar -C /import -xf -; chmod 644 /import/*; ls /import | tr "\n" " "' \
    | sed 's/^/[remote-deploy] keycloak import volume: /'; echo
  log "keycloak import: profile $profile from release $(basename "$release")"
}

# PLAN-39: keycloak-reconcile ran after keycloak was healthy and before any application; its log carries client and
# scope names with the action and "reconcile: N changes", never a secret. Prints it and its duration.
reconcile_report() {
  local id s f
  id="$(dc ps -a -q keycloak-reconcile 2>/dev/null | head -1)"
  [ -n "$id" ] || { log "keycloak-reconcile: no container"; return 0; }
  log "keycloak-reconcile: $(docker inspect -f '{{.State.Status}} exit={{.State.ExitCode}}' "$id")"
  docker logs "$id" 2>&1 | tail -n 80
  s="$(date -d "$(docker inspect -f '{{.State.StartedAt}}' "$id")" +%s 2>/dev/null || echo 0)"
  f="$(date -d "$(docker inspect -f '{{.State.FinishedAt}}' "$id")" +%s 2>/dev/null || echo 0)"
  [ "$s" -gt 0 ] && [ "$f" -ge "$s" ] && echo "timing keycloak_reconcile_seconds=$((f - s))"
  return 0
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
        # One-shot containers: volume-init, (PLAN-38) hrm-bootstrap and (PLAN-39) keycloak-reconcile must have exited 0.
        exited:none) [ "$4" = "0" ] && { [ "$5" = "volume-init" ] || [ "$5" = "hrm-bootstrap" ] || [ "$5" = "keycloak-reconcile" ]; } || bad="$bad $5($2/$3/exit $4)";;
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

  fill_keycloak_import "$release"

  t0="$(date +%s)"
  # PLAN-38: `up` waits for hrm-bootstrap (service_completed_successfully); if it fails, no application is started and
  # `up` itself fails. PLAN-39: the same for keycloak-reconcile, so no application starts with a client secret Keycloak
  # does not know (plan section 2, rule 3). current-tag still names the last healthy release.
  if ! dc up -d --remove-orphans; then
    bootstrap_report
    reconcile_report
    dc ps -a || true
    printf '%s\n' "$tag" > "$HRM_HOME/failed-tag"
    echo "$(date -u +%Y-%m-%dT%H:%M:%SZ)|tag=$tag|action=deploy|result=up-failed" >> "$HRM_HOME/deployments.log"
    die "release $tag: compose up failed (hrm-bootstrap or a container did not start; rollback to ${previous:-nothing}: bash remote-deploy.sh rollback)"
  fi
  t1="$(date +%s)"; echo "timing up_seconds=$((t1 - t0))"
  bootstrap_report
  reconcile_report

  if ! wait_healthy; then
    dc ps -a || true
    # current-tag still names the last healthy release; rollback returns there (not to previous-tag).
    printf '%s\n' "$tag" > "$HRM_HOME/failed-tag"
    echo "$(date -u +%Y-%m-%dT%H:%M:%SZ)|tag=$tag|action=deploy|result=unhealthy" >> "$HRM_HOME/deployments.log"
    die "release $tag did not become healthy (rollback to ${previous:-nothing}: bash remote-deploy.sh rollback)"
  fi

  # PLAN-39: the realm is reconciled by the keycloak-reconcile one-shot inside `up` (above). Only a release from before
  # PLAN-39 (a rollback target) still carries PLAN-37's host-side sync script, and only then it runs here.
  if [ ! -f "$release/deploy/keycloak/reconcile.sh" ] && [ -f "$release/deploy/vps/keycloak-sync-clients.sh" ]; then
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
