#!/usr/bin/env bash
# SUP-001-E: deny GPL/AGPL (and variants) unless listed in supply-chain-exceptions as license waiver
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
LIC="$ROOT/docs/THIRD_PARTY_LICENSES.md"
POLICY="$ROOT/docs/LICENSE_POLICY.md"

if [[ ! -f "$LIC" ]]; then
  echo "Missing $LIC"
  exit 1
fi

# Scan inventory for forbidden license tokens (case-insensitive)
if grep -Eiq '(^|[^a-z])(agpl|gpl-?3|gplv3|gnu general public license)([^a-z]|$)' "$LIC"; then
  # Allow if documented exception exists with packageOrCve starting with license:
  if ! grep -q '"packageOrCve": "license:' "$ROOT/supply-chain-exceptions.json" 2>/dev/null; then
    echo "FAIL: GPL/AGPL-class license mentioned in THIRD_PARTY_LICENSES.md without license: exception (SUP-001-E)."
    echo "See $POLICY"
    exit 1
  fi
fi

echo "License policy check OK (no unwaived GPL/AGPL in inventory)."
