#!/usr/bin/env bash
# SUP-001-B: fail if Dockerfile FROM uses :latest or a ref not in docker/base-image-pins.json
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PINS="$ROOT/docker/base-image-pins.json"
FAIL=0

if [[ ! -f "$PINS" ]]; then
  echo "Missing $PINS"
  exit 1
fi

while IFS= read -r -d '' df; do
  while IFS= read -r line; do
    [[ "$line" =~ ^[[:space:]]*# ]] && continue
    if [[ "$line" =~ ^FROM[[:space:]]+ ]]; then
      ref=$(echo "$line" | awk '{print $2}' | tr -d '\r')
      # strip AS alias
      ref="${ref%%@*}"
      if [[ "$ref" == *":latest" ]] || [[ "$ref" == "latest" ]]; then
        echo "FAIL: $df uses floating :latest → $line"
        FAIL=1
        continue
      fi
      if ! grep -qF "\"$ref\"" "$PINS"; then
        # allow multi-stage COPY from previous stages (no slash = stage name)
        if [[ "$ref" != */* ]]; then
          continue
        fi
        echo "FAIL: $df FROM not in base-image-pins.json → $ref"
        FAIL=1
      fi
    fi
  done < "$df"
done < <(find "$ROOT" -name 'Dockerfile*' -type f ! -path '*/node_modules/*' -print0)

if [[ "$FAIL" -ne 0 ]]; then
  echo "Base image pin check failed (SUP-001-B)."
  exit 1
fi
echo "Base image pin check OK."
