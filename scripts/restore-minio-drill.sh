#!/usr/bin/env bash
# OPS-003: restore MinIO backup prefix into an isolated test bucket and compare object counts.
# Usage:
#   MINIO_ENDPOINT=... MINIO_ACCESS_KEY=... MINIO_SECRET_KEY=... \
#   SOURCE_PREFIX=hrm-backups/minio/20260728T120000Z/file-storage \
#   ./scripts/restore-minio-drill.sh
set -euo pipefail

ENDPOINT="${MINIO_ENDPOINT:?MINIO_ENDPOINT required}"
ACCESS="${MINIO_ACCESS_KEY:?MINIO_ACCESS_KEY required}"
SECRET="${MINIO_SECRET_KEY:?MINIO_SECRET_KEY required}"
SOURCE_PREFIX="${SOURCE_PREFIX:?SOURCE_PREFIX required (alias path after bucket)}"
TEST_BUCKET="${TEST_BUCKET:-hrm-restore-drill}"
ALIAS="hrm-restore-drill"

need_mc() {
  command -v mc >/dev/null 2>&1 || { echo "ERROR: mc required" >&2; exit 2; }
}
need_mc

mc alias set "${ALIAS}" "${ENDPOINT}" "${ACCESS}" "${SECRET}" >/dev/null
mc mb --ignore-existing "${ALIAS}/${TEST_BUCKET}" || true
mc rm --recursive --force --dangerous "${ALIAS}/${TEST_BUCKET}/drill" 2>/dev/null || true

echo "Mirroring ${SOURCE_PREFIX} -> ${TEST_BUCKET}/drill ..."
mc mirror --overwrite "${ALIAS}/${SOURCE_PREFIX}" "${ALIAS}/${TEST_BUCKET}/drill"

src_count="$(mc ls --recursive "${ALIAS}/${SOURCE_PREFIX}" 2>/dev/null | wc -l | tr -d ' ')"
dst_count="$(mc ls --recursive "${ALIAS}/${TEST_BUCKET}/drill" 2>/dev/null | wc -l | tr -d ' ')"

echo "source_objects=${src_count}"
echo "restored_objects=${dst_count}"

if [[ "${src_count}" != "${dst_count}" ]]; then
  echo "ERROR: object count mismatch" >&2
  exit 1
fi
echo "MinIO restore drill OK"
