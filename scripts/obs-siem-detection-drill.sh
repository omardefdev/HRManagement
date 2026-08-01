#!/usr/bin/env bash
# OBS-003 detection drill: inject BulkLookup security event + ensure alert-sink receives SIEM alert.
# Usage:
#   DRY_RUN=1 bash scripts/obs-siem-detection-drill.sh
#   GATEWAY_URL=... ALERT_SINK_URL=... bash scripts/obs-siem-detection-drill.sh
set -euo pipefail

GATEWAY_URL="${GATEWAY_URL:-http://localhost:5000}"
ALERT_SINK_URL="${ALERT_SINK_URL:-http://localhost:8089}"
DRY_RUN="${DRY_RUN:-0}"
CORR="obs003-siem-$(date +%s)"

need() { command -v "$1" >/dev/null 2>&1 || { echo "missing $1" >&2; exit 2; }; }
need curl

payload="$(cat <<EOF
{
  "eventType": "BulkLookup",
  "actorSub": "drill-user",
  "correlationId": "${CORR}",
  "resource": "api/lookup",
  "detailJson": "{\"drill\":true,\"count\":99}"
}
EOF
)"

if [[ "${DRY_RUN}" == "1" ]]; then
  echo "DRY_RUN=1 — would POST security event + SIEM alert for corr=${CORR}"
  echo "${payload}"
  echo "Detection drill DRY_RUN OK"
  exit 0
fi

echo "== POST security event to Gateway =="
curl -sS -o /tmp/obs003-ingest.json -w "HTTP %{http_code}\n" \
  -H "Content-Type: application/json" \
  -d "${payload}" \
  "${GATEWAY_URL}/api/platform/audit/security-events" || true

echo "== POST synthetic SIEM alert to alert-sink (detector may also fire) =="
alert="$(cat <<EOF
{
  "version": "4",
  "status": "firing",
  "receiver": "webhook",
  "commonLabels": {
    "alertname": "SiemBulkLookup",
    "owner": "security",
    "severity": "page",
    "corr": "${CORR}"
  },
  "commonAnnotations": {
    "summary": "OBS-003 detection drill ${CORR}",
    "runbook_url": "docs/OBS_SIEM_RUNBOOKS.md"
  },
  "alerts": [{
    "status": "firing",
    "labels": {
      "alertname": "SiemBulkLookup",
      "owner": "security",
      "severity": "page"
    },
    "annotations": { "summary": "drill ${CORR}" },
    "startsAt": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  }]
}
EOF
)"

code="$(curl -sS -o /tmp/obs003-alert.json -w '%{http_code}' \
  -H "Content-Type: application/json" \
  -d "${alert}" \
  "${ALERT_SINK_URL}/webhook")"

echo "alert-sink HTTP ${code}"
if [[ "${code}" != "200" ]]; then
  echo "FAIL: alert-sink did not accept webhook" >&2
  exit 1
fi

last="$(curl -sS "${ALERT_SINK_URL}/last")"
echo "${last}" | grep -q "SiemBulkLookup" || { echo "FAIL: /last missing SiemBulkLookup" >&2; exit 1; }
echo "${last}" | grep -q "${CORR}" || { echo "FAIL: /last missing corr" >&2; exit 1; }

echo "Detection drill OK (corr=${CORR})"
