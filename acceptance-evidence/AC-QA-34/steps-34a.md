1. Read artifacts\ci-local\head/summary.json written by run-steps.sh inside the ci-local container.
2. Rule: gitSha == `git rev-parse HEAD` (4115e9d11df78f79c44fc659cfa87467aafc235e), dirtyTracked empty, only a global.json rollForward override, every
   step of scripts/ci/steps.json passed, complete = true.
3. Negative: the rule rejects the same summary with another gitSha and with one failed step.
4. Rule P-3: on Docker Desktop, Testcontainers inside the ci-local container times out intermittently through the
   desktop port proxy, so the evidence run uses -SkipTestcontainers (the step is "not-run", never dropped) and the
   Testcontainers suite runs natively on Windows for the same HEAD (artifacts/plan34/windows/tc.trx, 0 failures);
   the coverage gate, which needs that coverage, is "not-run" too and runs on the native unit + integration +
   Testcontainers reports (artifacts/plan34/windows/coverage-gate.log).