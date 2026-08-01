#!/usr/bin/env bash
# OPS-005: apply existing OPS-001..004 Kustomize overlays after cluster is ready.
# Usage: OWNER=org IMAGE_TAG=sha bash scripts/iac-kustomize-apply.sh staging|production|dr-secondary [--dry-run]
set -euo pipefail

ENV_NAME="${1:?usage: iac-kustomize-apply.sh <staging|production|dr-secondary> [--dry-run]}"
shift || true
DRY_RUN=0
for arg in "$@"; do
  [[ "${arg}" == "--dry-run" ]] && DRY_RUN=1
done

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OWNER="${OWNER:?Set OWNER= ghcr.io org/user}"
IMAGE_TAG="${IMAGE_TAG:?Set IMAGE_TAG= immutable SHA (DEP-001)}"

need() {
  command -v "$1" >/dev/null 2>&1 || { echo "ERROR: missing $1" >&2; exit 2; }
}
need kubectl

case "${ENV_NAME}" in
  staging)
    APP_OVERLAY="${ROOT}/deploy/k8s/overlays/staging-ha"
    DATA_OVERLAY="${ROOT}/deploy/k8s/data/overlays/staging-data-ha"
    ;;
  production)
    APP_OVERLAY="${ROOT}/deploy/k8s/overlays/production-ha"
    DATA_OVERLAY="${ROOT}/deploy/k8s/data/overlays/production-data-ha"
    ;;
  dr-secondary)
    APP_OVERLAY="${ROOT}/deploy/k8s/overlays/dr-secondary"
    DATA_OVERLAY="${ROOT}/deploy/k8s/data/overlays/dr-secondary-data"
    ;;
  *)
    echo "ERROR: unknown env ${ENV_NAME}" >&2
    exit 2
    ;;
esac

render() {
  local overlay="$1"
  kubectl kustomize "${overlay}" \
    | sed "s|ghcr.io/OWNER/hrmanagement/|ghcr.io/${OWNER}/hrmanagement/|g; s|:IMAGE_TAG|:${IMAGE_TAG}|g"
}

apply_or_dry() {
  local overlay="$1"
  echo "== ${overlay} =="
  if [[ "${DRY_RUN}" -eq 1 ]]; then
    render "${overlay}" >/dev/null
    echo "dry-run OK (kustomize build succeeded)"
  else
    render "${overlay}" | kubectl apply -f -
  fi
}

# Platform External Secrets Operator manifests (ClusterSecretStore) — optional if already installed
PLATFORM="${ROOT}/deploy/k8s/platform/external-secrets"
if [[ -d "${PLATFORM}" ]]; then
  echo "== platform external-secrets =="
  if [[ "${DRY_RUN}" -eq 1 ]]; then
    kubectl kustomize "${PLATFORM}" >/dev/null
    echo "dry-run OK (platform ESO)"
  else
    kubectl apply -k "${PLATFORM}" || echo "WARN: platform ESO apply failed (install operator chart first)"
  fi
fi

apply_or_dry "${DATA_OVERLAY}"
apply_or_dry "${APP_OVERLAY}"

echo "Done. Verify with scripts/verify-k8s-ha.sh / verify-k8s-data-ha.sh / verify-backup-catalog.sh"
