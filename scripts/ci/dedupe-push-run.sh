#!/usr/bin/env bash
# PLAN-37 (P-7): skip the heavy jobs of a `push` run on main when the pull request that produced this commit already
# tested the identical tree in a successful run. Fails open: anything other than a proven match runs everything.
#
#   pull_request: write the tested tree (git rev-parse HEAD^{tree} of the PR test merge commit) to $TREE_FILE, which
#                 the workflow uploads as the artifact "tested-tree"; skip=false.
#   push:         find the PR merged into $GITHUB_SHA, its latest run of this workflow for the PR head; when that run
#                 succeeded and its "tested-tree" artifact equals HEAD^{tree}: skip=true and a job summary naming the
#                 run. No PR, another tree, a failed or missing run, an API or download error: skip=false.
#
# Inputs (environment): GITHUB_EVENT_NAME, GITHUB_SHA, GITHUB_REPOSITORY, GITHUB_SERVER_URL, GH_TOKEN, WORKFLOW_FILE
# (default ci.yml), TREE_FILE (default artifacts/ci/tested-tree.txt). Writes skip=<true|false> to $GITHUB_OUTPUT and
# a line to $GITHUB_STEP_SUMMARY. GH_BIN and GIT_BIN replace gh and git in the unit tests. Always exits 0.
set -uo pipefail

GH_BIN="${GH_BIN:-gh}"
GIT_BIN="${GIT_BIN:-git}"
WORKFLOW_FILE="${WORKFLOW_FILE:-ci.yml}"
TREE_FILE="${TREE_FILE:-artifacts/ci/tested-tree.txt}"
OUTPUT="${GITHUB_OUTPUT:-/dev/null}"
SUMMARY="${GITHUB_STEP_SUMMARY:-/dev/null}"
SERVER="${GITHUB_SERVER_URL:-https://github.com}"

gh_cmd() { $GH_BIN "$@"; }
git_cmd() { $GIT_BIN "$@"; }

decide() {  # skip(true|false) reason
  printf 'skip=%s\n' "$1" >> "$OUTPUT"
  printf '[dedupe] skip=%s: %s\n' "$1" "$2"
  if [ "$1" = "true" ]; then
    printf '### Heavy jobs skipped\n\n%s\n' "$2" >> "$SUMMARY"
  else
    printf '### Full run\n\n%s\n' "$2" >> "$SUMMARY"
  fi
  exit 0
}

tree="$(git_cmd rev-parse 'HEAD^{tree}' 2>/dev/null)" || decide false "could not read HEAD^{tree}"
[[ "$tree" =~ ^[0-9a-f]{40}$ ]] || decide false "could not read HEAD^{tree}"

case "${GITHUB_EVENT_NAME:-}" in
  pull_request)
    mkdir -p "$(dirname "$TREE_FILE")" && printf '%s\n' "$tree" > "$TREE_FILE" \
      || decide false "could not write $TREE_FILE"
    decide false "pull request run: recorded the tested tree $tree"
    ;;
  push) ;;
  *) decide false "event ${GITHUB_EVENT_NAME:-none} is not a push" ;;
esac

repo="${GITHUB_REPOSITORY:-}"
sha="${GITHUB_SHA:-}"
[ -n "$repo" ] && [ -n "$sha" ] || decide false "GITHUB_REPOSITORY or GITHUB_SHA missing"

pr="$(gh_cmd api "repos/$repo/commits/$sha/pulls" --jq '[.[] | select(.merged_at != null)] | first | .number // empty' 2>/dev/null)" \
  || decide false "could not list the pull requests of $sha"
[[ "$pr" =~ ^[0-9]+$ ]] || decide false "no merged pull request produced $sha"

head="$(gh_cmd api "repos/$repo/pulls/$pr" --jq '.head.sha' 2>/dev/null)" || decide false "could not read pull request #$pr"
[[ "$head" =~ ^[0-9a-f]{40}$ ]] || decide false "pull request #$pr has no head commit"

# The most recent run of this workflow for the PR head, whatever its result: only a success may be reused.
run="$(gh_cmd api "repos/$repo/actions/workflows/$WORKFLOW_FILE/runs?event=pull_request&head_sha=$head&per_page=20" \
  --jq '.workflow_runs | sort_by(.created_at) | last | "\(.id) \(.status) \(.conclusion)"' 2>/dev/null)" \
  || decide false "could not list the runs of pull request #$pr"
read -r run_id run_status run_conclusion <<< "$run"
[[ "${run_id:-}" =~ ^[0-9]+$ ]] || decide false "pull request #$pr has no $WORKFLOW_FILE run for $head"
[ "$run_status" = "completed" ] && [ "$run_conclusion" = "success" ] \
  || decide false "the latest run $run_id of pull request #$pr is $run_status/$run_conclusion, not a success"

dir="$(mktemp -d 2>/dev/null)" || decide false "no temporary directory"
gh_cmd run download "$run_id" --repo "$repo" --name tested-tree --dir "$dir" > /dev/null 2>&1 \
  || decide false "could not download the tested-tree artifact of run $run_id"
tested="$(tr -d '[:space:]' < "$dir/$(basename "$TREE_FILE")" 2>/dev/null)"
rm -rf "$dir"
[[ "$tested" =~ ^[0-9a-f]{40}$ ]] || decide false "run $run_id has no readable tested tree"

if [ "$tested" = "$tree" ]; then
  decide true "Pull request #$pr already tested this exact tree ($tree) in run $SERVER/$repo/actions/runs/$run_id (success)."
fi
decide false "pull request #$pr tested tree $tested, this commit has $tree"
