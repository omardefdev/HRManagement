#!/usr/bin/env bash
# OBS-003 tamper drill: document SQL tamper + expect chain-verify failure.
# Lab-safe: DRY_RUN=1 validates instructions without mutating DB.
# Usage:
#   DRY_RUN=1 bash scripts/obs-audit-tamper-drill.sh
#   # Live (dangerous): set GATEWAY_AUDIT_DB_URL and CONFIRM_TAMPER=YES
set -euo pipefail

DRY_RUN="${DRY_RUN:-1}"
GATEWAY_URL="${GATEWAY_URL:-http://localhost:5000}"

echo "== OBS-003 tamper drill =="
echo "1) Baseline: bash scripts/obs-audit-chain-verify.sh  (expect ok=true)"
echo "2) Tamper (lab only):"
echo "   UPDATE gateway_audit_entries SET \"Decision\"='tampered' WHERE id = (SELECT MAX(id) FROM gateway_audit_entries);"
echo "3) Re-run chain-verify (expect HTTP 409 / ok=false)"
echo "4) Restore from WORM export / PITR — do not leave tampered lab DB"

if [[ "${DRY_RUN}" == "1" ]]; then
  echo "Tamper drill DRY_RUN OK (no DB mutation)"
  exit 0
fi

if [[ "${CONFIRM_TAMPER:-}" != "YES" ]]; then
  echo "Set CONFIRM_TAMPER=YES and GATEWAY_AUDIT_DB_URL to mutate lab DB" >&2
  exit 2
fi

: "${GATEWAY_AUDIT_DB_URL:?set GATEWAY_AUDIT_DB_URL}"
need() { command -v "$1" >/dev/null 2>&1 || { echo "missing $1" >&2; exit 2; }; }
need psql
need curl

psql "${GATEWAY_AUDIT_DB_URL}" -c \
  "UPDATE gateway_audit_entries SET \"Decision\"='tampered' WHERE id = (SELECT MAX(id) FROM gateway_audit_entries);"

code="$(curl -sS -o /tmp/obs-tamper-verify.json -w '%{http_code}' \
  "${GATEWAY_URL}/api/platform/audit/chain-verify" || true)"
echo "HTTP ${code}"
cat /tmp/obs-tamper-verify.json
echo

if [[ "${code}" == "200" ]]; then
  echo "FAIL: expected chain break after tamper" >&2
  exit 1
fi

echo "Tamper drill OK (chain break detected)"
