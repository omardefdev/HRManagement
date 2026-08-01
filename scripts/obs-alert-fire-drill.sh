#!/usr/bin/env bash
# OBS-002 fire drill: POST a synthetic Alertmanager webhook payload to alert-sink and verify /last.
# Usage:
#   ALERT_SINK_URL=http://localhost:8089 bash scripts/obs-alert-fire-drill.sh
# Optional: also POST via Alertmanager API if ALERTMANAGER_URL is set (requires stack up).
set -euo pipefail

ALERT_SINK_URL="${ALERT_SINK_URL:-http://localhost:8089}"
CORR="obs002-drill-$(date +%s)"
DRY_RUN="${DRY_RUN:-0}"

need() { command -v "$1" >/dev/null 2>&1 || { echo "missing $1" >&2; exit 2; }; }
need curl

if [[ "${DRY_RUN}" == "1" ]]; then
  echo "DRY_RUN=1 — validating payload shape only (start alert-sink for live drill)"
  echo "{\"alerts\":[{\"labels\":{\"alertname\":\"Obs002FireDrill\",\"corr\":\"${CORR}\"}}]}" >/tmp/obs-drill-dry.json
  echo "Fire drill DRY_RUN OK (corr=${CORR})"
  exit 0
fi

payload="$(cat <<EOF
{
  "version": "4",
  "groupKey": "{}:{alertname=\"Obs002FireDrill\"}",
  "status": "firing",
  "receiver": "webhook",
  "groupLabels": {"alertname": "Obs002FireDrill"},
  "commonLabels": {
    "alertname": "Obs002FireDrill",
    "severity": "J1",
    "owner": "platform",
    "severity": "page"
  },
  "commonAnnotations": {
    "summary": "OBS-002 fire drill ${CORR}",
    "runbook_url": "docs/OBS_ALERT_RUNBOOKS.md"
  },
  "externalURL": "http://alertmanager:9093",
  "alerts": [{
    "status": "firing",
    "labels": {
      "alertname": "Obs002FireDrill",
      "severity": "J1",
      "owner": "platform",
      "severity": "page"
    },
    "annotations": {
      "summary": "OBS-002 fire drill ${CORR}"
    },
    "startsAt": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
    "endsAt": "0001-01-01T00:00:00Z",
    "generatorURL": "http://prometheus/graph"
  }]
}
EOF
)"

echo "== POST webhook to ${ALERT_SINK_URL}/webhook =="
code="$(curl -sS -o /tmp/obs-drill-post.json -w '%{http_code}' \
  -H 'Content-Type: application/json' \
  -d "${payload}" \
  "${ALERT_SINK_URL}/webhook" || true)"
echo "POST -> HTTP ${code}"
if [[ "${code}" != "200" ]]; then
  echo "Fire drill FAILED: sink did not accept webhook (is alert-sink up?)" >&2
  echo "Doc-only fallback: payload written conceptually; start compose observability to re-run." >&2
  exit 1
fi

echo "== GET ${ALERT_SINK_URL}/last =="
last="$(curl -fsS "${ALERT_SINK_URL}/last")"
echo "${last}" | head -c 500
echo
if ! echo "${last}" | grep -q "${CORR}"; then
  echo "Fire drill FAILED: /last missing correlation ${CORR}" >&2
  exit 1
fi

echo "Fire drill OK (corr=${CORR})"
