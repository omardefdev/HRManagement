#!/usr/bin/env bash
# PLAN-13 AC-37: fetch unified journey rebuild JSON from Gateway.
# Usage:
#   GATEWAY_URL=http://localhost:5200 GATEWAY_TOKEN=... \
#   bash scripts/obs-audit-journey-rebuild.sh --correlation-id <guid>
set -euo pipefail

GATEWAY_URL="${GATEWAY_URL:-http://localhost:5200}"
CORR=""
OUT="journey.json"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --correlation-id) CORR="$2"; shift 2 ;;
    --output) OUT="$2"; shift 2 ;;
    *) echo "Unknown arg: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "${CORR}" ]]; then
  echo "Usage: $0 --correlation-id GUID [--output journey.json]" >&2
  exit 2
fi

hdrs=(-H "Accept: application/json")
if [[ -n "${GATEWAY_TOKEN:-}" ]]; then
  hdrs+=(-H "Authorization: Bearer ${GATEWAY_TOKEN}")
fi

code="$(curl -sS -o "${OUT}" -w '%{http_code}' \
  "${hdrs[@]}" \
  "${GATEWAY_URL}/api/platform/audit/journey/${CORR}")"

echo "HTTP ${code}"
cat "${OUT}"
echo

if [[ "${code}" != "200" ]]; then
  echo "FAIL: journey rebuild HTTP ${code}" >&2
  exit 1
fi

if grep -q '"gaps":\[\]' "${OUT}" || grep -q '"gaps": \[\]' "${OUT}"; then
  echo "Journey rebuild OK (gaps empty)"
else
  echo "WARN: gaps present in journey.json" >&2
fi
