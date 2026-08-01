#!/usr/bin/env bash
# OBS-001: compare documented scrape jobs vs prometheus.yml
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROM="${ROOT}/docker/prometheus/prometheus.yml"
INV="${ROOT}/docs/OBS_SERVICE_INVENTORY.md"

need() { command -v "$1" >/dev/null 2>&1 || { echo "missing $1" >&2; exit 2; }; }
need grep

expected=(
  hr-gateway hr-core hr-contracts hr-health hr-movements hr-training payroll
  idcard records-archive admin-logistics it-infra dev-analytics compliance
  auth-directory file-storage rabbitmq postgres
)

fail=0
echo "== prometheus.yml job coverage =="
for job in "${expected[@]}"; do
  if grep -qE "job_name:[[:space:]]*${job}\\b" "${PROM}"; then
    echo "OK  ${job}"
  else
    echo "MISSING ${job}" >&2
    fail=1
  fi
done

echo ""
echo "== inventory doc mentions scrape list =="
if grep -q 'hr-gateway' "${INV}" && grep -q 'file-storage' "${INV}"; then
  echo "OK  inventory lists scrape jobs"
else
  echo "MISSING inventory scrape section" >&2
  fail=1
fi

if [[ "${fail}" -ne 0 ]]; then
  echo "Inventory compare FAILED" >&2
  exit 1
fi
echo "Inventory compare OK"
