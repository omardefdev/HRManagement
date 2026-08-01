#!/usr/bin/env bash
# UX-001-C: fail if src views/html reference known public CDNs
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FAIL=0
# Ignore vendored third-party lib comments that mention jsdelivr
while IFS= read -r -d '' f; do
  if grep -nE 'cdn\.jsdelivr\.net|cdnjs\.cloudflare\.com|unpkg\.com|cdn\.bootcdn\.net' "$f" \
    | grep -vE 'wwwroot/lib/' >/dev/null 2>&1; then
    echo "FAIL: external CDN reference in $f"
    grep -nE 'cdn\.jsdelivr\.net|cdnjs\.cloudflare\.com|unpkg\.com|cdn\.bootcdn\.net' "$f" || true
    FAIL=1
  fi
done < <(find "$ROOT/src" \( -name '*.cshtml' -o -name '*.html' \) ! -path '*/wwwroot/lib/*' -print0)

if [[ "$FAIL" -ne 0 ]]; then
  echo "External CDN check failed (UX-001-C)."
  exit 1
fi
echo "No external CDN references in src views (UX-001)."
