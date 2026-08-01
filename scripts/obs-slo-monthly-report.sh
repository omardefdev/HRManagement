#!/usr/bin/env bash
# OBS-002: generate a monthly SLO markdown report from Prometheus (or a dry-run template).
# Usage:
#   PROMETHEUS_URL=http://localhost:9090 OUT=docs/ops-reports/slo-YYYY-MM.md \
#     bash scripts/obs-slo-monthly-report.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROMETHEUS_URL="${PROMETHEUS_URL:-}"
MONTH="${MONTH:-$(date +%Y-%m)}"
OUT="${OUT:-${ROOT}/docs/ops-reports/slo-${MONTH}.md}"
mkdir -p "$(dirname "${OUT}")"

query() {
  local q="$1"
  curl -fsS --get "${PROMETHEUS_URL}/api/v1/query" --data-urlencode "query=${q}" \
    | python -c "import sys,json; d=json.load(sys.stdin); r=d.get('data',{}).get('result',[]);
print(r[0]['value'][1] if r else 'n/a')" 2>/dev/null || echo "n/a"
}

{
  echo "# SLO monthly report — ${MONTH}"
  echo
  echo "Generated: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo
  echo "| Journey | Metric | Value |"
  echo "|---|---|---|"
  if [[ -n "${PROMETHEUS_URL}" ]] && curl -fsS "${PROMETHEUS_URL}/-/ready" >/dev/null 2>&1; then
    echo "| J1 | sli:j1_availability:ratio30d | $(query 'sli:j1_availability:ratio30d') |"
    echo "| J1 | slo:j1_error_budget_remaining:ratio | $(query 'slo:j1_error_budget_remaining:ratio') |"
    echo "| J2 | sli:j2_availability:ratio_rate5m | $(query 'sli:j2_availability:ratio_rate5m') |"
    echo "| J2 | job:http_duration_p95:5m{job=\"hr-gateway\"} | $(query 'job:http_duration_p95:5m{job=\"hr-gateway\"}') |"
    echo "| J3 | max(outbox_pending_messages) | $(query 'max(outbox_pending_messages)') |"
    echo "| J4 | sli:j4_payroll_fail_ratio:1h | $(query 'sli:j4_payroll_fail_ratio:1h') |"
  else
    echo "| J1 | sli:j1_availability:ratio30d | _dry-run: set PROMETHEUS_URL_ |"
    echo "| J1 | error budget remaining | _dry-run_ |"
    echo "| J2 | availability / p95 | _dry-run_ |"
    echo "| J3 | outbox pending | _dry-run_ |"
    echo "| J4 | payroll fail ratio | _dry-run_ |"
  fi
  echo
  echo "## Notes"
  echo
  echo "- Catalog: docs/OBS_SLO_CATALOG.md"
  echo "- Targets: J1/J2 99.5% availability / 30d; J2 P95 < 2s; J3 freshness; J4 fail ratio < 1%"
  echo "- Burn alerts: docker/prometheus/slo-burn-alerts.yml"
} > "${OUT}"

echo "Wrote ${OUT}"
