#!/usr/bin/env bash
# OBS-003: verify gateway audit hash chain via Gateway API (or dry-run shape check).
# Usage:
#   GATEWAY_URL=http://localhost:5000 bash scripts/obs-audit-chain-verify.sh
#   DRY_RUN=1 bash scripts/obs-audit-chain-verify.sh
set -euo pipefail

GATEWAY_URL="${GATEWAY_URL:-http://localhost:5000}"
DRY_RUN="${DRY_RUN:-0}"
TOKEN="${GATEWAY_TOKEN:-}"

if [[ "${DRY_RUN}" == "1" ]]; then
  echo "DRY_RUN=1 — chain verify contract: GET ${GATEWAY_URL}/api/platform/audit/chain-verify"
  echo "Expect JSON { ok: true|false, checkedCount: N, error?: string }"
  echo "Chain verify DRY_RUN OK"
  exit 0
fi

need() { command -v "$1" >/dev/null 2>&1 || { echo "missing $1" >&2; exit 2; }; }
need curl

hdrs=(-H "Accept: application/json")
if [[ -n "${TOKEN}" ]]; then
  hdrs+=(-H "Authorization: Bearer ${TOKEN}")
fi

echo "== GET ${GATEWAY_URL}/api/platform/audit/chain-verify =="
code="$(curl -sS -o /tmp/obs-chain-verify.json -w '%{http_code}' \
  "${hdrs[@]}" \
  "${GATEWAY_URL}/api/platform/audit/chain-verify" || true)"

echo "HTTP ${code}"
cat /tmp/obs-chain-verify.json
echo

if [[ "${code}" != "200" ]]; then
  echo "FAIL: chain verify did not return 200" >&2
  exit 1
fi

if ! grep -q '"ok":true\|"ok": true' /tmp/obs-chain-verify.json; then
  echo "FAIL: ok != true" >&2
  exit 1
fi

echo "Chain verify OK"
