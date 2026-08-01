#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
pwsh "$ROOT/scripts/rel001-reset-staging.ps1" "$@"
