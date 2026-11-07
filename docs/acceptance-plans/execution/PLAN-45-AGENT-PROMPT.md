# Task: PLAN-45 — Close the custody and loss approval loop

The deliverables:
- **No pending request without an instance.** AdminLogistics saves first (`ApprovalStarting`), then starts the approval, then saves `PendingApproval` with the instance. If HR.Core is down, the request waits as «بانتظار بدء الاعتماد» with the asset held, and a background job starts it later. A retry with the same key returns the same instance.
- **The requester can withdraw,** and the asset is free again.
- **The D-WF-2 chain, enforced:** step 1 `admin-logistics` (not the requester), step 2 `hr-management-approver`, two different people. The `AssetLossReport` exemption is lifted.
- **The approvers see the request** in the work inbox and decide on a generic approval page in HR.Core. The asset page's «عرض الاعتماد» link opens it. PLAN-46 reuses the page and the inbox for the card, discipline and separation types.
- **The outcome reaches AdminLogistics:** the assignment becomes `Active`, or the loss is confirmed.
- **AC-11 and AC-33 really test the approval.**
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**Standing owner instruction on merges** (PLAN-38 to PLAN-44 and PLAN-66): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-14).

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan45-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan45-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check:
- `origin/main`;
- the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan45-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Record the time spent waiting for CI (`WAITING-CI:`).
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session **without ultracode**, at `xhigh`):
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only; root-causing separate failures.
- Run **one independent review**, before the server phase, so that the single server build carries its fixes (PLAN-44 did this). Use a refuter only for findings rated high.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan45/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.

**Waiting for CI:**
- Wait with whatever mechanism this session allows: a background `gh run watch <id> --exit-status` if permitted, or the app's run monitor.
- If the session cannot wait, end the turn with a line that starts with **`WAITING-CI:`** and names the PR, the run id, its URL and the exact next step.
- On the owner's next message (even «اكمل»), check **once** (`gh pr checks <n>`) and continue from that step.
- Never merge on a guess.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior .NET engineer for cross-service workflows:
- approval engines (instances, steps, gates, separation of duties), ASP.NET Core MVC and authorization policies;
- service-to-service calls with acting users (PLAN-42, PLAN-43), idempotent starts, retries and background jobs;
- EF Core, PostgreSQL row level security, optimistic concurrency;
- MassTransit and the outbox patterns of PLAN-40;
- Keycloak roles, Docker Compose, PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev`). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..44 did.

**Where things stand** when this prompt was written (2026-11-07):
- **One plan at a time** (owner, 2026-11-07). Plans run one after another, in the main folder `D:\my projects\HRManagement`, with no other plan session beside them.
  - The second clone is retired.
  - The parallel protocol (`docs/PARALLEL_PLANS.md`, `scripts/parallel/hrm-lock.ps1`) stays in the repository but is not used: no locks, no second clone.
- **`main`** = `aeb6d716`. It holds PLAN-43 (PR 38), PLAN-66 (PR 39) and PLAN-44 (PR 41).
- **Notes that ride with this plan's first commit:**
  - **PLAN-43's and PLAN-44's merge notes** are single commits on the local branches `docs/plan-43-merge-note` (`5a3ce027`) and `docs/plan-44-merge-note` (`eaffc6c6`), not yet on `main`.
  - **PLAN-66's closing notes** are uncommitted in the working tree, copied from the retired second clone: `reports/plan66-progress.md`, `reports/plan66-final-report.md` and `docs/acceptance-plans/PLAN-66-qa-ci-fast-pipeline.md`.
- **Open side PRs** about the E2E Playwright install: #40 (`ci/e2e-playwright-install`) and #42 (`ci/e2e-journeys-playwright-install`). Never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI since PLAN-66:**
  - a full PR run takes about 45 minutes, and one acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`, about 24 minutes);
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - the meta stage runs only when CI files change;
  - `acceptance-job.ps1` takes `-Stage` and `-Classes`;
  - `tests/acceptance/ci-scope.json` has `stageJobs[]` and `metaRule`: a new class goes into `lab[]` or `prodLike[]`, in a group that a stage job runs.
