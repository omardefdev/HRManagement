#!/usr/bin/env bash
# SUP-001-C: reject expired Crit/High exceptions; validate schema fields
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FILE="${1:-$ROOT/supply-chain-exceptions.json}"
TODAY=$(date -u +%Y-%m-%d)

if [[ ! -f "$FILE" ]]; then
  echo "Missing $FILE"
  exit 1
fi

python3 - "$FILE" "$TODAY" <<'PY'
import json, sys, datetime
path, today_s = sys.argv[1], sys.argv[2]
today = datetime.date.fromisoformat(today_s)
with open(path, encoding="utf-8") as f:
    data = json.load(f)
ex = data.get("exceptions") or []
failed = False
for i, row in enumerate(ex):
    for key in ("id", "packageOrCve", "severity", "owner", "expiresOn", "reason"):
        if not row.get(key):
            print(f"FAIL: exception[{i}] missing {key}")
            failed = True
    exp = row.get("expiresOn")
    if exp:
        try:
            d = datetime.date.fromisoformat(str(exp)[:10])
        except ValueError:
            print(f"FAIL: exception[{i}] bad expiresOn={exp}")
            failed = True
            continue
        if d < today:
            print(f"FAIL: expired exception id={row.get('id')} expiresOn={exp}")
            failed = True
if failed:
    sys.exit(1)
print(f"Supply-chain exceptions OK ({len(ex)} active).")
PY
