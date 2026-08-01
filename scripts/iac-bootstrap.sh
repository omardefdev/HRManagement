#!/usr/bin/env bash
# OPS-005: one-time bootstrap of Terraform remote state (S3 + DynamoDB lock).
# Usage: REGION=eu-central-1 BUCKET=hrm-tfstate-ACCOUNT LOCK_TABLE=hrm-tfstate-lock bash scripts/iac-bootstrap.sh
set -euo pipefail

REGION="${REGION:-eu-central-1}"
BUCKET="${BUCKET:?Set BUCKET= name for tfstate}"
LOCK_TABLE="${LOCK_TABLE:?Set LOCK_TABLE= DynamoDB table name}"

need() {
  command -v "$1" >/dev/null 2>&1 || { echo "ERROR: missing $1" >&2; exit 2; }
}
need aws

echo "== create S3 bucket (idempotent) =="
if aws s3api head-bucket --bucket "${BUCKET}" 2>/dev/null; then
  echo "bucket exists: ${BUCKET}"
else
  if [[ "${REGION}" == "us-east-1" ]]; then
    aws s3api create-bucket --bucket "${BUCKET}"
  else
    aws s3api create-bucket --bucket "${BUCKET}" \
      --create-bucket-configuration "LocationConstraint=${REGION}"
  fi
fi

aws s3api put-bucket-versioning --bucket "${BUCKET}" \
  --versioning-configuration Status=Enabled
aws s3api put-bucket-encryption --bucket "${BUCKET}" \
  --server-side-encryption-configuration \
  '{"Rules":[{"ApplyServerSideEncryptionByDefault":{"SSEAlgorithm":"AES256"}}]}'
aws s3api put-public-access-block --bucket "${BUCKET}" \
  --public-access-block-configuration \
  BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true

echo "== create DynamoDB lock table (idempotent) =="
if aws dynamodb describe-table --table-name "${LOCK_TABLE}" --region "${REGION}" >/dev/null 2>&1; then
  echo "lock table exists: ${LOCK_TABLE}"
else
  aws dynamodb create-table \
    --region "${REGION}" \
    --table-name "${LOCK_TABLE}" \
    --attribute-definitions AttributeName=LockID,AttributeType=S \
    --key-schema AttributeName=LockID,KeyType=HASH \
    --billing-mode PAY_PER_REQUEST
fi

echo "Bootstrap complete. Copy terraform/backend.hcl.example -> backend.hcl and fill bucket/table."