- **From PLAN-43** (its final report, §9 and §12):
  - PLAN-45 rebuilds `ExternalApprovalService` and `WorkflowTriggerService` into the trusted custody start;
  - it raises `ASSET-ASSIGN-APPROVAL` and `ASSET-LOSS-APPROVAL` with `WorkflowPackSeeder.EnsureVersionAsync` and tightens `WorkflowGuardrails`;
  - it lifts the `AssetLossReport` exemption after adding `admin-logistics` to `WorkflowStepAction`;
  - it rewrites AC-11 as a UI journey (since PLAN-43, AC-11's requester is `b-logistics`).
  - Deferred to this plan:
    - **M6:** a start that times out after HR.Core committed leaves the assignment unlinked; the retry with the same key closes it.
    - **L7:** a start with no tenant lands in the national tenant, and a retry can duplicate it.
    - **L8:** a definition can name a role that `WorkflowStepAction` refuses.
- **From PLAN-44** (its final report, §3, §9, §10, §12):
  - Every instance insert goes through `WorkflowInstanceInsert`. A retry with the same key returns the same instance even under a race. A conflict is an Arabic message or 409. A start beside a running instance raises `WorkflowInstanceRunningException`. An outcome that cannot apply raises `WorkflowOutcomeRefusedException`.
  - `WorkflowPlan03Seeder` moved into `WorkflowPackSeeder` ("the multi-step custody method").
  - **L3:** a cancel and a decision can deadlock, because they lock the instance and the entity in opposite orders; the loser gets a 500. Withdraw makes cancels common, so this plan closes it (P-6).
  - **For every plan:**
    - a new handler checks state and link, and runs inside the decision's transaction;
    - a new tenant job sets `TenantIdFlow` before its scope opens a connection;
    - an acceptance test posts the URL the page renders, not one built by hand;
    - run `build-on-host.ps1` detached, because a dropped ssh session lost a full build once.
  - Jobs that read under `IgnoreTenantFilterForSystem` with no tenant may see no row under forced row security.
- **The demo realm has no user with `admin-logistics`.** The only step-2 approver among the demo users is `e2e-checker-mgmt` (`hr-management-approver`). P-13 adds what the demo needs.
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools.
  - The acceptance services have no container health check: readiness means `/health/ready`.
  - Never prune Docker volumes or images of other projects (P-2).

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-45-wf-asset-custody-approval-loop.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report. Its line numbers date from 2026-10-30 and have moved since PLAN-40..44: find the code by its content.
2. `reports/plan43-final-report.md` and `reports/plan44-final-report.md`: the deviations, deferred items and readiness notes named above.
3. `reports/plan40-final-report.md` §11 (the publisher contract) and `docs/EVENT_PUBLISHER_INVENTORY.md`.
4. `docs/CI_AND_TESTING.md` (PLAN-66: the parallel stages, reuse, meta on demand, the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.
5. `WorkflowOutcomeReconciliationJob.cs` (PLAN-44): the pattern for a job that walks tenants under forced row security (tenant list from a platform table, `TenantIdFlow`, `AdvisoryLeaderElection`).
6. `docs/FUN_005_IDENTITY_ASSETS_IT_GAP_STUDY.md` (G-IDA-02), `docs/WF_001_GAP_STUDY.md` (G-WF-02), `docs/WF_002_WORK_INBOX.md`, `docs/SOD_MATRIX.md`, `docs/qa/workflow-step-role-exemptions.json`.
7. `docs/OWNER_DECISIONS_REGISTER.md`: D-WF-2 and D-IDA-4.
8. Every file the plan cites.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **You are in `D:\my projects\HRManagement`, on `main`,** and no other plan session is running (one plan at a time).
2. **`git fetch origin`**, and `origin/main` holds PLAN-43 and PLAN-44 (`aeb6d716` or later).
3. **The working tree is clean,** except for these, which may be present:
   - the owner's local `global.json` edit;
   - `tmp-vendors.html`;
   - git-ignored `artifacts/`;
   - untracked prompt files under `docs/acceptance-plans/execution/`;
   - PLAN-66's three uncommitted notes (see «Where things stand»), which ride with this plan's first commit.

   Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
4. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
5. **`gh auth status`** shows `omardefdev` with the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

Accepted by the owner on 2026-10-31 (wave-1 questionnaire, «accept the recommended» for every plan decision):

- **D-WF-2 / D-IDA-4, the chain.** `ASSET-ASSIGN-APPROVAL` and `ASSET-LOSS-APPROVAL` get a new version with two steps:
  1. «موافقة مسؤول الأصول», `admin-logistics`;
  2. «اعتماد إداري», `hr-management-approver`.

  `RequireDistinctApprovers=true`. Older versions are retired, and running instances finish on their pinned version.
- **When approval is needed:** unchanged — the current policy decision (a privileged asset, or a value above 10,000; `AssetLostRules.RequiresLossApproval`). Per-agency thresholds are wave 2 (D-WF-3).
- **Automatic restart:** every `AssetApproval:Retry:IntervalSeconds=120`, up to `MaxAttempts=30`, then «يتطلب تدخلاً» and a metric. In the acceptance profile, `IntervalSeconds=15`.
- **Who withdraws:** the requester, or any `admin-logistics` in the asset's scope, with a mandatory reason.
- **Existing pending requests:** a script turns `PendingApproval` with no instance into `ApprovalStarting`, so the job picks them up. It touches running instances on old versions only with an explicit option.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-45-wf-asset-custody-approval-loop` from `origin/main`, in the main folder.<br>• **The first commits carry the previous plans' notes:**<br>&nbsp;&nbsp;– cherry-pick `5a3ce027` (PLAN-43's merge note) and `eaffc6c6` (PLAN-44's), keeping both sides if they touch the same file;<br>&nbsp;&nbsp;– commit PLAN-66's three uncommitted notes;<br>&nbsp;&nbsp;– commit this prompt file.<br>• After the cherry-picks, delete the two local note branches (`git branch -d`). |
| P-2 | **Docker hygiene (one plan at a time, so no locks).**<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• Before a Docker-heavy run (`ci-local.ps1`, Testcontainers suites, a stack), check `docker ps`. Remove only this repository's leftovers: compose projects `hrm-*` and Testcontainers.<br>• Never prune volumes or images of other projects. |
| P-3 | **Migrations and rollback safety.**<br>• `WF045_AssetApprovalLoop` in AdminLogistics:<br>&nbsp;&nbsp;– the new fields, nullable or with a database default;<br>&nbsp;&nbsp;– `xmin` on `AssetLossReport`;<br>&nbsp;&nbsp;– no change to the unique index on open assignments.<br>• **New status values go at the end of their enums only.** They are stored as integers. The previous tag may meet a row in `ApprovalStarting` or `Withdrawn` after a rollback: check what its pages and consumers do with an unknown value, and record it.<br>• **No data change in a migration on a tenant table** (PLAN-43's lesson). The new definition versions come from `EnsureVersionAsync` at start-up, inside the tenant, as PLAN-43 and PLAN-44 do.<br>• Run `has-pending-model-changes` for AdminLogistics and HR.Core with the repository's tool manifest, and check that `scripts/build-ef-bundles.sh` picks the migration up. |
| P-4 | **The retry job under forced row security** (PLAN-44's lesson). `AssetApprovalStartRetryJob` is AdminLogistics' first background job.<br>• It walks tenants as `WorkflowOutcomeReconciliationJob` does: a tenant list from a source **outside** row security, `TenantIdFlow` set before each scope opens a connection, and one leader per cycle (`AdvisoryLeaderElection`). If AdminLogistics has no such list, use the narrowest existing source of tenant ids (for example HR.Core's platform list through an existing service read), or **Stop**.<br>• Each start runs inside `ActingUserScope(CreatedBy)` **and the request's tenant**, never the national default. That closes PLAN-43's L7.<br>• **Prove it as the server runs it.** An integration test runs the job as a non-superuser role under forced row security, with `ApprovalStarting` rows in two tenants. Both are picked up, each starts in its own tenant, and a session with no tenant would have found nothing. |
| P-5 | **The start contract.**<br>• `AssetApprovalStarter` builds PLAN-43's `WorkflowApprovalStartRequest`:<br>&nbsp;&nbsp;– `IdempotencyKey = "{Type}:{Id}"`, stable across retries;<br>&nbsp;&nbsp;– `ExcludedApproverUserIds = {CreatedBy}`, and `SubjectEmployeeId` (the recipient or the holder);<br>&nbsp;&nbsp;– `ScopeOrgUnitId` from `EmployeeOrgUnitSnapshot`;<br>&nbsp;&nbsp;– a title with no national id, and the `DetailsUrl` under `/ui/admin-logistics/`.<br>• It returns `(Success, InstanceId, Error)` and never throws.<br>• **A retry for a committed start returns the same instance** (PLAN-44), so the request links to it. That closes PLAN-43's M6. If the instance is already finished, its outcome is applied.<br>• **Rule 1 of §1:** no path writes `PendingApproval` without `WorkflowInstanceId` in the same save, guarded by `Wf045NoPendingWithoutInstanceArchitectureTests`.<br>• Consumers apply an outcome only to a request in `PendingApproval` with the matching instance (PLAN-43). A withdrawn request never changes. |
| P-6 | **Withdraw, and the cancel-decision deadlock** (PLAN-44 L3).<br>• `WithdrawApproval`:<br>&nbsp;&nbsp;– with an instance, cancel in HR.Core first (`CancelAsync`); if HR.Core answers that the instance is finished, apply its outcome instead;<br>&nbsp;&nbsp;– a conflict or 409 becomes the Arabic conflict message;<br>&nbsp;&nbsp;– the reason has at least 10 characters, the actor is recorded, and nothing is deleted.<br>• **Close L3.** Lock the instance and the entity in the same order on the cancel path and the decision path, or map PostgreSQL `40P01` to the conflict message. Prove it with a PostgreSQL test that races a withdraw against the last approval: one wins, the other gets the message, never a 500. |
| P-7 | **Roles, the gate and the exemption.**<br>• `admin-logistics` joins `WorkflowStepAction` and `HrCoreWorkInbox` **only**. Never `ModuleHrCore` or `HrCoreReadOnly`. Its own negative test: 403 on `/ui/hr-core/Employees`. That also closes PLAN-43's L8 for this role.<br>• **`ExternalApprovalGate`:** the unit scope on `ScopeOrgUnitId` for branch-level roles; requester, subject and distinct approvers from `WorkflowSodRules`. Register it in `CreateGates`.<br>• **Only `AssetLossReport` leaves the exemption list** (deviation from the plan's `[]`). The four PLAN-46 types stay until PLAN-46. Change, together:<br>&nbsp;&nbsp;– the three HR.Core settings files and `docker-compose.acceptance-prodlike.yml`;<br>&nbsp;&nbsp;– `DefaultExemptEntityTypes`;<br>&nbsp;&nbsp;– `docs/qa/workflow-step-role-exemptions.json`, the entry closed;<br>&nbsp;&nbsp;– its dated obligation.<br>The PLAN-43 tests that keep these in step stay green. |
| P-8 | **The generic approval page.**<br>• `WorkflowApprovalsController` serves only types registered in `ExternalApprovalTypes`, through `WorkflowInstanceActionService` and the same evaluator as the API: structure, role, SoD rules, `ExternalApprovalGate`. No path is wider than the API.<br>• **GET** needs the holder of a step role in the pinned definition, the requester, or `admin`; otherwise 403.<br>• **POSTs** carry an antiforgery token, the PLAN-40 and PLAN-43 pattern, so the CodeQL gate holds without growing its baseline.<br>• `Facts` are HTML-encoded. `DetailsUrl` keeps PLAN-43's prefix check.<br>• **The gateway:** `/ui/hr-core/WorkflowApprovals` GET → `HrCoreWorkInbox`, POST → `WorkflowStepAction`. `HrModuleAuthorizationFilter` lists the controller. Check that `admin-logistics` reaches `/ui/hr-core/WorkInbox`, and nothing else in HR.Core.<br>• **The inbox:** external types join `SupportedEntityTypes`, and their context comes from the instance. Keep PLAN-44's linkage rule for the five local types; for an external type, the instance is the item. `WorkInboxAudience` takes its roles from `ExternalApprovalTypes.AudienceRoles`. |
| P-9 | **Definitions.**<br>• The new versions come from `EnsureVersionAsync` with `ChangeReason="seed:PLAN-45"`, and the older published versions are retired.<br>• The custody method PLAN-44 moved into `WorkflowPackSeeder` must never publish v1 again: it checks, and retires v1 if it finds it published.<br>• `WorkflowGuardrails` for both types: two steps, the first `admin-logistics`, the last `hr-management-approver`. |
| P-10 | **Existing requests.**<br>• `scripts/wf045-repair-asset-approvals.ps1` (PS 5.1, ASCII): dry run by default; `-Apply` turns `PendingApproval` with no instance into `ApprovalStarting`; `-RestartOnLatest` (cancel, then restart, for instances on a retired version at step 1 with no decision) only when asked.<br>• Its report goes to `artifacts/plan45/` locally and on the server, **not** to a committed `reports/` file, unless the plan's path is needed for a test.<br>• **On the server:** dry run, then `-Apply`. Never `-RestartOnLatest`: running instances finish on their version (the decision). |
| P-11 | **Acceptance placement (PLAN-41 and PLAN-66 rules).**<br>• `AcWf45AssetApprovalLoopTests` goes in `prodLike[]`, `required: true`, in the **isolated** group: AC-WF-45b stops `hr-core-web`.<br>• AC-11 and AC-33 now prove the claim: make them `required: true` where they run, or move them to `prodLike[]` if they need the production settings. Update `ac-ownership.json`: `AC-WF-45` (owner PLAN-45, `provesClaim: true`), and AC-11 and AC-33 (`provesClaim: true`).<br>• Tests post the URL and the token the page renders (PLAN-44).<br>• Copy what the report needs into `artifacts/plan45/`, then restore the committed evidence: local evidence is never committed. |
| P-12 | **Local testing (owner's rule of 2026-11-06).** Locally, run only the acceptance classes this plan touches and their direct regressions, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`. **The full suite runs once, in CI.** The list, at least:<br>• **prod-like:** `AcWf45AssetApprovalLoopTests`, `AcWf43DefinitionGovernanceTests`, `AcWf44InstanceIntegrityTests`, `AcHcm25ProductionApprovalTests`, `AcS2s42TrustBoundaryTests`, `AcMsg40AtomicPublishingTests`;<br>• **lab:** `Ac11PolicyWorkflowTests`, `Ac32LogisticsScopeLiveTests`, `Ac33AssetLossLiveTests`, `AcWf23WorkInboxUiTests`;<br>• and any class P0 finds that touches assets, the inbox, the gateway rules or the policies you change.<br>Known local-only failures of lab classes that need PowerShell 7 (PLAN-43 §6) are recorded, not chased. |
| P-13 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan45-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Build once,** on the final code, and **detached** (`nohup` on the server, then watch for the push), as PLAN-44 learned. Then `scripts/vps/deploy.ps1 -Tag <sha12>`.<br>• **The demo needs a custody chain it does not have.** Add two demo users to the realm manifest's `demoUsers`, with the demo password that already exists in the server's `.env`, never printed: a logistics requester and a logistics checker, both `admin-logistics`, in the demo agency's scope. Use the path that created the existing demo users. Step 2 is `e2e-checker-mgmt`. Record the usernames for the owner's QA guide.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn: `scripts/vps/deploy.ps1 -Rollback` to PLAN-44's tag, safe by P-3. Restore the backup only if data was damaged, prove the demo works, and record the downtime. |
| P-14 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P8, after local CI and the pre-push check are green.<br>2. `gh pr create`; wait («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>` and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-15 | **No regression of PLAN-25 and PLAN-36..44, and PLAN-66.** These stay green:<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests` (unchanged);<br>• `AcWf43`, `AcWf44`, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, and both recruitment journeys;<br>• `OutboxPublishOrderingArchitectureTests`, `Data001ConcurrencyArchitectureTests`, the `Ten002c` row-security tests, `ProductionWiringMatrixTests`, `S2s042InternalRoutesAreServiceOnlyTests`, `GatewayPathAuthorizationMiddlewareTests`, the PLAN-43 exemption tests, the PLAN-66 pipeline tests (`Qa066*`);<br>• `scripts/wf043-validate.ps1`, `scripts/hcm025-validate.ps1`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`.<br>• New service calls go into `deploy/wiring/service-wiring.json`, then `scripts/int037-render-wiring-doc.ps1`. Events that change shape go into `docs/EVENT_PUBLISHER_INVENTORY.md` in the same commit. |

## Scope boundaries

**Out of scope** (from the plan):
- **Done earlier:** publishing after the last save (PLAN-40); the external start, the registry, v2 events and default role enforcement (PLAN-43); concurrency and the unique running-instance index (PLAN-44).
- **PLAN-46:** the general contract (`IWorkflowApprovalClient`, `IWorkflowOutcomeDecided`) and the other external types. Moving `AssetApprovalStarter` onto the shared client is wave 2.
- **Wave 2:** per-agency thresholds (D-WF-3); silent custody closure for transfer and compensation (IDA-D).
- **PLAN-62:** the separation part of custody closure.
- **PLAN-52:** unit scope on the AdminLogistics screens.

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- another plan's untracked prompt file;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- any secret, token or password (templates keep their placeholders, `__SVC_SECRET__` or `${…}`). That includes the demo password.

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- touch the open side PRs (#40, #42) or their branches;
- disable host-key checking;
- give `admin-logistics` any HR.Core access beyond the inbox and the approval page;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, mark a class not required, or keep an exemption to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails.**
- **The retry job cannot find a tenant list** outside row security without new trust between services (P-4).
- **The chain cannot work** without giving `admin-logistics` `ModuleHrCore` or `HrCoreReadOnly` (P-7).
- **AC-HCM-25 or `Hcm025PanelServerParityTests`** would need a change.
- **A server deploy fails,** and neither `-Rollback` nor the restore brings the demo back.

---

## P0 — Timing, preconditions, baseline, inventories

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions.
2. `git switch -c feature/plan-45-wf-asset-custody-approval-loop origin/main`. Bring in the previous plans' notes and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan45-baseline.md`:
   - **0.2:** PLAN-40's guard and PLAN-43's start and v2 events are on `main`.
   - **0.3:** the build.
   - **0.4:** the listed filters. Integration tests follow P-2.
   - **0.5:** assignments and loss reports in `PendingApproval`, with and without an instance, and their instances' versions. Take them from the acceptance data when you next start the stack, and read-only from the server now.
   - **0.6:** the versions of both definitions, and the moved custody seeding method (P-9).
4. **Inventories:**
   - the tenant list a job in AdminLogistics can read outside row security (P-4);
   - how the previous tag treats unknown status values (P-3);
   - every caller of `WorkflowTriggerService` and of the assign and loss actions;
   - every gateway rule and policy that touches `/ui/hr-core/WorkInbox`, `WorkflowApprovals`, `admin-logistics` and `HrCoreWorkInbox`;
   - the acceptance classes that touch assets, the inbox, those rules or those policies (P-12);
   - the demo users and the demo agency's scope (P-13).
5. **The server, read-only:**
   - the running tag;
   - pending requests with and without an instance;
   - the definitions' versions and published state;
   - the realm's users with `admin-logistics` or `hr-management-approver`.

Commit: `chore(wf045): baseline, pending custody and loss requests on the acceptance data and the server, definition versions, tenant source for a job, callers, policies and demo users`.

## P1 — The loop in AdminLogistics (plan §1, P-3, P-4, P-5, P-6)

- **The model and its schema:**
  - `ApprovalStarting` and `Withdrawn`, at the end of both enums;
  - the fields, and `xmin` on `AssetLossReport`;
  - the `WF045_AssetApprovalLoop` migration;
  - `AssetApprovalRules`, with the transitions and the withdraw that sets `ReturnedAt`.
- **The start, the retry and the withdraw:**
  - `AssetApprovalStarter`;
  - `AssignToEmployee` and `MarkLost` save first, start, then save `PendingApproval` with the instance; both are `[Idempotent]`;
  - `RetryApprovalStart` and `WithdrawApproval`;
  - `AssetApprovalStartRetryJob` and `AssetApprovalMetrics`;
  - the settings under `AssetApproval` in the three AdminLogistics settings files, and `IntervalSeconds=15` in the acceptance profile.
- **The UI:** `Details.cshtml` gets the status bar, the two buttons, and the corrected «عرض الاعتماد» link (to the page of P3).
- **Tests:**
  - `Wf045AssetApprovalRulesTests`;
  - `Wf045NoPendingWithoutInstanceArchitectureTests`;
  - the starter's request shape (key, exclusions, subject, scope, tenant, title without national id);
  - the job's unit tests.

Commit: `feat(wf045): custody and loss requests start their approval after saving, wait visibly and restart by themselves when HR.Core is down, and can be withdrawn; no pending request without an instance`.

## P2 — The chain, enforced (plan §2, P-7, P-9)

- The definitions' new versions with D-WF-2's chain. The custody seeding method checks for v1, and retires it if published.
- `WorkflowGuardrails` for both types.
- The policies (`WorkflowStepAction`, `HrCoreWorkInbox`), and `ExternalApprovalGate` in `CreateGates`.
- The exemption lifted for `AssetLossReport` only.
- **Tests:**
  - `Wf045ExternalApprovalGateTests`: scope, requester, subject, distinct approvers, and a branch-level role outside the unit;
  - the policy and gateway tests, with the 403 on `/ui/hr-core/Employees`;
  - the exemption consistency tests, updated.

Commit: `feat(wf045): custody and loss approvals follow D-WF-2, admin-logistics then hr-management-approver by different people, with role enforcement and no exemption for loss reports`.

## P3 — The generic approval page and the inbox (plan §3, P-8)

- `WorkflowApprovalsController` and `Views/WorkflowApprovals/Details.cshtml`.
- The gateway rule, and the module filter's exception.
- The inbox for external types: `SupportedEntityTypes`, the context from the instance, `WorkInboxAudience`, `WorkflowDeepLinkResolver` and `WorkInboxItemMapper`.
- In AdminLogistics, the asset page's link, and a «صندوق الاعتمادات» entry in `_Layout.cshtml` for `admin-logistics` holders.
- **Tests:**
  - `Wf045WorkflowApprovalsControllerTests`: visibility, policies, antiforgery, encoded facts, a non-external type refused;
  - `GatewayPathAuthorizationMiddlewareTests` and the module filter tests, updated.

Commit: `feat(wf045): a generic approval page in HR.Core and external types in the work inbox; the asset page links to it`.

## P4 — AC-11 and AC-33, the repair script, PostgreSQL (plan §4, §5, P-4, P-6, P-10)

1. **AC-11 and AC-33 are rewritten** as the plan's §4 says:
   - **AC-11:** a real assignment by `a-logistics-maker`, step 1 by `a-logistics-checker`, step 2 by `a-management-approver`, then `Active`.
   - **AC-33:** no 403 accepted; a privileged loss goes through both steps, the asset ends `Lost`, and the settlement is recorded.
2. **`scripts/wf045-repair-asset-approvals.ps1`** (P-10).
3. **`Wf045AssetApprovalPostgresTests`** (Testcontainers; P-2):
   - HR.Core: approving the last step of a custody instance writes the v2 event's `OutboxMessage` row in the same transaction;
   - AdminLogistics: `xmin` on `AssetLossReport` (a withdraw racing a consumer: one wins);
   - the unique index keeps one open assignment, even with `ApprovalStarting`;
   - the retry job against a fake HR.Core: a failure, then a success, gives `PendingApproval` with one instance (the same key);
   - the job under forced row security in two tenants (P-4);
   - the withdraw racing the last approval (P-6).

   Then the plan's integration filters.

Commit: `test(wf045): AC-11 and AC-33 approve for real; the repair script for pending requests without an instance; the loop, the job under forced row security and the withdraw race proved on PostgreSQL`.

## P5 — Acceptance, then the independent review (plan Acceptance, P-11, P-12)

1. **Acceptance.** Start the stack (`acceptance-up.ps1 -Profile ProdLike`) and record the time to ready.
   - Run the repair script dry, then `-Apply`, on the acceptance data.
   - Run **`AcWf45AssetApprovalLoopTests`**: AC-WF-45a..h, positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   - Then the P-12 list through the job script with `-Classes`, per profile. All required classes must Pass.

   `down -v`. Restore the committed evidence.
2. **`ci-scope.json` and `ac-ownership.json`** (P-11).
3. **Independent review** (one fresh subagent), before the server, so the single build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - a path that saves `PendingApproval` without an instance;
   - a retry that makes a second instance, or starts in the wrong tenant;
   - a job that sees nothing under forced row security;
   - a withdraw that a late outcome can undo, or that ends in a 500;
   - `admin-logistics` reaching anything in HR.Core beyond the inbox and the page;
   - a decision on the page wider than the API;
   - a requester or one person approving both steps;
   - an unencoded fact, or an open redirect through `DetailsUrl`;
   - something the previous tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(wf045): AC-WF-45a..h pass on the prod-like stack; AC-11, AC-33 and the PLAN-40 to PLAN-44 suites still green; review fixes; CI scope and ownership updated`.

## P6 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to the same context after yours, remove yours and add them again on top. Rebuild, and rerun the unit and integration tests, and the acceptance classes that the merge touched. Read what the merged plans change for you, and record it.

Commit (only if the merge needed fixes): `fix(wf045): main merged, conflicts resolved, migrations on top`.

## P7 — The demo server (P-13)

Record every server change and its undo command **as you make it**.

1. Check P-13's condition.
2. Take the backup, then **verify** it.
3. **Build and deploy.** Run the build once, detached, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`:
   - `hrm-bootstrap` applies `WF045_AssetApprovalLoop` as `*_migrator`;
   - `keycloak-reconcile` brings the realm in line;
   - if the deploy stops at `keycloak-reconcile` after PostgreSQL restarts, run the same command again (PLAN-40 incident).

   Record the downtime and the time to all healthy.
4. **The demo users** of P-13, and proof that both log in.
5. **Then check**, with evidence in `artifacts/plan45/vps/` (summarised in the final report, never in `acceptance-evidence/`):
   1. The migration applied. The definitions show the new versions published and the old ones retired. `AssetLossReport` is no longer exempt in the running configuration.
   2. The repair script, dry, then `-Apply`. The job picks the requests up; record each item before and after.
   3. **A real custody approval on the demo:** the logistics requester assigns a privileged asset, the logistics checker approves step 1 on the generic page reached from the inbox, and `e2e-checker-mgmt` approves step 2. The assignment ends `Active`.
   4. **A loss the same way.** The asset ends `Lost`.
   5. **A withdraw.** The asset is free.
   6. The requester cannot approve step 1. The logistics checker gets 403 on `/ui/hr-core/Employees`.
   7. The retry job's log line and metrics are present.
   8. `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green.
   9. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass, with the PLAN-38..44 server checks (`AcWf43VpsChecks`, PLAN-44's).
6. **If anything fails and cannot be fixed in the turn:** roll back (P-13), prove the demo works, and record it.

Commit: `test(wf045): demo server on the custody approval loop (migration by the bootstrap, new definition versions, demo logistics users, pending requests repaired), PLAN-36..44 server checks still green`.

## P8 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `FUN_005_*`: the custody journey;
   - `WF_002_WORK_INBOX.md`: external types and the generic page;
   - `SOD_MATRIX.md`: the custody chain;
   - `OPS_VPS_SINGLE_HOST.md`: the backup location, the change table, the repair script and the retry job;
   - the «الحالة» lines for G-WF-02 (WF study) and G-IDA-02 (FUN-005 study);
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local CI, then the pre-push check, before any push** (P-2 for Docker): `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD, then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`. Both must be green.
3. **`reports/plan45-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations.
   4. The inventories: pending requests (acceptance and server, before and after), definition versions, the tenant source, policies.
   5. The guards and their own negative tests.
   6. Unit, integration and acceptance results; the PR run's artifact link as the evidence of record.
   7. The server: before and after, changes and their undo, the backup location, the repair items, **the demo users for the owner's QA guide** (usernames only).
   8. Deferred items.
   9. Defects for later plans.
   10. **Timing:**
       - total wall-clock;
       - active time against waiting for CI;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 45`.
   11. **A readiness note** for PLAN-46: what it reuses (the generic page, `WorkInboxAudience`, `ExternalApprovalGate`, the starter and retry pattern, the exemption mechanism for its four types) and the files it shares with this plan.
4. **Merge (P-14), by you:**
   1. Merge the feature branch into a new `release/plan-45` from `main` (`--no-ff`).
   2. Write `reports/plan45-pr.md` (short).
   3. `git push -u origin release/plan-45`
   4. `gh pr create --base main --head release/plan-45 --title "PLAN-45: the custody and loss approval loop" --body-file reports/plan45-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`. Record the PR link, each check result, the merge commit and the post-merge run in `reports/plan45-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`. That note rides with the next plan's first commit; **do not open a second PR for it.**

## Definition of done

- **No pending request without an instance** in AdminLogistics. A failure in HR.Core is handled by a visible automatic and manual restart, in the right tenant.
- **Withdraw** frees the asset and cancels the instance. A late outcome changes nothing, and a race ends in the conflict message.
- **Custody and loss** are in the work inbox and on the generic approval page, and the asset page's link works.
- **The chain** is `admin-logistics` then `hr-management-approver`, by different people. The role is enforced for both types, and `AssetLossReport` has no exemption.
- **The tests:** AC-WF-45a..h = Pass (the PR run's artifact is the evidence of record). AC-11 and AC-33 test the approval for real, and the PLAN-25 and PLAN-36..44 suites are green.
- **The server:** runs this code, and the demo has users who can walk the custody chain.
- **The documents:** the status lines, the custody journey, the inbox document and the SoD matrix are updated.
- **The merge:** done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed;
- what changed on the demo server, with the new demo usernames (no password);
- how long the plan took, and how long it waited for CI;
- the PR link;
- the path of the final report.
