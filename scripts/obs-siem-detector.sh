#!/usr/bin/env bash
# OBS-003: optional standalone SIEM detector (posts to alert-sink when JSONL thresholds met).
# Prefer Gateway SiemDetectorService; this script is for lab without Gateway loop.
# Usage:
#   SECURITY_AUDIT_JSONL=data/security-audit ALERT_SINK_URL=http://localhost:8089 \
#     bash scripts/obs-siem-detector.sh
set -euo pipefail

RULES_DIR="${HR_SIEM_RULES_PATH:-policy/siem}"
JSONL_DIR="${SECURITY_AUDIT_JSONL:-data/security-audit}"
ALERT_SINK_URL="${ALERT_SINK_URL:-http://localhost:8089}"
DRY_RUN="${DRY_RUN:-0}"

echo "Rules: ${RULES_DIR}"
echo "JSONL: ${JSONL_DIR}"

if [[ ! -d "${RULES_DIR}" ]]; then
  echo "No rules dir; exit 0"
  exit 0
fi

fired=0
for rule in "${RULES_DIR}"/*.yaml "${RULES_DIR}"/*.yml; do
  [[ -f "${rule}" ]] || continue
  eventType="$(grep -E '^eventType:' "${rule}" | head -1 | sed 's/.*:[[:space:]]*//' | tr -d '"')"
  threshold="$(grep -E '^threshold:' "${rule}" | head -1 | sed 's/.*:[[:space:]]*//' || echo 1)"
  alertname="$(grep -E '^alertname:' "${rule}" | head -1 | sed 's/.*:[[:space:]]*//' | tr -d '"' || echo SiemRule)"
  [[ -n "${eventType}" ]] || continue
  count=0
  if [[ -d "${JSONL_DIR}" ]]; then
    count="$(grep -h "\"EventType\":\"${eventType}\"\|\"eventType\":\"${eventType}\"" "${JSONL_DIR}"/*.jsonl 2>/dev/null | wc -l | tr -d ' ')"
  fi
  echo "rule $(basename "${rule}"): eventType=${eventType} count=${count} threshold=${threshold}"
  if [[ "${count}" -ge "${threshold}" ]]; then
    fired=$((fired + 1))
    if [[ "${DRY_RUN}" == "1" ]]; then
      echo "DRY_RUN would alert ${alertname}"
      continue
    fi
    curl -sS -o /dev/null -w "sink %{http_code}\n" \
      -H "Content-Type: application/json" \
      -d "{\"version\":\"4\",\"status\":\"firing\",\"commonLabels\":{\"alertname\":\"${alertname}\",\"owner\":\"security\",\"severity\":\"page\"},\"alerts\":[{\"status\":\"firing\",\"labels\":{\"alertname\":\"${alertname}\",\"owner\":\"security\"}}]}" \
      "${ALERT_SINK_URL}/webhook" || true
  fi
done

echo "Detector done (fired=${fired})"
