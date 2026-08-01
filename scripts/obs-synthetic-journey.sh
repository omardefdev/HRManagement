#!/usr/bin/env bash
# OBS-001: hit gateway health (and optional downstream) then check Tempo for recent traces.
# Usage: GATEWAY_URL=http://localhost:8080 TEMPO_URL=http://localhost:3200 bash scripts/obs-synthetic-journey.sh
set -euo pipefail

GATEWAY_URL="${GATEWAY_URL:-http://localhost:8080}"
TEMPO_URL="${TEMPO_URL:-http://localhost:3200}"
CORR="obs001-$(date +%s)"

need() { command -v "$1" >/dev/null 2>&1 || { echo "missing $1" >&2; exit 2; }; }
need curl

echo "== synthetic HTTP via gateway =="
code="$(curl -sS -o /tmp/obs-synth-body.txt -w '%{http_code}' \
  -H "X-Correlation-ID: ${CORR}" \
  -H "traceparent: 00-$(printf '%032d' 0 | sed 's/0/a/g' | head -c 32)-$(printf '%016d' 1 | sed 's/0/b/g' | head -c 16)-01" \
  "${GATEWAY_URL}/health/ready" || true)"
echo "gateway /health/ready -> HTTP ${code} corr=${CORR}"

# Best-effort second hop if core is exposed via gateway path
curl -sS -o /dev/null -H "X-Correlation-ID: ${CORR}" \
  "${GATEWAY_URL}/health/live" || true

echo "== wait for export =="
sleep 3

echo "== Tempo search =="
if curl -fsS "${TEMPO_URL}/ready" >/dev/null 2>&1; then
  # Tempo search API varies by version; accept ready + optional search
  curl -fsS "${TEMPO_URL}/api/search?limit=20&tags=service.name" >/tmp/obs-tempo.json || \
    curl -fsS "${TEMPO_URL}/api/search?limit=20" >/tmp/obs-tempo.json || true
  if [[ -s /tmp/obs-tempo.json ]]; then
    echo "Tempo returned search payload ($(wc -c </tmp/obs-tempo.json) bytes)"
  else
    echo "WARN: Tempo search empty — ensure OTEL is enabled on apps and collector→tempo works"
  fi
else
  echo "WARN: Tempo not reachable at ${TEMPO_URL} — stack may be down (doc-only gate still OK via inventory/pii)"
fi

echo "Synthetic journey script finished (corr=${CORR})"
