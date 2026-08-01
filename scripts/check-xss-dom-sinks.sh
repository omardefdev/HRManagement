#!/usr/bin/env bash
# UX-001-E: flag first-party JS using innerHTML with likely dynamic content
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FAIL=0
# First-party app JS only
for f in \
  "$ROOT/src/Shared/HRManagement.Web.Shared/wwwroot/js/shared-ui.js" \
  "$ROOT/src/Shared/HRManagement.Web.Shared/wwwroot/js/entity-pickers.js" \
  "$ROOT/src/Shared/HRManagement.Web.Shared/wwwroot/js/file-upload.js" \
  "$ROOT/src/Shared/HRManagement.Web.Shared/wwwroot/js/live-notifications.js" \
  "$ROOT/src/Shared/HRManagement.Web.Shared/wwwroot/js/display-names.js"
do
  [[ -f "$f" ]] || continue
  if grep -nE '\.innerHTML\s*=' "$f"; then
    echo "FAIL: innerHTML assignment in $f (UX-001 — use textContent/createElement)"
    FAIL=1
  fi
done

if [[ "$FAIL" -ne 0 ]]; then
  exit 1
fi
echo "First-party JS innerHTML gate OK (UX-001)."
