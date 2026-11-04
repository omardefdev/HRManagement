#!/usr/bin/env bash
# PLAN-41 (P-4): when a pull request or a push changes acceptance-evidence/**, the committed summary must come from a CI run
# of this repository and be fresh against the code of the commit. Nothing changed, or not a GitHub event: nothing to check.
set -euo pipefail

case "${GITHUB_EVENT_NAME:-}" in
  pull_request|push) ;;
  *) echo "committed-evidence: not a pull request or push run, nothing to check"; exit 0 ;;
esac

# The checkout is shallow; the first parent of the commit under test is the base of the change.
if ! git rev-parse -q --verify 'HEAD^1' > /dev/null; then
  git fetch -q --depth=2 origin "${GITHUB_SHA:?GITHUB_SHA is not set}"
fi
changed="$(git diff --name-only 'HEAD^1' HEAD -- acceptance-evidence | wc -l)"
if [ "$changed" -eq 0 ]; then
  echo "committed-evidence: acceptance-evidence/ did not change, nothing to check"
  exit 0
fi

echo "committed-evidence: $changed file(s) changed under acceptance-evidence/"
pwsh -NoProfile -File scripts/acceptance-baseline-validate.ps1 -Committed
