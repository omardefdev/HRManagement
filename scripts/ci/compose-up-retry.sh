#!/usr/bin/env bash
# PLAN-35 (P-1): bring up a CI compose stack. CI path only; the product startup is unchanged and the two defects below
# stay recorded for their owners (reports/plan35-final-report.md):
#  1. Named volumes start root-owned on a clean Docker host, and the non-root app user (1654) of file-storage-web
#     cannot create its Local-provider folders ("Access to the path '/var/hrm/file-storage/hr-core' is denied", exit
#     139). As in scripts/acceptance-up.ps1, the volume is prepared as root before the stack starts.
#  2. On a cold start several services (auth-directory-web, payroll-web, compliance-web, dev-analytics-web) can exit
#     when PostgreSQL refuses their first connection; a second "up --wait" starts them cleanly. One retry.
#     Cause: the postgresql healthcheck (pg_isready over the Unix socket) already passes while the image runs its
#     init scripts on a temporary socket-only server, which then restarts. Services that survive that window can keep
#     running without their migrations ("relation ... does not exist", HTTP 500; run 41119397846). PostgreSQL is
#     therefore started first and the stack waits until it answers over TCP, which only the final server does.
# After "up --wait" the script also fails when any container of the stack has exited (a crash after --wait returned).
#   bash scripts/ci/compose-up-retry.sh -f docker-compose.yml -f docker-compose.services.yml --profile full-stack
set -uo pipefail

# docker/DotnetService.Dockerfile copies docker/build-manifest/ (gitignored, BLD-002): a clean checkout has none
# (E2E run 41089826696 failed with '"/docker/build-manifest": not found').
pwsh -NoProfile -File scripts/docker-generate-build-manifest.ps1 || { echo "::error::build manifest generation failed"; exit 1; }
docker compose "$@" build || { echo "::error::compose build failed"; exit 1; }
if docker compose "$@" config --services | grep -qx file-storage-web; then
  docker compose "$@" run --rm --no-deps --user 0:0 --entrypoint sh file-storage-web \
    -c "mkdir -p /var/hrm/file-storage && chown -R 1654:1654 /var/hrm/file-storage" \
    || { echo "::error::could not prepare the file-storage volume ownership"; exit 1; }
fi

if docker compose "$@" config --services | grep -qx postgresql; then
  docker compose "$@" up -d postgresql || { echo "::error::could not start postgresql"; exit 1; }
  ready=""
  for i in $(seq 1 90); do
    if docker compose "$@" exec -T postgresql sh -c 'pg_isready -h 127.0.0.1 -U "$POSTGRES_USER" -d postgres' >/dev/null 2>&1; then
      ready=1; echo "PostgreSQL accepts TCP connections after ${i} check(s)"; break
    fi
    sleep 2
  done
  [ -n "$ready" ] || { echo "::error::PostgreSQL did not accept TCP connections"; docker compose "$@" logs postgresql --tail 60 || true; exit 1; }
fi

# Services whose container exited with a non-zero code (a one-shot service that finished with 0 is fine).
exited() { docker compose "$@" ps -a --status exited --format '{{.Service}} {{.ExitCode}}' 2>/dev/null | awk '$2 != 0 { print $1 }'; }

for attempt in 1 2; do
  if docker compose "$@" up -d --wait --wait-timeout 600; then
    # A service without a healthcheck counts as ready once running; give a crash on startup time to show.
    sleep 20
    if [ -z "$(exited "$@")" ]; then
      if [ "$attempt" -gt 1 ]; then
        echo "::warning::compose stack healthy only on attempt ${attempt} (cold-start retry, PLAN-35 P-1)"
      fi
      exit 0
    fi
  fi
  echo "::warning::compose stack not healthy on attempt ${attempt}"
  docker compose "$@" ps -a || true
  for svc in $(exited "$@"); do
    echo "--- last log lines of exited service ${svc}"
    docker compose "$@" logs "${svc}" --tail 60 || true
  done
done
echo "::error::compose stack not healthy after 2 attempts"
exit 1
