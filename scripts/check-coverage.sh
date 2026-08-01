#!/usr/bin/env bash
# QA-002: Risk-scoped coverage gate + per-assembly floors (delegates to check-coverage.py).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PYTHON_BIN="${PYTHON_BIN:-}"
if [[ -z "$PYTHON_BIN" ]]; then
  if command -v python3 >/dev/null 2>&1; then
    PYTHON_BIN="python3"
  elif command -v python >/dev/null 2>&1; then
    PYTHON_BIN="python"
  else
    echo "ERROR: python3 or python is required for scripts/check-coverage.py."
    exit 1
  fi
fi

# COVERAGE_THRESHOLD env overrides coverage-floors.json globalThreshold when set by CI.
exec "$PYTHON_BIN" "$ROOT/scripts/check-coverage.py" "$@"
