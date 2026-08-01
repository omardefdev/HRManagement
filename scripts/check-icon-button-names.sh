#!/usr/bin/env bash
# UX-002-E wrapper
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
python3 "$ROOT/scripts/check-icon-button-names.py"
