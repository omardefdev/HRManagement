#!/usr/bin/env bash
# OPS-004: failback to primary + data reconciliation helpers.
# Does not change DNS. After reconcile, run dr-dns-cutover-check against primary URL.
#
# Usage:
#   bash scripts/dr-failback-reconcile.sh
#   SKIP_RECONCILE=1 bash scripts/dr-failback-reconcile.sh   # docs/timing only
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}"
START="$(date -u +%s)"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG_DIR="${DR_DRILL_LOG_DIR:-./backups/dr-drills}"
mkdir -p "${LOG_DIR}"
LOG="${LOG_DIR}/failback-${STAMP}.log"
exec > >(tee -a "${LOG}") 2>&1

echo "=== OPS-004 DR failback + reconcile ${STAMP} ==="
echo "1) Put secondary in read-only / stop writers (manual / scale to 0 jobs)."
echo "2) Confirm primary HA healthy."
echo "3) Run reconciliation scripts."
echo "4) DNS back to primary AFTER dr-dns-cutover-check.sh succeeds."

if command -v kubectl >/dev/null 2>&1; then
  bash "${ROOT}/scripts/verify-k8s-ha.sh" || echo "WARN: primary app HA verify failed"
  bash "${ROOT}/scripts/verify-k8s-data-ha.sh" || echo "WARN: primary data HA verify failed"
fi

if [[ "${SKIP_RECONCILE:-0}" != "1" ]]; then
  if command -v pwsh >/dev/null 2>&1; then
    echo "--- reconcile-tenants ---"
    pwsh -File "${ROOT}/scripts/reconcile-tenants.ps1" || echo "WARN: reconcile-tenants"
    echo "--- reconcile-employee-orphans ---"
    pwsh -File "${ROOT}/scripts/reconcile-employee-orphans.ps1" || echo "WARN: reconcile-employee-orphans"
    echo "--- reconcile-file-orphans ---"
    pwsh -File "${ROOT}/scripts/reconcile-file-orphans.ps1" || echo "WARN: reconcile-file-orphans"
  else
    echo "WARN: pwsh not found — run reconcile-*.ps1 manually on an operator workstation."
  fi
else
  echo "SKIP_RECONCILE=1 — document manual reconcile in results log."
fi

END="$(date -u +%s)"
echo "elapsed_seconds=$((END - START))"
echo "log=${LOG}"
echo "Next: DR_TARGET_URL=https://hr.example.com bash scripts/dr-dns-cutover-check.sh"
echo "Then IC updates DNS and closes the DR incident in OPS_DR_VERIFICATION.md."
exit 0
