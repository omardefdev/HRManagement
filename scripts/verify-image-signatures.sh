#!/usr/bin/env bash
# SUP-001-D: cosign verify for all release service images at IMAGE_TAG (or digests file)
set -euo pipefail
IMAGE_PREFIX="${IMAGE_PREFIX:?}"
IMAGE_TAG="${IMAGE_TAG:?}"
REPO="${GITHUB_REPOSITORY:?}"
ISSUER="${COSIGN_OIDC_ISSUER:-https://token.actions.githubusercontent.com}"
IDENTITY_REGEXP="${COSIGN_IDENTITY_REGEXP:-https://github.com/${REPO}/.github/workflows/deploy.yml@.*}"

IMAGES=(
  hr-gateway hr-core-web hr-contracts-web hr-health-web hr-movements-web
  hr-training-web payroll-web idcard-web records-archive-web admin-logistics-web
  it-infra-web dev-analytics-web compliance-web auth-directory-web file-storage-web
)

if ! command -v cosign >/dev/null 2>&1; then
  echo "cosign not installed"
  exit 1
fi

for img in "${IMAGES[@]}"; do
  ref="${IMAGE_PREFIX}/${img}:${IMAGE_TAG}"
  echo "Verifying signature: $ref"
  cosign verify \
    --certificate-identity-regexp "$IDENTITY_REGEXP" \
    --certificate-oidc-issuer "$ISSUER" \
    "$ref"
done
echo "All release image signatures verified (SUP-001-D)."
