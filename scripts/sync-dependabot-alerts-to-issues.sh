#!/usr/bin/env bash
# SUP-001-E: open GitHub Issues for open Dependabot alerts missing a SUP-001 tracking issue.
# PLAN-35 (G-QA-11): a refused alerts request (401/403/404, missing "Dependabot alerts: read") fails the step with the
# reason; it is never reported as "0 alerts". GITHUB_TOKEN cannot read Dependabot alerts: the workflow passes the
# secret DEPENDABOT_ALERTS_TOKEN (fine-grained PAT, Dependabot alerts: read, Issues: write) as GH_TOKEN.
set -euo pipefail
REPO="${GITHUB_REPOSITORY:?}"

if ! command -v gh >/dev/null 2>&1; then
  echo "::error::gh CLI required"
  exit 1
fi

err=$(mktemp)
trap 'rm -f "$err"' EXIT
# --slurp: one JSON array of pages, so a repository with more than 100 open alerts is not truncated.
if ! alerts_json=$(gh api --paginate --slurp "repos/${REPO}/dependabot/alerts?state=open&per_page=100" 2>"$err"); then
  echo "::error::Dependabot alerts request for ${REPO} was refused: $(tr '\n' ' ' < "$err")"
  echo "::error::The token needs 'Dependabot alerts: read' on ${REPO}. Set the secret DEPENDABOT_ALERTS_TOKEN (fine-grained PAT); GITHUB_TOKEN cannot read alerts."
  exit 1
fi

alerts_json=$(echo "$alerts_json" | python3 -c '
import json, sys
pages = json.load(sys.stdin)
if not isinstance(pages, list) or not all(isinstance(p, list) for p in pages):
    print("unexpected Dependabot alerts response: " + json.dumps(pages)[:300], file=sys.stderr)
    sys.exit(1)
print(json.dumps([a for p in pages for a in p]))
')
count=$(echo "$alerts_json" | python3 -c 'import json,sys; print(len(json.load(sys.stdin)))')
echo "Open Dependabot alerts: $count"
if [[ "$count" == "0" ]]; then
  exit 0
fi

echo "$alerts_json" | python3 -c '
import json, sys, subprocess, os
repo = os.environ["GITHUB_REPOSITORY"]
alerts = json.load(sys.stdin)
existing = subprocess.check_output(
    ["gh", "issue", "list", "--repo", repo, "--label", "SUP-001,advisory", "--state", "open", "--json", "title", "--limit", "1000"],
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
