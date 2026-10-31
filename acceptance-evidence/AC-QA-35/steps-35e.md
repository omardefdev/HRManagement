1. Negative: actionlint (docs/qa/tool-pins.json) on a trial workflow whose run step carries a literal "\n" → exit 1,
   shellcheck SC1012 (WorkflowYamlLintTests checks the same shape in CI).
2. gh run list --workflow e2e-journeys.yml --branch main --event workflow_dispatch: a successful run.
3. Its recruitment-journey job log prints "Passed: 1, skipped: 0" twice (lab profile, then prod-like profile).