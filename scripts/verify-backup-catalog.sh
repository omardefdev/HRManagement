#!/usr/bin/env bash
# OPS-003: fail if backup catalog is missing, stale, or reports non-ok status.
# OPS-004 DR drill: invoked by scripts/dr-failover-drill.sh as a precheck gate.
#
# Usage:
#   BACKUP_CATALOG_PATH=./backups/backup-catalog.jsonl \
#   BACKUP_MAX_AGE_HOURS=36 \
#   bash scripts/verify-backup-catalog.sh
#
# DR drill (secondary site / failover rehearsal):
#   BACKUP_DR_DRILL=1 \
#   BACKUP_REQUIRE_COMPONENTS=postgres,minio,keycloak \
#   BACKUP_MAX_AGE_HOURS=48 \
#   BACKUP_CATALOG_PATH=/catalog/backup-catalog.jsonl \
#   bash scripts/verify-backup-catalog.sh
#
# See docs/OPS_BACKUP_VERIFICATION.md and docs/OPS_DR_TOPOLOGY.md.
set -euo pipefail

CATALOG="${BACKUP_CATALOG_PATH:-./backups/backup-catalog.jsonl}"
MAX_AGE_HOURS="${BACKUP_MAX_AGE_HOURS:-36}"
if [[ "${BACKUP_DR_DRILL:-0}" == "1" ]]; then
  REQUIRE_COMPONENTS="${BACKUP_REQUIRE_COMPONENTS:-postgres,minio,keycloak}"
  echo "DR drill mode: require components=${REQUIRE_COMPONENTS}"
else
  REQUIRE_COMPONENTS="${BACKUP_REQUIRE_COMPONENTS:-postgres,minio}"
fi
fail=0

if [[ ! -f "${CATALOG}" ]]; then
  echo "ERROR: catalog not found: ${CATALOG}" >&2
  exit 1
fi

now_epoch="$(date -u +%s)"
max_age_sec=$((MAX_AGE_HOURS * 3600))

# Track latest ok takenAt per component prefix
declare -A LATEST_EPOCH
declare -A LATEST_STATUS

while IFS= read -r line || [[ -n "${line}" ]]; do
  [[ -z "${line}" ]] && continue
  component="$(printf '%s' "${line}" | sed -n 's/.*"component":"\([^"]*\)".*/\1/p')"
  taken="$(printf '%s' "${line}" | sed -n 's/.*"takenAt":"\([^"]*\)".*/\1/p')"
  status="$(printf '%s' "${line}" | sed -n 's/.*"status":"\([^"]*\)".*/\1/p')"
  checksum="$(printf '%s' "${line}" | sed -n 's/.*"checksumSha256":"\([^"]*\)".*/\1/p')"
  path="$(printf '%s' "${line}" | sed -n 's/.*"path":"\([^"]*\)".*/\1/p')"
  [[ -z "${component}" || -z "${taken}" ]] && continue

  # prefix before first dot (postgres.hr_core_db -> postgres)
  prefix="${component%%.*}"
  taken_epoch="$(date -u -d "${taken}" +%s 2>/dev/null || date -u -j -f "%Y-%m-%dT%H:%M:%SZ" "${taken}" +%s 2>/dev/null || echo 0)"
  prev="${LATEST_EPOCH[$prefix]:-0}"
  if [[ "${taken_epoch}" -ge "${prev}" ]]; then
    LATEST_EPOCH[$prefix]="${taken_epoch}"
    LATEST_STATUS[$prefix]="${status:-unknown}"
  fi

  # Optional: verify checksum file still matches when path is local dump
  if [[ -n "${checksum}" && -f "${path}" ]] && command -v sha256sum >/dev/null 2>&1; then
    actual="$(sha256sum "${path}" | awk '{print $1}')"
    if [[ "${actual}" != "${checksum}" ]]; then
      echo "ERROR: corrupt backup detected path=${path}" >&2
      fail=1
    fi
  fi
done < "${CATALOG}"

IFS=',' read -ra reqs <<< "${REQUIRE_COMPONENTS}"
for req in "${reqs[@]}"; do
  req="$(echo "${req}" | tr -d ' ')"
  [[ -z "${req}" ]] && continue
  if [[ -z "${LATEST_EPOCH[$req]+x}" ]]; then
    echo "ERROR: no catalog entries for component prefix=${req}" >&2
    fail=1
    continue
  fi
  age=$((now_epoch - LATEST_EPOCH[$req]))
  if [[ "${age}" -gt "${max_age_sec}" ]]; then
    echo "ERROR: stale backup prefix=${req} age_hours=$((age / 3600)) max=${MAX_AGE_HOURS}" >&2
    fail=1
  else
    echo "OK: ${req} age_hours=$((age / 3600)) status=${LATEST_STATUS[$req]}"
  fi
  if [[ "${LATEST_STATUS[$req]}" != "ok" ]]; then
    echo "ERROR: last status for ${req} is ${LATEST_STATUS[$req]}" >&2
    fail=1
  fi
done

if [[ "${BACKUP_DR_DRILL:-0}" == "1" ]]; then
  postgres_entries="$(grep -c '"component":"postgres\.' "${CATALOG}" 2>/dev/null || echo 0)"
  if [[ "${postgres_entries}" -lt 16 ]]; then
    echo "ERROR: DR drill expects >=16 postgres.* catalog lines (found ${postgres_entries})" >&2
    fail=1
  else
    echo "OK: postgres database catalog lines=${postgres_entries}"
  fi
fi

if [[ "${fail}" -ne 0 ]]; then
  echo "verify-backup-catalog: FAILED"
  exit 1
fi
echo "verify-backup-catalog: OK"
exit 0
