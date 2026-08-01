#!/usr/bin/env bash
# OPS-003: append a line to the backup catalog (JSONL).
# Usage: backup_catalog_append <component> <path> <checksum> <bytes> [status]
# Env: BACKUP_CATALOG_PATH (default: ./backups/backup-catalog.jsonl)
#      BACKUP_RETENTION_DAYS (default: 30) — used for expiresAt

backup_catalog_append() {
  local component="${1:?component}"
  local path="${2:?path}"
  local checksum="${3:-}"
  local bytes="${4:-0}"
  local status="${5:-ok}"
  local catalog="${BACKUP_CATALOG_PATH:-./backups/backup-catalog.jsonl}"
  local retention="${BACKUP_RETENTION_DAYS:-30}"
  local taken
  taken="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  local expires
  if date -u -d "+${retention} days" +%Y-%m-%dT%H:%M:%SZ >/dev/null 2>&1; then
    expires="$(date -u -d "+${retention} days" +%Y-%m-%dT%H:%M:%SZ)"
  else
    # BSD/macOS
    expires="$(date -u -v+"${retention}"d +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || echo "")"
  fi
  mkdir -p "$(dirname "${catalog}")"
  printf '{"takenAt":"%s","component":"%s","path":"%s","checksumSha256":"%s","bytes":%s,"expiresAt":"%s","status":"%s"}\n' \
    "${taken}" "${component}" "${path}" "${checksum}" "${bytes}" "${expires}" "${status}" >> "${catalog}"
}
