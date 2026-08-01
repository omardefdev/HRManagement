#!/usr/bin/env bash
# NFR-001 k6 runner
# Usage: bash scripts/nfr-k6-run.sh gateway-mixed load
set -euo pipefail

SCENARIO="${1:-smoke}"
PROFILE="${2:-load}"
BASE_URL="${BASE_URL:-http://localhost:5000}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STAMP="$(date +%Y%m%d-%H%M%S)"
RUN_DIR="${ROOT}/perf/results/${STAMP}-${PROFILE}-${SCENARIO}"
mkdir -p "${RUN_DIR}"

SCRIPT="${ROOT}/perf/k6/${SCENARIO}.js"
if [[ "${SCENARIO}" == "smoke" ]]; then
  SCRIPT="${ROOT}/perf/k6/smoke.js"
fi
[[ -f "${SCRIPT}" ]] || { echo "missing ${SCRIPT}" >&2; exit 2; }

cat > "${RUN_DIR}/meta.json" <<EOF
{"startedAtUtc":"$(date -u +%Y-%m-%dT%H:%M:%SZ)","scenario":"${SCENARIO}","profile":"${PROFILE}","baseUrl":"${BASE_URL}","scale_factor":"${NFR_SCALE_FACTOR:-}"}
EOF

export BASE_URL NFR_PROFILE="${PROFILE}" VUS_BASE="${VUS_BASE:-50}"

run_k6() {
  if command -v k6 >/dev/null 2>&1; then
    (cd "$(dirname "${SCRIPT}")" && k6 run --summary-export="${RUN_DIR}/summary.json" "$(basename "${SCRIPT}")")
  else
    docker run --rm \
      -e BASE_URL -e NFR_PROFILE -e ACCESS_TOKEN -e VUS_BASE -e SOAK_DURATION -e SPIKE_VUS \
      -e PAYROLL_RUN_ID -e EXPORT_PATH \
      -v "${ROOT}/perf/k6:/scripts" \
      -v "${RUN_DIR}:/out" \
      grafana/k6:0.54.0 run --summary-export=/out/summary.json "/scripts/$(basename "${SCRIPT}")"
  fi
}

run_k6
echo "Results -> ${RUN_DIR}"
