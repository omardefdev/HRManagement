#!/usr/bin/env bash
# SUP-001-C fixture: proves Crit/High gate fails without an active exception
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TMP=$(mktemp)
trap 'rm -f "$TMP"' EXIT

cat > "$TMP" <<'EOF'
The following packages have known vulnerabilities:
  > Fake.Vulnerable.Package 1.0.0
    Severity: Critical
    Advisory: GHSA-fixture-sup001
EOF

if bash "$ROOT/scripts/check-supply-chain-exceptions.sh"; then
  :
else
  echo "exceptions file invalid"
  exit 1
fi

# Inline gate logic against fake report (without calling dotnet list)
python3 - "$TMP" "$ROOT/supply-chain-exceptions.json" "$(date -u +%Y-%m-%d)" <<'PY'
import json, re, sys, datetime
report_path, exc_path, today_s = sys.argv[1], sys.argv[2], sys.argv[3]
today = datetime.date.fromisoformat(today_s)
text = open(report_path, encoding="utf-8").read()
active = set()
data = json.load(open(exc_path, encoding="utf-8"))
for row in data.get("exceptions") or []:
    exp = row.get("expiresOn")
    if not exp:
        continue
    d = datetime.date.fromisoformat(str(exp)[:10])
    if d < today:
        continue
    key = (row.get("packageOrCve") or "").strip().lower()
    if key and not key.startswith("license:"):
        active.add(key)
uncovered = []
for line in text.splitlines():
    if not re.search(r"(?i)\b(Critical|High)\b", line):
        continue
    lowered = line.lower()
    if not any(k in lowered for k in active):
        uncovered.append(line)
if not uncovered:
    print("FAIL: fixture expected uncovered Crit/High")
    sys.exit(1)
print("Fixture OK: Crit/High without exception would fail the gate.")
sys.exit(0)
PY
