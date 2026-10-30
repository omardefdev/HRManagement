#!/usr/bin/env bash
# PLAN-35 (triage rule 3): prints the dotnet test filter suffix for the E2E cases quarantined in tests/e2e/quarantine.json
# ("&FullyQualifiedName!~<test>" per entry with status Quarantined) and lists them in the job summary. Empty output when
# nothing is quarantined. Every entry is a dated obligation (QA-E2E-QUARANTINE).
set -euo pipefail
file="${1:-tests/e2e/quarantine.json}"
python3 - "$file" <<'PY'
import json, os, sys
entries = [e for e in json.load(open(sys.argv[1], encoding="utf-8")).get("entries", []) if e.get("status") == "Quarantined"]
for e in entries:
    for k in ("test", "reason", "owner", "reviewBy"):
        if not e.get(k):
            print(f"quarantine entry without {k}: {e}", file=sys.stderr)
            sys.exit(1)
summary = os.environ.get("GITHUB_STEP_SUMMARY")
if summary:
    with open(summary, "a", encoding="utf-8") as s:
        s.write(f"### E2E quarantine (tests/e2e/quarantine.json): {len(entries)} case(s)\n\n")
        for e in entries:
            s.write(f"- `{e['test']}` - {e['reason']} (owner {e['owner']}, review by {e['reviewBy']})\n")
for e in entries:
    print(f"quarantined E2E case: {e['test']} (owner {e['owner']}, review by {e['reviewBy']})", file=sys.stderr)
print("".join(f"&FullyQualifiedName!~{e['test']}" for e in entries))
PY
