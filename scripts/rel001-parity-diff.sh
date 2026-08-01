#!/usr/bin/env bash
# REL-001: thin wrapper — prefers pwsh parity-diff.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
if command -v pwsh >/dev/null 2>&1; then
  pwsh "$ROOT/scripts/rel001-parity-diff.ps1" "$@"
else
  echo "pwsh required for rel001-parity-diff" >&2
  exit 1
fi
