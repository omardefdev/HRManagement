# PLAN-49: what the policy engine governs, facts that match their schemas, and no dead or orphan rules

Specification: `docs/acceptance-plans/execution/PLAN-49-AGENT-PROMPT.md`. Final report: `reports/plan49-final-report.md`.

## What changes

- **One domain catalog in code** (`PolicyDomainCatalog`): five governed domains (`Core.Recruitment`, `Core.Establishment`,
  `Assets.Custody`, `IDCard.Lifecycle`, `Contracts.Leave`) and fifteen out of scope. The validator, the engine, the internal
  endpoint, the start-up check, the policy page and `docs/POLICY_DOMAIN_REGISTRY.md` read it; there is no second registry.
- **The five governed domains run in Shadow** in the Production and Staging files and on the prod-like stack. No domain is in
  Enforce in any production file, and a test forbids it.
- **The fifteen other domains are retired** by a final published version with no rules (four append-only change sets and a
  migration that adds two nullable columns). The enforcement client stays only in the three services that call it.
- **No effective rule** with an effect nobody consumes, a placeholder legal value or an orphan `TriggerWorkflow`; the start-up
  inventory says so at every start, for the national templates and for each agency's overrides.
- **Facts match their schemas.** Leave asks the engine on create, update, approve and the self-service event; the
  establishment submit sends the actor's real scope.
- **A policy may refuse an approval or start the workflow the service asked for, never another one** (G-POL-09).
- The ClamAV host port of the acceptance stack is gone (PLAN-48's defect).

## Proof

- Unit 6012 / 6012; the plan's integration filter with Testcontainers 141 passed, 1 skipped; the validation scripts pass.
- AC-POL-49a..g pass locally on images built from the final tree; this run's artifact is the evidence of record.
- The demo server runs `046316bde9e7` (the commit of phase P5): fifteen templates retired, five domains in Shadow with live
  ledger rows; the server checks of PLAN-36..48 and of this plan are in the final report.

## After the first run

The first run of this PR (`42974750826`) had one red step: the Testcontainers step of the `CI` job, where five tests of
`CrossTenantRlsIntegrationTests` got 53300 (no connection slot left). The `meta` stage and the `Acceptance` roll-up were red
with it, because two meta tests read that job's result; every other check was green.

- **Cause:** every test of a collection takes a database of its own, so a pool of its own, and Npgsql keeps an idle pooled
  connection for five minutes. This plan's ten new PostgreSQL tests added 21 idle connections to the collection's one server
  (100 slots): it stood at 90 connections, 89 of them idle, when the RLS proofs began, and they take eighteen databases.
- **Fix (`3005a21c`):** the connection strings of `PostgreSqlFixture` close an idle connection after five seconds;
  `PostgreSqlFixtureIdleConnectionTests` proves it on the server and fails with Npgsql's defaults. A connection that is never
  returned to its pool is not closed by this, so a real leak would still fill the server.
- **Measured on the host, same filter, before and after:** 10 failed of 211, then 0 of 212; the shared server's peak 90,
  then 25.

## For the reviewer

- New CI stage job `prodlike-policy` (the overlay stage `prodLike:pol49`, AC-POL-49f and the boot check).
- `scripts/ci/acceptance-job.ps1`: one block for runs without `-Stage` only (the stage jobs are untouched).
- Deviations from the prompt and the reasons: final report, section 3.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
