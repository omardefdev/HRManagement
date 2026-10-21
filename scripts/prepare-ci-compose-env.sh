#!/usr/bin/env bash
# Prepare .env + Keycloak import (base realm + QA-004 E2E personas) before docker compose up in CI.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ ! -f .env.ci ]]; then
  echo "ERROR: .env.ci not found"
  exit 1
fi

cp .env.ci .env
pwsh -NoProfile -File scripts/prepare-keycloak-import.ps1 -DemoPassword 'HrAdmin123!'
pwsh -NoProfile -File scripts/merge-keycloak-realm-extensions.ps1

REALM="$ROOT/docker/keycloak/import/realm-hrmanagement.json"
if [[ ! -f "$REALM" ]]; then
  echo "ERROR: Keycloak realm missing after prepare"
  exit 1
fi

echo "CI compose environment ready."
