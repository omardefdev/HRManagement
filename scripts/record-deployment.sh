#!/usr/bin/env bash
# Append a deployment audit record (DEP-001).
# Required env: IMAGE_TAG, GITHUB_SHA (or empty), DEPLOY_ENV, GITHUB_ACTOR
# Optional: DEPLOY_PATH (default /opt/hrm), DEPLOY_LOG

set -euo pipefail

DEPLOY_PATH="${DEPLOY_PATH:-/opt/hrm}"
DEPLOY_LOG="${DEPLOY_LOG:-${DEPLOY_PATH}/deployments.log}"
IMAGE_TAG="${IMAGE_TAG:?IMAGE_TAG is required}"
GITHUB_SHA="${GITHUB_SHA:-unknown}"
DEPLOY_ENV="${DEPLOY_ENV:-unknown}"
GITHUB_ACTOR="${GITHUB_ACTOR:-unknown}"

mkdir -p "$(dirname "$DEPLOY_LOG")"
line="$(date -u +%Y-%m-%dT%H:%M:%SZ)|tag=${IMAGE_TAG}|sha=${GITHUB_SHA}|env=${DEPLOY_ENV}|actor=${GITHUB_ACTOR}"
echo "$line" >> "$DEPLOY_LOG"
echo "Recorded deployment: $line"
