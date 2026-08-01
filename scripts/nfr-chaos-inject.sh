#!/usr/bin/env bash
# NFR-002 inject wrapper
# Usage: bash scripts/nfr-chaos-inject.sh pg latency
#        bash scripts/nfr-chaos-inject.sh rabbit down
set -euo pipefail
TARGET="${1:?target pg|rabbit|gateway}"
FAULT="${2:?fault latency|down|loss|timeout|reset}"
API="${TOXIPROXY_URL:-http://localhost:8474}"
LATENCY_MS="${LATENCY_MS:-2000}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STAMP="$(date +%Y%m%d-%H%M%S)"
RUN="${ROOT}/chaos/results/${STAMP}-inject-${TARGET}-${FAULT}"
mkdir -p "${RUN}"

listen_for() {
  case "$1" in
    pg) echo "0.0.0.0:15432" ;;
    rabbit) echo "0.0.0.0:15672" ;;
    gateway) echo "0.0.0.0:15000" ;;
  esac
}
upstream_for() {
  case "$1" in
    pg) echo "postgresql:5432" ;;
    rabbit) echo "rabbitmq:5672" ;;
    gateway) echo "gateway:8080" ;;
  esac
}

LISTEN="$(listen_for "$TARGET")"
UPSTREAM="$(upstream_for "$TARGET")"

curl -sf -X POST "${API}/proxies" -H 'Content-Type: application/json' \
  -d "{\"name\":\"${TARGET}\",\"listen\":\"${LISTEN}\",\"upstream\":\"${UPSTREAM}\",\"enabled\":true}" >/dev/null 2>&1 || true

# clear toxics
if toxics="$(curl -sf "${API}/proxies/${TARGET}/toxics" 2>/dev/null)"; then
  echo "$toxics" | sed -n 's/.*"name":"\([^"]*\)".*/\1/p' | while read -r n; do
    [[ -n "$n" ]] && curl -sf -X DELETE "${API}/proxies/${TARGET}/toxics/${n}" >/dev/null || true
  done
fi

case "$FAULT" in
  reset|down)
    en=true; [[ "$FAULT" == "down" ]] && en=false
    curl -sf -X POST "${API}/proxies/${TARGET}" -H 'Content-Type: application/json' \
      -d "{\"name\":\"${TARGET}\",\"listen\":\"${LISTEN}\",\"upstream\":\"${UPSTREAM}\",\"enabled\":${en}}" >/dev/null
    ;;
  latency)
    curl -sf -X POST "${API}/proxies/${TARGET}" -H 'Content-Type: application/json' \
      -d "{\"name\":\"${TARGET}\",\"listen\":\"${LISTEN}\",\"upstream\":\"${UPSTREAM}\",\"enabled\":true}" >/dev/null
    curl -sf -X POST "${API}/proxies/${TARGET}/toxics" -H 'Content-Type: application/json' \
      -d "{\"name\":\"nfr_latency\",\"type\":\"latency\",\"stream\":\"downstream\",\"toxicity\":1.0,\"attributes\":{\"latency\":${LATENCY_MS},\"jitter\":100}}" >/dev/null
    ;;
  timeout)
    curl -sf -X POST "${API}/proxies/${TARGET}/toxics" -H 'Content-Type: application/json' \
      -d '{"name":"nfr_timeout","type":"timeout","stream":"downstream","toxicity":1.0,"attributes":{"timeout":5000}}' >/dev/null
    ;;
  loss)
    curl -sf -X POST "${API}/proxies/${TARGET}/toxics" -H 'Content-Type: application/json' \
      -d '{"name":"nfr_loss","type":"slicer","stream":"downstream","toxicity":0.15,"attributes":{"average_size":64,"size_variation":32,"delay":10}}' >/dev/null
    ;;
  *) echo "unknown fault" >&2; exit 2 ;;
esac

echo "{\"target\":\"${TARGET}\",\"fault\":\"${FAULT}\",\"status\":\"injected\"}" > "${RUN}/summary.json"
echo "OK inject ${FAULT} on ${TARGET} → ${RUN}"
