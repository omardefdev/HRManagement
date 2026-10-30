#!/usr/bin/env bash
# PLAN-35 (G-QA-01): gitleaks over the whole git history with .gitleaks.toml (default rules + narrow allowlists), from
# the image pinned by digest in docs/qa/tool-pins.json. Redacted JSON report in artifacts/gitleaks/. Needs a full
# clone (actions/checkout fetch-depth: 0). Fails when gitleaks scanned fewer commits than HEAD has (a git error inside
# the container, e.g. "dubious ownership", must not pass as "no leaks").
set -euo pipefail
ref=$(grep -o '"ref": "zricethezav/gitleaks@sha256:[0-9a-f]*"' docs/qa/tool-pins.json | cut -d'"' -f4)
[ -n "$ref" ] || { echo "::error::no gitleaks pin in docs/qa/tool-pins.json"; exit 1; }
out="${1:-artifacts/gitleaks}"
mkdir -p "$out"
chmod 777 "$out"
expected=$(git rev-list --count --no-merges HEAD)
echo "gitleaks ${ref}; ${expected} non-merge commit(s) reachable from HEAD"
set +e
docker run --rm -e GIT_CONFIG_COUNT=1 -e GIT_CONFIG_KEY_0=safe.directory -e GIT_CONFIG_VALUE_0='*' \
  -v "$PWD:/repo" -v "$PWD/$out:/out" "${ref}" detect --source /repo --config /repo/.gitleaks.toml \
  --redact --no-banner --no-color --exit-code 1 --report-format json --report-path /out/gitleaks.json 2>&1 | tee "$out/gitleaks.log"
code=${PIPESTATUS[0]}
set -e
scanned=$(grep -oE '[0-9]+ commits scanned' "$out/gitleaks.log" | grep -oE '^[0-9]+' || echo 0)
if [ "$code" -ne 0 ]; then
  echo "::error::gitleaks found leaks or failed (exit ${code}); redacted report: ${out}/gitleaks.json"
  exit 1
fi
if [ "${scanned}" -lt "${expected}" ]; then
  echo "::error::gitleaks scanned ${scanned} commit(s), HEAD has ${expected} non-merge commit(s): the history scan did not run"
  exit 1
fi
echo "gitleaks: no leaks in ${scanned} commit(s)"
