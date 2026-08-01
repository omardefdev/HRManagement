#!/usr/bin/env bash
# OPS-004: technical site-failover drill orchestrator (warm standby).
# Does not change DNS. Records timing; promotes secondary only when DR_APPLY=1.
#
# Modes:
#   LAB (default): validate manifests + optional namespace drill hooks; print recovery order.
#   APPLY: DR_APPLY=1 on a secondary kube-context — apply dr-secondary overlays after data bootstrap.
#
# Usage:
#   bash scripts/dr-failover-drill.sh
#   DR_APPLY=1 KUBE_CONTEXT=secondary bash scripts/dr-failover-drill.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}"
START="$(date -u +%s)"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG_DIR="${DR_DRILL_LOG_DIR:-./backups/dr-drills}"
mkdir -p "${LOG_DIR}"
LOG="${LOG_DIR}/failover-${STAMP}.log"
exec > >(tee -a "${LOG}") 2>&1

echo "=== OPS-004 DR failover drill ${STAMP} ==="
echo "Recovery order: backups -> Postgres -> Keycloak -> Rabbit/MinIO -> Apps/Gateway -> DNS(manual)"

fail=0
run_precheck() {
  local name="$1"
  shift
  if [[ -x "$1" ]] || [[ -f "$1" ]]; then
    echo "--- precheck: ${name} ---"
    if bash "$@" ; then
      echo "OK: ${name}"
    else
      echo "WARN: ${name} failed (continuing; record in results)"
      fail=1
    fi
  else
    echo "SKIP: ${name} (script missing)"
  fi
}

echo "--- precheck: verify-backup-catalog (DR drill mode) ---"
if BACKUP_DR_DRILL=1 bash "${ROOT}/scripts/verify-backup-catalog.sh"; then
  echo "OK: verify-backup-catalog"
else
  echo "WARN: verify-backup-catalog failed (continuing; record in results)"
  fail=1
fi
# HA verifies need a live cluster — soft fail
if command -v kubectl >/dev/null 2>&1; then
  run_precheck "verify-k8s-ha" "${ROOT}/scripts/verify-k8s-ha.sh" || true
  run_precheck "verify-k8s-data-ha" "${ROOT}/scripts/verify-k8s-data-ha.sh" || true
else
  echo "SKIP: kubectl not available"
fi

echo "--- validate Kustomize stubs ---"
kubectl kustomize "${ROOT}/deploy/k8s/overlays/dr-secondary" >/dev/null
kubectl kustomize "${ROOT}/deploy/k8s/data/overlays/dr-secondary-data" >/dev/null
echo "OK: dr-secondary overlays build"

if [[ "${DR_APPLY:-0}" == "1" ]]; then
  CTX="${KUBE_CONTEXT:?KUBE_CONTEXT required when DR_APPLY=1}"
  echo "Applying secondary overlays on context ${CTX} ..."
  echo "Ensure data was bootstrapped from hrm-backups first (CNPG recovery / MinIO mirror)."
  kubectl --context "${CTX}" apply -k "${ROOT}/deploy/k8s/data/overlays/dr-secondary-data"
  kubectl --context "${CTX}" apply -k "${ROOT}/deploy/k8s/overlays/dr-secondary"
  echo "Apply complete. Next: DR_TARGET_URL=... bash scripts/dr-dns-cutover-check.sh then IC DNS Go."
else
  echo "LAB mode: set DR_APPLY=1 KUBE_CONTEXT=... to apply on secondary cluster."
  echo "Optional lab PITR: RECOVERY_TARGET_TIME='...' bash scripts/restore-pitr-drill.sh"
fi

END="$(date -u +%s)"
ELAPSED=$((END - START))
echo "elapsed_seconds=${ELAPSED}"
echo "log=${LOG}"
echo "Record measured RTO/RPO in docs/OPS_DR_VERIFICATION.md results table."
if [[ "${fail}" -ne 0 ]]; then
  echo "drill finished with WARNINGS"
  exit 0
fi
echo "drill OK"
exit 0
