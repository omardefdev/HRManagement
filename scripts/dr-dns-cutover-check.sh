#!/usr/bin/env bash
# OPS-004: verify DR target health before DNS cutover. Does NOT change DNS.
# Usage:
#   DR_TARGET_URL=https://hr-dr.example.com bash scripts/dr-dns-cutover-check.sh
# Optional: DR_HEALTH_PATH=/health/ready  DR_TIMEOUT_SEC=15
set -euo pipefail

TARGET="${DR_TARGET_URL:?DR_TARGET_URL required (e.g. https://hr-dr.example.com)}"
PATH_SUFFIX="${DR_HEALTH_PATH:-/health/ready}"
TIMEOUT="${DR_TIMEOUT_SEC:-15}"
URL="${TARGET%/}${PATH_SUFFIX}"

start="$(date -u +%s)"
echo "Checking ${URL} (timeout ${TIMEOUT}s) ..."

code="000"
if command -v curl >/dev/null 2>&1; then
  code="$(curl -k -sS -o /tmp/dr-dns-check.body -w '%{http_code}' --max-time "${TIMEOUT}" "${URL}" || true)"
elif command -v wget >/dev/null 2>&1; then
  if wget --no-check-certificate -q -O /tmp/dr-dns-check.body --timeout="${TIMEOUT}" "${URL}"; then
    code="200"
  else
    code="000"
  fi
else
  echo "ERROR: need curl or wget" >&2
  exit 2
fi

end="$(date -u +%s)"
elapsed=$((end - start))

echo "http_status=${code}"
echo "elapsed_seconds=${elapsed}"
echo "checked_at_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"

if [[ "${code}" != "200" ]]; then
  echo "FAIL: target not ready — do not cut over DNS (OPS-004)." >&2
  exit 1
fi

echo "OK: target healthy. Record Go/No-Go in docs/OPS_DR_COMMUNICATION_TREE.md before changing DNS."
exit 0
