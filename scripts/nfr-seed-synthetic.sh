#!/usr/bin/env bash
# NFR-001 synthetic seed wrapper (generates SQL via pwsh or documents fallback).
# Usage: bash scripts/nfr-seed-synthetic.sh 10000
set -euo pipefail
EMPLOYEES="${1:-10000}"
OUT="${NFR_SEED_OUT:-data/nfr-seed}"

if command -v pwsh >/dev/null 2>&1; then
  pwsh -File scripts/nfr-seed-synthetic.ps1 -Employees "$EMPLOYEES" -OutDir "$OUT"
  exit 0
fi

echo "pwsh not found — writing minimal manifest only to ${OUT}"
mkdir -p "$OUT"
cat > "${OUT}/manifest.json" <<EOF
{"employees":${EMPLOYEES},"scale_factor":$(awk "BEGIN{printf \"%.6f\", ${EMPLOYEES}/5000000}"),"note":"Install PowerShell to generate full SQL"}
EOF
echo "Done (manifest only)."
