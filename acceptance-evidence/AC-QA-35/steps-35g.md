1. gh run list --workflow dated-obligations.yml --branch main --event workflow_dispatch: a successful run.
2. Negative: bash scripts/sync-dependabot-alerts-to-issues.sh with an invalid GH_TOKEN → exit 1, "refused" and the
   missing-permission message; never "Open Dependabot alerts: 0".