#!/usr/bin/env bash
# OBS-002: create an Alertmanager silence for maintenance windows.
# Requires amtool in PATH and Alertmanager reachable.
# Usage:
#   ALERTMANAGER_URL=http://localhost:9093 DURATION=2h \
#   MATCHERS='alertname=~".*"' bash scripts/obs-silence-maintenance.sh
set -euo pipefail

ALERTMANAGER_URL="${ALERTMANAGER_URL:-http://localhost:9093}"
DURATION="${DURATION:-2h}"
COMMENT="${COMMENT:-OBS-002 scheduled maintenance}"
CREATED_BY="${CREATED_BY:-${USER:-ops}}"
MATCHERS="${MATCHERS:-severity=~\"J1|J2|J3|J4\"}"

need() { command -v "$1" >/dev/null 2>&1 || { echo "missing $1 — install amtool (alertmanager)" >&2; exit 2; }; }

if command -v amtool >/dev/null 2>&1; then
  echo "Creating silence via amtool (${DURATION}) matchers=${MATCHERS}"
  # shellcheck disable=SC2086
  amtool silence add ${MATCHERS} \
    --author="${CREATED_BY}" \
    --comment="${COMMENT}" \
    --duration="${DURATION}" \
    --alertmanager.url="${ALERTMANAGER_URL}"
  echo "Silence created. List: amtool silence query --alertmanager.url=${ALERTMANAGER_URL}"
  exit 0
fi

echo "amtool not found — documenting manual API fallback"
echo "POST ${ALERTMANAGER_URL}/api/v2/silences with matchers for journey alerts"
echo "Example duration: ${DURATION} comment: ${COMMENT}"
cat <<EOF
{
  "matchers": [{"name":"journey","value":"J1|J2|J3|J4","isRegex":true}],
  "startsAt": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "endsAt": "$(date -u -d "+${DURATION}" +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date -u -v+2H +%Y-%m-%dT%H:%M:%SZ)",
  "createdBy": "${CREATED_BY}",
  "comment": "${COMMENT}"
}
EOF
