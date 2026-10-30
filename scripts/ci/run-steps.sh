#!/usr/bin/env bash
# QA-034: runs the CI build-and-test steps from scripts/ci/steps.json (the single source of those steps).
#   bash scripts/ci/run-steps.sh --step "Unit tests"      # one step (what .github/workflows/ci.yml does)
#   bash scripts/ci/run-steps.sh --step "Restore (locked mode)" --step "Build"   # several, in steps.json order
#   bash scripts/ci/run-steps.sh --all [--continue-on-error] [--skip "<name>"]... [--out DIR]
# Each step runs as GitHub runs a `run:` step on Linux: bash --noprofile --norc -eo pipefail, with the step's env.
# After a failure, --all skips the remaining steps (reported "skipped") except those marked "always", unless
# --continue-on-error. --skip marks a step "not-run" explicitly (never silently omitted).
# Writes DIR/summary.json (default artifacts/ci-local): gitSha, dirtyTracked, knownLocalOverrides, per-step
# exit/durationSec/status, and trx counts. Exit code: 0 only if every executed step exited 0.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
STEPS_FILE="scripts/ci/steps.json"
OUT="artifacts/ci-local"
MODE=""
SELECTED=()
CONTINUE=0
SKIPS=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --all) MODE="all"; shift ;;
    --step) MODE="one"; SELECTED+=("${2:?--step needs a name}"); shift 2 ;;
    --continue-on-error) CONTINUE=1; shift ;;
    --skip) SKIPS+=("${2:?--skip needs a name}"); shift 2 ;;
    --out) OUT="${2:?--out needs a directory}"; shift 2 ;;
    -h|--help) sed -n '2,9p' "$0"; exit 0 ;;
    *) echo "run-steps.sh: unknown argument $1" >&2; exit 2 ;;
  esac
done
if [[ -z "$MODE" ]]; then echo "run-steps.sh: pass --all or --step \"<name>\"" >&2; exit 2; fi

PY="$(command -v python3 || command -v python || true)"
if [[ -z "$PY" ]]; then echo "run-steps.sh: python3 is required" >&2; exit 2; fi

mkdir -p "$OUT/logs" "$OUT/steps"

# name<TAB>always for every step, in order
mapfile -t STEP_ROWS < <("$PY" - "$STEPS_FILE" <<'PY'
import json, sys
for s in json.load(open(sys.argv[1], encoding="utf-8"))["steps"]:
    print(s["name"] + "\t" + ("1" if s.get("always") else "0"))
PY
)

# Writes the step's env exports and its bash body to files for the given name.
materialise_step() {
  "$PY" - "$STEPS_FILE" "$1" "$2" "$3" <<'PY'
import json, shlex, sys
steps = json.load(open(sys.argv[1], encoding="utf-8"))["steps"]
match = [s for s in steps if s["name"] == sys.argv[2]]
if len(match) != 1:
    sys.stderr.write("step not found (or not unique) in steps.json: %s\n" % sys.argv[2])
    sys.exit(3)
s = match[0]
with open(sys.argv[3], "w", encoding="utf-8", newline="\n") as f:
    for k, v in (s.get("env") or {}).items():
        f.write("export %s=%s\n" % (k, shlex.quote(str(v))))
with open(sys.argv[4], "w", encoding="utf-8", newline="\n") as f:
    f.write(s["run"] + "\n")
PY
}

slug() { echo "$1" | tr -c 'A-Za-z0-9._-' '_' | cut -c1-80; }

