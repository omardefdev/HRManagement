#!/usr/bin/env bash
# NFR-002 worker crash drill: stop payroll container, note PAY-003 resume expectation.
# Usage: PAYROLL_CONTAINER=hrm-payroll bash scripts/nfr-chaos-worker-crash.sh
set -euo pipefail
CONTAINER="${PAYROLL_CONTAINER:-hrm-payroll}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STAMP="$(date +%Y%m%d-%H%M%S)"
RUN="${ROOT}/chaos/results/${STAMP}-worker-crash"
mkdir -p "${RUN}"

if ! command -v docker >/dev/null 2>&1; then
  echo '{"status":"skipped","reason":"docker missing"}' > "${RUN}/summary.json"
  echo "docker missing — documented skip"
  exit 0
fi

if docker ps --format '{{.Names}}' | grep -qx "${CONTAINER}"; then
  docker stop "${CONTAINER}"
  echo "{\"container\":\"${CONTAINER}\",\"action\":\"stop\",\"status\":\"stopped\",\"expect\":\"PAY-003 resume/checkpoint; no duplicate salary lines\"}" > "${RUN}/summary.json"
  echo "Stopped ${CONTAINER}. Restart with: docker start ${CONTAINER}"
else
  echo "{\"container\":\"${CONTAINER}\",\"status\":\"not-running\",\"expect\":\"manual kill of payroll process + resume job\"}" > "${RUN}/summary.json"
  echo "Container ${CONTAINER} not running — record manual crash in notes.md"
fi

cat > "${RUN}/notes.md" <<EOF
# Worker crash drill
1. Stop worker mid-run
2. Confirm partitions lease expire / claimable
3. Resume job via API Pause/Resume or restart container
4. Assert no duplicate SalaryLines (unique index)
EOF
