#!/usr/bin/env bash
# SUP-001-C: NuGet Crit/High gate with optional supply-chain-exceptions.json allowlist
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REPORT="${1:-vuln-report.txt}"
EXCEPTIONS="$ROOT/supply-chain-exceptions.json"
TODAY=$(date -u +%Y-%m-%d)

dotnet list "$ROOT/HRManagement.slnx" package --vulnerable --include-transitive 2>&1 | tee "$REPORT"

python3 - "$REPORT" "$EXCEPTIONS" "$TODAY" <<'PY'
import json, re, sys, datetime
report_path, exc_path, today_s = sys.argv[1], sys.argv[2], sys.argv[3]
today = datetime.date.fromisoformat(today_s)
text = open(report_path, encoding="utf-8", errors="replace").read()
if not re.search(r"(?i)\b(Critical|High)\b", text):
    print("Security scan passed (no Critical/High vulnerabilities).")
    sys.exit(0)

active = set()
try:
    data = json.load(open(exc_path, encoding="utf-8"))
except FileNotFoundError:
    data = {"exceptions": []}
for row in data.get("exceptions") or []:
    exp = row.get("expiresOn")
    if not exp:
        continue
    try:
        d = datetime.date.fromisoformat(str(exp)[:10])
    except ValueError:
        continue
    if d < today:
        continue
    key = (row.get("packageOrCve") or "").strip().lower()
    if key and not key.startswith("license:"):
        active.add(key)

# Extract likely package/CVE tokens near Critical/High lines
findings = []
for line in text.splitlines():
    if not re.search(r"(?i)\b(Critical|High)\b", line):
        continue
    findings.append(line.strip())

uncovered = []
for line in findings:
    lowered = line.lower()
    covered = any(k in lowered for k in active) if active else False
    if not covered:
        uncovered.append(line)

if uncovered:
    print("::error::Vulnerable packages with Critical or High severity (no active exception):")
    for u in uncovered:
        print(u)
    sys.exit(1)

print(f"Security scan: Crit/High findings covered by {len(active)} active exception(s).")
PY
