#!/usr/bin/env bash
# OBS-002: verify every alert has owner + runbook_url annotations/labels.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fail=0

check_file() {
  local f="$1"
  local alerts
  alerts="$(grep -E '^\s+- alert:' "${f}" | sed 's/.*alert:[[:space:]]*//')"
  while IFS= read -r name; do
    [[ -z "${name}" ]] && continue
    # crude block extract: from alert name to next alert or EOF
    block="$(awk -v n="${name}" '
      $0 ~ "- alert: " n {p=1}
      p && $0 ~ /- alert:/ && $0 !~ n {exit}
      p {print}
    ' "${f}")"
    if ! echo "${block}" | grep -q 'owner:'; then
      echo "MISSING owner: ${name} in ${f}" >&2
      fail=1
    fi
    if ! echo "${block}" | grep -q 'runbook_url:'; then
      echo "MISSING runbook_url: ${name} in ${f}" >&2
      fail=1
    fi
    echo "OK  ${name}"
  done <<< "${alerts}"
}

check_file "${ROOT}/docker/prometheus/alerts.yml"
check_file "${ROOT}/docker/prometheus/slo-burn-alerts.yml"

if [[ "${fail}" -ne 0 ]]; then
  echo "Alert labels check FAILED" >&2
  exit 1
fi
echo "Alert labels check OK"
