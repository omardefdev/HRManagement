#!/usr/bin/env bash
# NFR-002 restore proxies
# Usage: bash scripts/nfr-chaos-restore.sh [pg|rabbit|gateway|all]
set -euo pipefail
TARGET="${1:-all}"
API="${TOXIPROXY_URL:-http://localhost:8474}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STAMP="$(date +%Y%m%d-%H%M%S)"
RUN="${ROOT}/chaos/results/${STAMP}-restore-${TARGET}"
mkdir -p "${RUN}"

restore_one() {
  local proxy="$1"
  curl -sf "${API}/proxies/${proxy}/toxics" 2>/dev/null | sed -n 's/.*"name":"\([^"]*\)".*/\1/p' | while read -r n; do
    [[ -n "$n" ]] && curl -sf -X DELETE "${API}/proxies/${proxy}/toxics/${n}" >/dev/null || true
  done
  meta="$(curl -sf "${API}/proxies/${proxy}" 2>/dev/null || true)"
  [[ -z "$meta" ]] && return 0
  listen="$(echo "$meta" | sed -n 's/.*"listen":"\([^"]*\)".*/\1/p' | head -1)"
  upstream="$(echo "$meta" | sed -n 's/.*"upstream":"\([^"]*\)".*/\1/p' | head -1)"
  curl -sf -X POST "${API}/proxies/${proxy}" -H 'Content-Type: application/json' \
    -d "{\"name\":\"${proxy}\",\"listen\":\"${listen}\",\"upstream\":\"${upstream}\",\"enabled\":true}" >/dev/null || true
  echo "restored ${proxy}"
}

if [[ "$TARGET" == "all" ]]; then
  restore_one pg; restore_one rabbit; restore_one gateway
else
  restore_one "$TARGET"
fi

echo "{\"target\":\"${TARGET}\",\"status\":\"restored\"}" > "${RUN}/summary.json"
echo "OK restore → ${RUN}"