record() { # name status exit duration log
  "$PY" - "$OUT/steps" "$1" "$2" "$3" "$4" "$5" <<'PY'
import json, os, sys, time
d, name, status, code, dur, log = sys.argv[1:7]
row = {"name": name, "status": status, "exit": None if code == "" else int(code),
       "durationSec": None if dur == "" else round(float(dur), 1), "log": log,
       "finishedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
safe = "".join(c if c.isalnum() or c in "._-" else "_" for c in name)[:80]
json.dump(row, open(os.path.join(d, safe + ".json"), "w", encoding="utf-8"), indent=2)
PY
}

run_one() { # name -> exit code of the step
  local name="$1" s envf runf log start end code
  s="$(slug "$name")"
  envf="$OUT/steps/$s.env.sh"; runf="$OUT/steps/$s.run.sh"; log="$OUT/logs/$s.log"
  materialise_step "$name" "$envf" "$runf" || return 3
  echo "::group::$name"
  start=$(date +%s.%N)
  ( set -a; . "$envf"; set +a; exec bash --noprofile --norc -eo pipefail "$runf" ) 2>&1 | tee "$log"
  code=${PIPESTATUS[0]}
  end=$(date +%s.%N)
  echo "::endgroup::"
  local dur; dur=$(awk "BEGIN{print $end-$start}")
  if [[ $code -eq 0 ]]; then record "$name" "passed" "$code" "$dur" "$log"
  else record "$name" "failed" "$code" "$dur" "$log"; echo "::error::CI step failed (exit $code): $name"; fi
  return $code
}

write_summary() {
  "$PY" - "$OUT" "$STEPS_FILE" <<'PY'
import glob, json, os, subprocess, sys, time, xml.etree.ElementTree as ET
out, steps_file = sys.argv[1], sys.argv[2]
def git(*a, cwd="."):
    try:
        return subprocess.run(["git", *a], cwd=cwd, capture_output=True, text=True, check=True).stdout
    except Exception:
        return None
order = [s["name"] for s in json.load(open(steps_file, encoding="utf-8"))["steps"]]
rows = {}
for f in glob.glob(os.path.join(out, "steps", "*.json")):
    r = json.load(open(f, encoding="utf-8"))
    rows[r["name"]] = r
steps = [rows.get(n, {"name": n, "status": "not-recorded", "exit": None, "durationSec": None}) for n in order]

def dirty(cwd):
    # the host checkout (CI_SOURCE_DIR) may be a Windows tree with core.autocrlf=true, which Linux git does not know
    crlf = os.environ.get("CI_SOURCE_AUTOCRLF") if cwd == os.environ.get("CI_SOURCE_DIR") else None
    args = (["-c", "core.autocrlf=" + crlf] if crlf else []) + ["status", "--porcelain", "-z", "--untracked-files=no"]
    s = git(*args, cwd=cwd)
    if s is None:
        return None
    entries, parts, i = [], s.split("\0"), 0
    while i < len(parts):
        e = parts[i]
        if len(e) > 3:
            entries.append(e[3:])
            if e[0] in "RC":
                i += 1  # rename/copy: the next field is the source path
        i += 1
    return sorted(entries)

def overrides(cwd, dirty_list):
    known = []
    if dirty_list and "global.json" in dirty_list:
        head = git("show", "HEAD:global.json", cwd=cwd)
        try:
            work = json.load(open(os.path.join(cwd, "global.json"), encoding="utf-8"))
            committed = json.loads(head) if head else None
        except Exception:
            committed, work = None, None
        if committed and work:
            c, w = dict(committed.get("sdk", {})), dict(work.get("sdk", {}))
            c.pop("rollForward", None); w.pop("rollForward", None)
            if c == w and {k: v for k, v in committed.items() if k != "sdk"} == {k: v for k, v in work.items() if k != "sdk"}:
                known.append("global.json (rollForward only: %s -> %s)" % (committed["sdk"].get("rollForward"), work["sdk"].get("rollForward")))
    return known

tested_dirty = dirty(".") or []
tested_known = overrides(".", tested_dirty)
source_dir = os.environ.get("CI_SOURCE_DIR")
host = None
if source_dir and os.path.isdir(source_dir):
    hd = dirty(source_dir) or []
    hk = overrides(source_dir, hd)
    host = {"path": source_dir, "gitSha": (git("rev-parse", "HEAD", cwd=source_dir) or "").strip() or None,
            "dirtyTracked": [d for d in hd if not any(k.startswith(d + " ") for k in hk)],
            "knownLocalOverrides": hk}

def trx_counts():
    res = []
    for f in sorted(glob.glob("TestResults/**/*.trx", recursive=True)):
        try:
            c = ET.parse(f).getroot().find(".//{*}ResultSummary/{*}Counters")
            res.append({"file": f, "total": int(c.get("total", 0)), "passed": int(c.get("passed", 0)),
                        "failed": int(c.get("failed", 0)), "skipped": int(c.get("notExecuted", 0))})
        except Exception as e:
            res.append({"file": f, "error": str(e)})
    return res

executed = [s for s in steps if s.get("status") in ("passed", "failed")]
summary = {
    "card": "QA-034",
    "generatedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    "source": os.environ.get("CI_LOCAL_SOURCE", "checkout"),
    "gitSha": (git("rev-parse", "HEAD") or "").strip() or None,
    "dirtyTracked": [d for d in tested_dirty if not any(k.startswith(d + " ") for k in tested_known)],
    "knownLocalOverrides": tested_known,
    "host": host,
    "ok": bool(executed) and all(s["status"] == "passed" for s in executed) and not any(s.get("status") == "failed" for s in steps),
    "counts": {k: sum(1 for s in steps if s.get("status") == k) for k in ("passed", "failed", "skipped", "not-run", "not-recorded")},
    # complete: every step of steps.json ran and passed (no --skip, nothing skipped after a failure).
    "complete": all(s.get("status") == "passed" for s in steps),
    "steps": steps,
    "trx": trx_counts(),
}
json.dump(summary, open(os.path.join(out, "summary.json"), "w", encoding="utf-8"), indent=2)
print("Wrote %s/summary.json  ok=%s  %s" % (out, summary["ok"], summary["counts"]))
PY
}

is_skipped() { local n="$1" s; for s in "${SKIPS[@]+"${SKIPS[@]}"}"; do [[ "$s" == "$n" ]] && return 0; done; return 1; }

is_selected() { local n="$1" s; for s in "${SELECTED[@]}"; do [[ "$s" == "$n" ]] && return 0; done; return 1; }

OVERALL=0
if [[ "$MODE" == "one" ]]; then
  if [[ ${#SELECTED[@]} -eq 1 ]]; then
    run_one "${SELECTED[0]}"; OVERALL=$?
    write_summary >/dev/null || true
    exit $OVERALL
  fi
  # several --step: run them in steps.json order, stop at the first failure unless --continue-on-error
  for s in "${SELECTED[@]}"; do
    found=0
    for row in "${STEP_ROWS[@]}"; do [[ "${row%$'\t'*}" == "$s" ]] && found=1; done
    [[ $found -eq 1 ]] || { echo "run-steps.sh: unknown step: $s" >&2; exit 2; }
  done
  for row in "${STEP_ROWS[@]}"; do
    name="${row%$'\t'*}"
    is_selected "$name" || continue
    if ! run_one "$name"; then OVERALL=1; [[ $CONTINUE -eq 1 ]] || break; fi
  done
  write_summary >/dev/null || true
  exit $OVERALL
fi

rm -f "$OUT"/steps/*.json
FAILED=0
for row in "${STEP_ROWS[@]}"; do
  name="${row%$'\t'*}"; always="${row##*$'\t'}"; always="${always%$'\r'}"
  if is_skipped "$name"; then
    echo "::warning::CI step NOT RUN (--skip): $name"
    record "$name" "not-run" "" "" ""
    continue
  fi
  if [[ $FAILED -eq 1 && $CONTINUE -eq 0 && "$always" != "1" ]]; then
    record "$name" "skipped" "" "" ""
    continue
  fi
  if ! run_one "$name"; then FAILED=1; OVERALL=1; fi
done
write_summary

echo
printf '%-8s %8s  %s\n' "STATUS" "SECONDS" "STEP"
"$PY" - "$OUT/summary.json" <<'PY'
import json, sys
s = json.load(open(sys.argv[1], encoding="utf-8"))
for st in s["steps"]:
    d = "" if st.get("durationSec") is None else "%.1f" % st["durationSec"]
    print("%-8s %8s  %s" % (st["status"].upper()[:8], d, st["name"]))
for t in s["trx"]:
    print("trx %-60s total=%s failed=%s skipped=%s" % (t.get("file"), t.get("total"), t.get("failed"), t.get("skipped")))
PY
exit $OVERALL
