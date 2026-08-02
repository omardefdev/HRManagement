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

REALM="$ROOT/docker/keycloak/import/realm-hrmanagement.json"
E2E="$ROOT/docker/keycloak/realm-qa004-e2e.json"
if [[ ! -f "$REALM" || ! -f "$E2E" ]]; then
  echo "ERROR: Keycloak realm files missing after prepare"
  exit 1
fi

python3 <<'PY'
import json
from pathlib import Path

root = Path("docker/keycloak")
main_path = root / "import" / "realm-hrmanagement.json"
e2e_path = root / "realm-qa004-e2e.json"
main = json.loads(main_path.read_text(encoding="utf-8"))
e2e = json.loads(e2e_path.read_text(encoding="utf-8"))
seen = {u.get("username") for u in main.get("users") or []}
for user in e2e.get("users") or []:
    name = user.get("username")
    if name and name not in seen:
        main.setdefault("users", []).append(user)
        seen.add(name)
main_path.write_text(json.dumps(main, indent=2), encoding="utf-8")
print(f"Merged E2E personas into {main_path} ({len(main.get('users') or [])} users)")
PY

echo "CI compose environment ready."
