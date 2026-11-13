## PLAN-52 — branch scope on everything a branch manager reaches, and a reflective BOLA test (IAM-D)

**What changes**
- **One scope mechanism.** Each service has a registry of scope keys, translated to SQL on PostgreSQL and SQLite. Every controller declares a stance: `[OrgScoped]`, `[OrgScopeCentralOnly]`, `[OrgScopeNotApplicable]` or `[RecruitmentOrgScope]`. One action filter, at order −3000, resolves every id of a restricted user's request (route, query, form, model, list) before the action runs: out of scope → 404, or the older 403 where a test pins it; Denied or central-only → 403. `OrgScopeRequirement` is removed.
- **HR.Core:** all 81 controllers declare a stance. Lists, exports and pick-lists are filtered. HCM052 gives Diwan and inspections a unit and stamps the workflow instances' unit, under FORCE, by the least-privileged migrator.
- **HR.Contracts:** the hire wizard answers 404 by `transactionId`. Export jobs record the requester's scope.
- **Admin.Logistics:** orders, receipts and maintenance are scoped; catalogue writes are central-only. HR.Movements: transfers and loans need both units; the three employee-only types are central, also for a delegate of a branch-scoped principal.
- **Transfers:** a transferred restricted user loses his scope (fail-closed; in Keycloak too when the sync is on). HR.Health's snapshot follows the transfer.
- **BOLA v2:** `OrgScopeCoverageTests` reflects over the fifteen web assemblies, with lists that only shrink; only `IAM-H` latent rows remain. BOLA v1 is deleted. AC-QA-34d reads v2.

**Proof**
- AC-IAM-52a..e (prod-like `main`, required).
- AC-02/03/06/32 and P-14's lab, prod-like and leastpriv lists, green locally.
- The generated HTTP tests over the four registries: every id, every page bare and with the user's own ids.
- The full Testcontainers step: 227 passed, 1 skipped.
- The demo server runs `92a69960c826` with `e2e-branch-manager` (HR): the four PLAN-52 server checks pass, and the PLAN-36..51 server checks stay green.

Report: `reports/plan52-final-report.md`. Progress: `reports/plan52-progress.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
