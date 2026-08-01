#!/usr/bin/env bash
# IAM-004: create non-root MinIO application user for File.Storage only.
# Requires mc (MinIO client) or runs via docker against the minio container network.
#
#   MINIO_ROOT_USER=... MINIO_ROOT_PASSWORD=... \
#   MINIO_FILESTORAGE_ACCESS_KEY=... MINIO_FILESTORAGE_SECRET_KEY=... \
#   ./scripts/init-minio-filestorage-user.sh
set -euo pipefail

ENDPOINT="${MINIO_ENDPOINT:-http://localhost:9000}"
ROOT_USER="${MINIO_ROOT_USER:?MINIO_ROOT_USER required}"
ROOT_PASS="${MINIO_ROOT_PASSWORD:?MINIO_ROOT_PASSWORD required}"
APP_KEY="${MINIO_FILESTORAGE_ACCESS_KEY:?MINIO_FILESTORAGE_ACCESS_KEY required}"
APP_SECRET="${MINIO_FILESTORAGE_SECRET_KEY:?MINIO_FILESTORAGE_SECRET_KEY required}"

MC="${MC_BIN:-mc}"

"$MC" alias set hrminio "$ENDPOINT" "$ROOT_USER" "$ROOT_PASS" --api S3v4 >/dev/null

# Buckets used by File.Storage / module uploads
for bucket in hr-core hr-contracts idcard records-archive hr-training file-storage file-storage-quarantine; do
  "$MC" mb --ignore-existing "hrminio/${bucket}" || true
done

POLICY_FILE="$(mktemp)"
cat > "$POLICY_FILE" <<'EOF'
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["s3:GetObject", "s3:PutObject", "s3:DeleteObject", "s3:ListBucket"],
      "Resource": [
        "arn:aws:s3:::hr-core",
        "arn:aws:s3:::hr-core/*",
        "arn:aws:s3:::hr-contracts",
        "arn:aws:s3:::hr-contracts/*",
        "arn:aws:s3:::idcard",
        "arn:aws:s3:::idcard/*",
        "arn:aws:s3:::records-archive",
        "arn:aws:s3:::records-archive/*",
        "arn:aws:s3:::hr-training",
        "arn:aws:s3:::hr-training/*",
        "arn:aws:s3:::file-storage",
        "arn:aws:s3:::file-storage/*",
        "arn:aws:s3:::file-storage-quarantine",
        "arn:aws:s3:::file-storage-quarantine/*"
      ]
    }
  ]
}
EOF

"$MC" admin policy create hrminio filestorage-app "$POLICY_FILE" 2>/dev/null \
  || "$MC" admin policy set hrminio filestorage-app "$POLICY_FILE" 2>/dev/null \
  || true

"$MC" admin user add hrminio "$APP_KEY" "$APP_SECRET" 2>/dev/null || true
"$MC" admin policy attach hrminio filestorage-app --user "$APP_KEY" 2>/dev/null \
  || "$MC" admin policy set hrminio filestorage-app user="$APP_KEY" 2>/dev/null \
  || true

rm -f "$POLICY_FILE"
echo "IAM-004: MinIO File.Storage app user ready ($APP_KEY)."
echo "Inject FileStorage__AccessKey/SecretKey only on file-storage service — never via shared env."
