#!/usr/bin/env bash
# OPS-005: detect Terraform drift (and optional kubectl diff).
# Exit 0 = no changes, 1 = error, 2 = drift detected (terraform detailed-exitcode).
# Usage: bash scripts/iac-drift-check.sh staging|production|dr-secondary
set -euo pipefail

ENV_NAME="${1:?usage: iac-drift-check.sh <staging|production|dr-secondary>}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIR="${ROOT}/terraform/envs/${ENV_NAME}"

need() {
  command -v "$1" >/dev/null 2>&1 || { echo "ERROR: missing $1" >&2; exit 1; }
}
need terraform

cd "${DIR}"

if [[ -f backend.hcl ]]; then
  terraform init -input=false -backend-config=backend.hcl
elif [[ -f ../../backend.hcl ]]; then
  EXTRA=()
  [[ -f backend.hcl.example ]] && EXTRA+=(-backend-config=backend.hcl.example)
  terraform init -input=false -backend-config=../../backend.hcl "${EXTRA[@]}"
else
  echo "ERROR: remote backend required for drift check" >&2
  exit 1
fi

PLAN_ARGS=(-input=false -detailed-exitcode -lock=false)
[[ -f terraform.tfvars ]] && PLAN_ARGS+=(-var-file=terraform.tfvars)

set +e
terraform plan "${PLAN_ARGS[@]}"
code=$?
set -e

case "${code}" in
  0)
    echo "No Terraform drift for ${ENV_NAME}."
    ;;
  2)
    echo "DRIFT detected for ${ENV_NAME} (terraform plan exit 2)." >&2
    exit 2
    ;;
  *)
    echo "terraform plan failed (exit ${code})" >&2
    exit 1
    ;;
esac

if [[ "${KUBECTL_DIFF:-0}" == "1" ]] && command -v kubectl >/dev/null 2>&1; then
  echo "== optional kubectl diff =="
  OWNER="${OWNER:-OWNER}"
  IMAGE_TAG="${IMAGE_TAG:-IMAGE_TAG}"
  case "${ENV_NAME}" in
    staging) OVL="${ROOT}/deploy/k8s/overlays/staging-ha" ;;
    production) OVL="${ROOT}/deploy/k8s/overlays/production-ha" ;;
    dr-secondary) OVL="${ROOT}/deploy/k8s/overlays/dr-secondary" ;;
  esac
  kubectl kustomize "${OVL}" \
    | sed "s|ghcr.io/OWNER/hrmanagement/|ghcr.io/${OWNER}/hrmanagement/|g; s|:IMAGE_TAG|:${IMAGE_TAG}|g" \
    | kubectl diff -f - || true
fi

exit 0
