#!/usr/bin/env bash
# SUP-001-E: open GitHub Issues for open Dependabot alerts missing a SUP-001 tracking issue
set -euo pipefail
REPO="${GITHUB_REPOSITORY:?}"

if ! command -v gh >/dev/null 2>&1; then
  echo "gh CLI required"
  exit 1
fi

# List open Dependabot alerts (requires security_events / dependabot alerts permission)
alerts_json=$(gh api "repos/${REPO}/dependabot/alerts?state=open&per_page=50" 2>/dev/null || echo '[]')
count=$(echo "$alerts_json" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(len(d) if isinstance(d,list) else 0)')
echo "Open Dependabot alerts: $count"
if [[ "$count" == "0" ]]; then
  exit 0
fi

echo "$alerts_json" | python3 -c '
import json, sys, subprocess, os
repo = os.environ["GITHUB_REPOSITORY"]
alerts = json.load(sys.stdin)
if not isinstance(alerts, list):
    sys.exit(0)
existing = subprocess.check_output(
    ["gh", "issue", "list", "--repo", repo, "--label", "SUP-001,advisory", "--state", "open", "--json", "title", "--limit", "100"],
    text=True,
)
titles = {i["title"] for i in json.loads(existing)}
for a in alerts:
    sev = (a.get("security_advisory") or {}).get("severity") or "unknown"
    ghsa = (a.get("security_advisory") or {}).get("ghsa_id") or str(a.get("number"))
    pkg = ((a.get("security_vulnerability") or {}).get("package") or {}).get("name") or "unknown"
    title = f"[SUP-001][{sev}] {pkg} ({ghsa})"
    if title in titles:
        print(f"skip existing: {title}")
        continue
    body = f"""Automated from Dependabot alert (SUP-001-E).

- Package: `{pkg}`
- Severity: **{sev}**
- Advisory: {ghsa}
- Alert URL: {a.get("html_url", "")}
- SLA: see docs/DEPENDENCY_UPDATE_SLA.md
"""
    subprocess.check_call([
        "gh", "issue", "create", "--repo", repo,
        "--title", title,
        "--label", "SUP-001,advisory",
        "--body", body,
    ])
    print(f"opened: {title}")
'
