#!/usr/bin/env bash
# PLAN-66: a job with no work in this run says why in its summary: it reuses a green full run (S2), or the rule left the governance
# checks out of this run (S3).
set -euo pipefail
summary="${GITHUB_STEP_SUMMARY:-/dev/null}"
if [ -n "${REUSED_RUN:-}" ]; then
  url="${GITHUB_SERVER_URL:-https://github.com}/${GITHUB_REPOSITORY:-}/actions/runs/${REUSED_RUN}"
  printf '### Reused run %s\n\nThis commit leaves the code tree unchanged, so this job did its work in %s.\n' "$REUSED_RUN" "$url" >> "$summary"
  echo "reused-note: reused run $REUSED_RUN ($url)"
else
  printf '### Not run by rule\n\nThis pull request changes nothing the governance checks read (D-QA-66-1).\n' >> "$summary"
  echo "reused-note: not run by rule"
fi
