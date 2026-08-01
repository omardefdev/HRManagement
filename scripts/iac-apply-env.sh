#!/usr/bin/env bash
# OPS-005: terraform init + apply for an environment.
# Usage: bash scripts/iac-apply-env.sh staging|production|dr-secondary [--plan-only]
# Production apply is intended from CI with GitHub Environment approval — laptop use requires documented exception.
set -euo pipefail

ENV_NAME="${1:?usage: iac-apply-env.sh <staging|production|dr-secondary> [--plan-only]}"
shift || true
PLAN_ONLY=0
for arg in "$@"; do
  [[ "${arg}" == "--plan-only" ]] && PLAN_ONLY=1
done

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIR="${ROOT}/terraform/envs/${ENV_NAME}"

if [[ ! -d "${DIR}" ]]; then
  echo "ERROR: unknown env directory ${DIR}" >&2
  exit 2
fi

if [[ "${ENV_NAME}" == "production" && "${PLAN_ONLY}" -eq 0 && "${ALLOW_LOCAL_PROD_APPLY:-}" != "1" ]]; then
  echo "ERROR: production apply blocked locally. Use CI Environment approval," >&2
  echo "  or set ALLOW_LOCAL_PROD_APPLY=1 with documented break-glass." >&2
  exit 3
fi

need() {
  command -v "$1" >/dev/null 2>&1 || { echo "ERROR: missing $1" >&2; exit 2; }
}
need terraform

cd "${DIR}"

TFVARS="${TFVARS:-terraform.tfvars}"
BACKEND_CFG="${BACKEND_CFG:-}"

INIT_ARGS=()
if [[ -n "${BACKEND_CFG}" ]]; then
  INIT_ARGS+=(-backend-config="${BACKEND_CFG}")
elif [[ -f backend.hcl ]]; then
  INIT_ARGS+=(-backend-config=backend.hcl)
elif [[ -f ../../backend.hcl ]]; then
  INIT_ARGS+=(-backend-config=../../backend.hcl)
  if [[ -f backend.hcl.example ]]; then
    # merge env key if present as second -backend-config
    INIT_ARGS+=(-backend-config=backend.hcl.example)
  fi
else
  echo "WARN: no backend.hcl — init with -backend=false (local only / CI validate)" >&2
  INIT_ARGS+=(-backend=false)
fi

terraform init "${INIT_ARGS[@]}"
terraform fmt -check -recursive ../../modules . || terraform fmt -recursive ../../modules .
terraform validate

PLAN_ARGS=()
[[ -f "${TFVARS}" ]] && PLAN_ARGS+=(-var-file="${TFVARS}")
[[ -f terraform.tfvars.example && ! -f "${TFVARS}" ]] && PLAN_ARGS+=(-var-file=terraform.tfvars.example)

terraform plan "${PLAN_ARGS[@]}" -out=tfplan

if [[ "${PLAN_ONLY}" -eq 1 ]]; then
  echo "Plan-only complete (tfplan written)."
  exit 0
fi

terraform apply -auto-approve tfplan
echo "Apply complete for ${ENV_NAME}. Next: scripts/iac-kustomize-apply.sh ${ENV_NAME}"
