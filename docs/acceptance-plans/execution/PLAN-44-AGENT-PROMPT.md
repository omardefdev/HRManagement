# Task: PLAN-44 — Workflow instance integrity under concurrency and failure

The deliverables:
- **No lost or doubled decision.** Two concurrent approvals of one instance: one wins, the other gets a clear Arabic conflict message, and nothing is written twice (`xmin` on `WorkflowInstance`).
- **One running instance per entity.** Two concurrent submissions of one entity end with one running instance (a partial unique index), and so do two concurrent external starts with one idempotency key (PLAN-43's deferred unique index).
- **A decision and its effect commit together.** The instance, the signature, the entity's change and the outbox row are saved in one transaction, or nothing is.
- **What slipped before this plan is found and repaired.** A reconciliation job detects «finished instance, entity still pending» and reports it with a metric; repair goes through the existing operator endpoint, dry run first.
- **One definition of the maker.** The inbox and the server bar the same people; the legacy approval path checks the step's role; only the requester cancels; dead code is removed.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order.

**There are no owner actions in this plan.** Every decision is taken (see «Decisions»).

**This session may start before PLAN-43 is merged** (P-1). It then either works early in a second clone, or prepares read-only in the primary clone and waits.

**Standing owner instruction on merges** (PLAN-38 to PLAN-43): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-12). **This plan's own PR only:** PLAN-43 and PLAN-66 are finished by their own sessions.

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan44-progress.md`** — your checkpoint. It contains:
   - the mode, the clone, and the base commit;
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - lock acquisitions and releases;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan44-progress.md` (in waiting mode: `artifacts/plan44/progress-draft.md`) and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check:
- the state of PLAN-43 (`git fetch origin`; is it on `origin/main`?);
- the locks (`scripts/parallel/hrm-lock.ps1 -Action Status -Resource acceptance-stack`, and again with `-Resource demo-server`);
- the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create the progress file (in waiting mode, the draft). Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Record the time spent waiting: for a lock, for PLAN-43 (`WAITING-PEER:`), and for CI (`WAITING-CI:`).
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session **without ultracode**, at `xhigh`):
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only; root-causing separate failures.
- Run **one independent review** at the end. Use a refuter only for findings rated high.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan44/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.

**Waiting:**
- **For CI.** Wait with whatever mechanism this session allows: a background `gh run watch <id> --exit-status` if permitted, or the app's run monitor. If the session cannot wait, end the turn with a line that starts with **`WAITING-CI:`** and names the PR, the run id, its URL and the exact next step.
- **For PLAN-43.** When you cannot go further before it merges, end the turn with a line that starts with **`WAITING-PEER:`** and names what you wait for and the exact next step.
- On the owner's next message (even «اكمل»), check **once** (`gh pr checks <n>`, or `git fetch origin` and `origin/main`) and continue from that step.
- Never merge on a guess.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior .NET engineer for workflow engines and data integrity:
- approval engines (instances, steps, gates, makers and checkers), separation of duties;
- EF Core optimistic concurrency (`xmin`), PostgreSQL partial unique indexes, unique-violation handling, transactions;
- PostgreSQL row level security, and migrations that run as a non-superuser owner under forced row security;
- MassTransit and the outbox patterns of PLAN-40 (`OutboxUnitOfWork`, publish then one save);
- Docker Compose, PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev`). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..43 did.

**Where things stand** when this prompt was written (2026-11-06):
- **`main`** = `7480843e` (PLAN-42 merged).
- **PLAN-43** is in PR #38 (`origin/release/plan-43` = `5524e112`). Its session works in the **primary clone** `D:\my projects\HRManagement`: it waits for its checks, then merges and writes its merge note. Never touch its branch, PR or working tree.
- **PLAN-66** (a faster CI) may run beside you in a second clone. It changes `.github/workflows/ci.yml`, `scripts/ci/*` and `tests/acceptance/ci-scope.json`. If it merges first, your PR runs on its pipeline.
- **The parallel protocol** is `docs/PARALLEL_PLANS.md`, with the locks in `scripts/parallel/hrm-lock.ps1`. Two acceptance stacks do not fit on this machine, so the plans take turns on one stack.
- **From PLAN-43** (its final report, «Readiness note»):
  - PLAN-44 builds on `ExternalApprovalService` (its key lookup needs the unique index this plan adds), `WorkflowInstanceActionService`, and `WorkflowInstance`'s new columns. `xmin` is on `WorkflowDefinition` only.
  - PLAN-44 and PLAN-45 both touch `WorkflowInstanceActionService.cs`, `WorkflowInstance.cs`, HR.Core migrations, `WorkflowPackSeeder.cs`, `ci-scope.json` and `ac-ownership.json`.
- **Lessons from PLAN-43 for every plan:**
  - **Never trust a data change in a migration on a tenant table.** `WorkflowDefinitions` has forced row security, and `hr_core_migrator` carries no tenant, so PLAN-43's backfill UPDATE matched no row on the server. Testcontainers connects as a superuser and the acceptance stack seeds after migrating, so neither showed it. Only the server check did. This plan's de-duplication is exactly such a change (P-3).
  - A persona change needs `scripts/acceptance-reset.ps1` before the first start.
  - `main` is strict: a merge by another plan costs a full CI rerun.
  - A session that ends its turn does not watch its PR. Hence `WAITING-CI:`.
- **The local testing rule** (owner, 2026-11-06): run locally only the acceptance classes this plan touches and their direct regressions; the full suite runs once, in CI (P-10).
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools.
  - The acceptance services have no container health check: readiness means `/health/ready`.
  - Never prune Docker while another session builds or holds the stack lock.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-44-wf-instance-integrity.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report. Its line numbers date from 2026-10-30 and have moved since PLAN-40..43: find the code by its content.
2. `reports/plan43-final-report.md` (on `origin/release/plan-43` until PR #38 merges):
   - its deviations: two migrations, the start-up backfill, `WorkflowDefinitionRead`, `WorkflowExternalInstancesController`, the exemptions;
   - §9 (deferred, including the unique key index);
   - §12 (readiness note).
3. `reports/plan40-final-report.md` §11 (the publisher contract) and `docs/EVENT_PUBLISHER_INVENTORY.md`.
4. `docs/PARALLEL_PLANS.md`, `docs/OPS_VPS_SINGLE_HOST.md` (build-on-host, deploy, rollback, smoke, backup and restore, the server change table).
5. `src/Shared/HRManagement.ServiceDefaults/TenantRlsMigrationHelper.cs`, `scripts/sql/ten002c-harden-tenant-rls.sql`, `deploy/bootstrap/ensure-roles.sql` and the `Ten002c*` tests: how forced row security and the roles are set up.
6. `docs/WF_001_WORKFLOW_ENGINE.md`, `docs/WF_002_WORK_INBOX.md`, `docs/SOD_MATRIX.md`, and G-WF-12 and G-WF-19 in `docs/WF_001_GAP_STUDY.md`.
7. `docs/OWNER_DECISIONS_REGISTER.md`: D-WF-8, the PLAN-44 part.
8. Every file the plan cites.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **Mode and clone (P-1).** Run `git fetch origin`. Is PLAN-43 on `origin/main` (its merge commit in `git log origin/main`, or `gh pr view 38 --json state`)?
   - **Merged** → **normal mode**. Use a clone no other session uses: the primary clone once PLAN-43's session has finished there (on `main`, clean tree, its merge note written), or the second clone.
   - **Not merged, and you are in a clone no other session uses** (for example `D:\my projects\HRManagement-b`, unless PLAN-66 runs there) → **early mode**.
   - **Not merged, and you are in the primary clone** → **waiting mode**.
2. **A second clone is set up** as `docs/PARALLEL_PLANS.md` says. Fix whatever is missing:
   - `git remote set-url origin https://github.com/omardefdev/HRManagement.git`;
   - the hooks: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scm/install-hooks.ps1`;
   - the owner's local `global.json` edit, copied from the primary clone and **never committed**;
   - any git-ignored local file a script needs that only the primary clone has: copy it without printing it, and never commit it.
3. **The base:**
   - **Normal mode:** `origin/main`, containing PLAN-43's merge.
   - **Early mode:** `origin/release/plan-43` (`5524e112` or later). Record the base SHA in the progress file; P6 needs it.
4. **The working tree is clean** (not checked in waiting mode), except for these, which may be present:
   - the owner's local `global.json` edit;
   - git-ignored `artifacts/`;
   - untracked prompt files under `docs/acceptance-plans/execution/`;
   - `tmp-vendors.html`.

   Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
5. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
6. **`gh auth status`** shows `omardefdev` with the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

Accepted by the owner on 2026-10-31 (wave-1 questionnaire, «accept the recommended» for every plan decision):

- **D-WF-8, the PLAN-44 part.**
  - The maker set is `CreatedBy` ∪ `InitiatedBy` ∪ `RequestedByUserId` (from PLAN-43). It is barred from **every** step in the gates for requisition, offer, establishment, competition and results.
  - The committee and the appeal keep their own rules.
  - The setting is `Workflow:Maker:IncludeSubmitter=true`.

**The plan's other defaults apply as written:**
- **Reconciliation is not automatic.** The periodic job detects, logs and updates the metric only (`Workflow:Reconciliation:AutoApply=false`). Repair goes through the existing operator endpoint (`repair-approvals`), dry run first.
- **Existing duplicates at migration.** The instance linked to the entity stays, otherwise the newest. The others become `Cancelled`, with the history entry «PLAN-44: نسخة مكررة». A report is taken before and after.
- **Moving `ApprovedCV` onto the engine is deferred** to wave 2 (WF-D). This plan only hardens the legacy path (§5).

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Modes.**<br>• **Normal:** branch `feature/plan-44-wf-instance-integrity` from `origin/main`, in a clone no other session uses.<br>• **Early** (a second clone, PLAN-43 unmerged): the same branch from `origin/release/plan-43`. P0 to P5 are allowed: code, unit and integration tests, and acceptance runs under the stack lock. **Not allowed:** any push, PR or GitHub change; any write to the demo server. When PLAN-43 is merged: `git fetch origin`, then `git rebase --onto origin/main <base> feature/plan-44-wf-instance-integrity`. Only your commits move, and nothing of yours was pushed.<br>• **Waiting** (the primary clone, PLAN-43 unmerged): PLAN-43's session still owns the working tree. **Do not switch branches, and do not create or change any tracked or untracked file outside `artifacts/plan44/`** (git-ignored). Do P0's read-only work: read the plan and the code, and write the inventories and `artifacts/plan44/progress-draft.md`. Then end the turn with `WAITING-PEER:`. On the next message, once PLAN-43 is merged and the clone is on a clean `main`, continue in normal mode: move the draft into `reports/plan44-progress.md`. |
| P-2 | **Sharing the machine.**<br>• **The acceptance stack:** take `acceptance-stack` with `hrm-lock.ps1` before `acceptance-up.ps1`; renew it during long runs; release it only after `down -v`. Never use `-NoBuild` after re-acquiring it.<br>• **Docker-heavy runs** (`ci-local.ps1`, Testcontainers suites): start one only when `docker ps` shows no Testcontainers (`label=org.testcontainers=true`) or `hrm-acceptance` containers that you did not start.<br>• **The demo server:** take `demo-server` for every server write.<br>• **Never** prune Docker images, volumes or build cache, or bring down a stack you did not start, while another session may be using them.<br>• Record every acquisition, release, wait and takeover in the progress file. |
| P-3 | **Migrations under forced row security** (PLAN-43's lesson). `WorkflowInstances` is a tenant table under forced row security, and `hr_core_migrator` is `NOBYPASSRLS` with no tenant.<br>• **Name:** `WF044_WorkflowInstanceIntegrity`, or two migrations with that prefix (record it).<br>• **Find out first**, read-only, on the acceptance stack and on the server: who owns `WorkflowInstances` (`pg_tables.tableowner`); whether row security is enabled and forced (`pg_class.relrowsecurity`, `relforcerowsecurity`); how many duplicates exist.<br>• **The de-duplication must really run as the migrator.** If the migrator owns the table: in the migration's own transaction, `ALTER TABLE "WorkflowInstances" NO FORCE ROW LEVEL SECURITY`, de-duplicate, create the indexes, then `ALTER TABLE "WorkflowInstances" FORCE ROW LEVEL SECURITY`. Nothing outside the transaction ever sees the table unforced. If a guard test forbids `NO FORCE`, extend it narrowly: the same migration must restore `FORCE`, and the test checks the pair. If the migrator does not own it, find another safe path (for example per tenant, with `app.current_tenant` set from a table that is not under row security), or **Stop**.<br>• **Prove it the way the server runs it.** An integration test that does not trust the superuser:<br>&nbsp;&nbsp;1. creates a non-superuser owner role;<br>&nbsp;&nbsp;2. forces row security;<br>&nbsp;&nbsp;3. seeds duplicates in **two tenants**, linked and unlinked, with and without decisions;<br>&nbsp;&nbsp;4. applies the migration as that role;<br>&nbsp;&nbsp;5. checks the result, and that `FORCE` is back.<br>• **The indexes:**<br>&nbsp;&nbsp;– `IX_WorkflowInstances_Tenant_Entity_Active`, unique on `(TenantId, EntityType, EntityId)` with the filter `"Status" = 'InProgress'`;<br>&nbsp;&nbsp;– the key index PLAN-43 deferred: unique on `(TenantId, SourceClientId, IdempotencyKey)` where `IdempotencyKey` is not null. Check for duplicates of that key first; there should be none.<br>• **Rollback safety:** the previous tag must keep running beside the new schema. `xmin` needs no column. With the new index, two concurrent duplicate inserts of the old code fail one of them with a unique violation; record that this is the only effect.<br>• Run `has-pending-model-changes` with the repository's tool manifest (`dotnet tool restore`), and check that `scripts/build-ef-bundles.sh` picks the migrations up.<br>• In early mode: if PLAN-43 adds HR.Core migrations after your base, remove yours and add them again on top after the move (P6). |
| P-4 | **The transaction and the outbox (the PLAN-40 contract).**<br>• `ApproveAsync` and `RejectAsync` already run inside `OutboxUnitOfWork.ExecuteAsync`. **Do not build a second wrapper.** Prove that everything is inside it: the instance save, the signature, the handler's entity change, and the publish before the last save. Add the conflict handling of §1.<br>• `ExternalApprovalService.CancelAsync`, and any other path that changes an instance's status, get the same conflict handling.<br>• Prove it with a fault-injection integration test: a failing handler leaves no `Completed` instance, no signature and no `OutboxMessage` row.<br>• `OutboxPublishOrderingArchitectureTests` stays green. If an event's shape changes, update `docs/EVENT_PUBLISHER_INVENTORY.md` in the same commit. |
| P-5 | **The maker rule and existing scenarios** (plan 0.6). Inventory `AcHcm25*`, `AcEst24*` and every other acceptance or unit scenario in which a submitter who is not the creator acts on step 1, or a requester acts on any step.<br>• **Do not suspend the rule** with `Workflow:Maker:IncludeSubmitter`.<br>• Where a scenario's subject is not the maker rule itself, give the step to another persona of the same role and record each change.<br>• **AC-HCM-25's assertions do not change.** `Hcm025PanelServerParityTests` gains the plan's new cases, and its existing cases stay as they are.<br>• If an AC-HCM-25 scenario cannot stay green without suspending the rule: **Stop**. |
| P-6 | **Reconciliation.**<br>• `WorkflowOutcomeReconciliationJob` runs every `Workflow:Reconciliation:IntervalMinutes` (15), calls `ApprovalRepairService.RunAsync(dryRun: !AutoApply)` for every tenant, updates the metrics, and logs a warning for each item. Register it beside the other hosted jobs in `Program.cs`.<br>• The repair applies only an outcome that slipped. It never creates or changes an instance, and it records the operator as the actor.<br>• **External types are not repaired here:** their effect is an outbox event inside the same transaction, and their owners repair them (PLAN-45, PLAN-46). |
| P-7 | **The inbox.**<br>• `IsAwaitingApproval` requires `entity.WorkflowInstanceId == instance.Id` for every type that carries the field.<br>• The committee and the appeal use the match each of them has.<br>• `EntityCreatedBy` becomes `EntityMakers`, built by the same `WorkflowMakerRules.Makers` as the gates. The panel and the server must judge alike. |
| P-8 | **The legacy path, and the dead code.**<br>• **Role check:** `ApprovalWorkflowService.TryApproveStep` and `TryReject` receive the actor's roles. The current step's `RoleRequired` must be among them, or `admin` while `Workflow:AdminBypass=true`.<br>• **Cancel:** only the requester (`RequestedBy`) or `admin`.<br>• **Delete:** `CreateViaEngineOrLegacy`, `AssetAssignmentWorkflowSeeder` and `AssetLossWorkflowSeeder`, with their tests. Confirm first that nothing calls them; `WorkflowPackSeeder` already holds both custody definitions.<br>• **Guard:** an architecture test refuses any definition seeding outside `WorkflowPackSeeder`. |
| P-9 | **Acceptance placement (PLAN-41 rules).**<br>• `AcWf44InstanceIntegrityTests` goes in `prodLike[]`, `required: true`.<br>• It seeds anomalous rows with SQL (c, e). Put it in an isolated group, or have it remove every row it seeded, so that no other class sees them. Record which.<br>• `ac-ownership.json` gets `AC-WF-44` (owner PLAN-44, `provesClaim: true`).<br>• Copy what the report needs into `artifacts/plan44/`, then restore the committed evidence: local evidence is never committed. |
| P-10 | **Local testing (owner's rule of 2026-11-06).** Locally, run only the acceptance classes this plan touches and their direct regressions, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`. **The full suite runs once, in CI.** The list, at least:<br>• **prod-like:** `AcWf44InstanceIntegrityTests`, `AcHcm25ProductionApprovalTests`, `AcWf43DefinitionGovernanceTests`, `AcMsg40AtomicPublishingTests`;<br>• **lab:** `AcEst24EstablishmentWorkflowUiTests`, `AcWf23WorkInboxUiTests`, `AcHcm21RequisitionApprovalUiTests`, `AcHcm22RecruitmentWorkflowsUiTests`, `AcHcm28CommitteeAppealTests`, `AcHcm31CompetitionTests`, `Ac11PolicyWorkflowTests`;<br>• and any class P0 finds that approves, rejects, cancels or reads the inbox.<br>Known local-only failures of lab classes that need PowerShell 7 (PLAN-43 §6) are recorded, not chased. |
| P-11 | **PowerShell 5.1.**<br>• Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block.<br>• Start the prod-like stack with `scripts/acceptance-up.ps1 -Profile ProdLike`, which prepares the realm, instead of the raw compose command.<br>• Build local images one at a time. |
| P-12 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P8, after local CI and the pre-push check are green, and only with PLAN-43 on `main`.<br>2. `gh pr create`; wait («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, push that branch only, and wait again.<br>5. `main` is strict. If another plan (PLAN-66, PLAN-45, PLAN-47) merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-13 | **The demo server.**<br>• **When:** only with PLAN-43 on `origin/main` **and** the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`). Never deploy over, or roll back to, an unmerged tag of another plan.<br>• **Lock:** hold `demo-server` for the whole server phase.<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan44-<utc>/` (700/600), verified as before (all 16 `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Before deploying,** run the duplicates report read-only, with a role that sees every tenant.<br>• **Build once,** on the final code after P6 (`scripts/vps/build-on-host.ps1`, then `scripts/vps/deploy.ps1 -Tag <sha12>`).<br>• **Reconciliation on the server:** run the dry run through the operator endpoint and record every item. Apply it only to items of the plan's second category (a finished instance linked to an entity still pending). Record each item before and after, with the operator account as the actor.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn: `scripts/vps/deploy.ps1 -Rollback` to PLAN-43's tag, safe by P-3. Restore the backup only if data was damaged, prove the demo works, and record the downtime. |
| P-14 | **No regression of PLAN-25 and PLAN-36..43.** These stay green:<br>• AC-HCM-25a..h (P-5), and `Hcm025PanelServerParityTests` (extended);<br>• `AcWf43DefinitionGovernanceTests`, `AcS2s42TrustBoundaryTests`, `AcMsg40AtomicPublishingTests`, AC-EST-24;<br>• `Data001ConcurrencyArchitectureTests` (with the new line), `OutboxPublishOrderingArchitectureTests`, the `Ten002c` row-security tests, `ProductionWiringMatrixTests`, `S2s042InternalRoutesAreServiceOnlyTests`;<br>• `scripts/wf043-validate.ps1`, `scripts/hcm025-validate.ps1`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan):
- **Wave 2:**
  - moving `ApprovedCV` onto the engine and retiring the legacy path (WF-D);
  - the stored unified inbox (WF-G).
- **PLAN-40:** publishing after the last save, and its architecture guard (G-WF-03). Done; this plan builds on it.
- **PLAN-45:** the local-first custody start and its retry, and the orphan instances from `AssetsController`.
- **PLAN-46 and PLAN-60:** the owner-side reconciliation of cross-service types (separation, card, discipline).
- **PLAN-50 and wave 2 (WF-F):** delegation, deadlines and escalation.

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- another plan's untracked prompt file;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- any secret, token or password (templates keep their placeholders, `__SVC_SECRET__` or `${…}`).

**Never:**
- push to `main`, force-push, or push anything before PLAN-43 is on `main`;
- merge with a pending or red check;
- touch another session's clone, branch, PR, stack or lock;
- disable host-key checking;
- leave `WorkflowInstances` without forced row security, even for a moment outside the migration's own transaction;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, mark a class not required, or suspend the maker rule to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails.**
- **The de-duplication cannot run correctly** as the migrator under forced row security, and no safe per-tenant path exists (P-3).
- **The data holds a group with no linked instance in which an unlinked instance carries a decision.** The plan refuses to choose there. Report the list (acceptance or server) and stop before migrating that database.
- **An AC-HCM-25 scenario** cannot stay green without suspending the maker rule (P-5).
- **The move onto `main` conflicts** in a way that would change PLAN-43's merged behaviour.
- **A server deploy fails,** and neither `-Rollback` nor the restore brings the demo back.

---

## P0 — Timing, mode, preconditions, baseline, inventories

1. Write the `Started (UTC)` line (see «Timing»). Decide the mode and check the Preconditions. In waiting mode, do only steps 3 to 5 read-only, into `artifacts/plan44/`, then end the turn with `WAITING-PEER:`.
2. `git switch -c feature/plan-44-wf-instance-integrity <base>`. Commit this prompt file.
3. **The plan's baseline** («المرحلة 0») into `reports/plan44-baseline.md`:
   - **0.2:** PLAN-43's columns (`RequestedByUserId`, `SourceClientId`, `IdempotencyKey`) and `WorkflowSodRules` exist.
   - **0.3:** the build.
   - **0.4:** the listed filters. Integration tests follow P-2.
   - **0.5:** the duplicates and the «finished instance, entity still pending» rows. Take them from the acceptance database when you next hold the stack lock (P5), and read-only from the server now.
   - **0.6:** the scenario inventory of P-5.
4. **Inventories:**
   - the owner and the row security of `WorkflowInstances` (P-3);
   - every path that changes an instance's status (`ApproveAsync`, `RejectAsync`, `ExternalApprovalService.CancelAsync`, the coordinator's start, and any other), with its callers;
   - every gate that names a maker, and the inbox's five cases;
   - every caller of the code §5 deletes;
   - every acceptance class that approves, rejects, cancels or reads the inbox (P-10).
5. **The server, read-only:**
   - the running tag;
   - the duplicates report;
   - instances by type and status;
   - finished instances whose entity is still pending;
   - the table's owner and its row security flags.

Commit: `chore(wf044): baseline, duplicates and stuck outcomes on the acceptance data and the server, row security of WorkflowInstances, callers, maker scenarios`.

## P1 — The concurrency token (plan §1)

- `entity.ConfigureOptimisticConcurrency(this)` on `WorkflowInstance`, and the `Data001ConcurrencyArchitectureTests` line.
- `WorkflowConcurrency.ConflictMessage` («تغيّرت حالة الطلب أثناء الإجراء؛ أعد تحميل الصفحة وحاول مجدداً»).
- `DbUpdateConcurrencyException` becomes that result in `ApproveAsync`, `RejectAsync`, `ExternalApprovalService.CancelAsync`, and every other path P0 found that changes an instance's status. The transaction rolls back, including the signature (P-4). There is no automatic retry.
- **Tests:** the unit tests for the result mapping. The PostgreSQL proof is in P5.

Commit: `feat(wf044): workflow instances carry an xmin token; a concurrent decision gets a conflict message and writes nothing`.

## P2 — One running instance per entity, and per key (plan §2, P-3)

- **The model and its schema:**
  - the two unique partial indexes;
  - the migration, with the de-duplication of P-3 and the plan's rules 2.1 and 2.2;
  - `scripts/wf044-duplicate-instances-report.ps1` (read-only, PS 5.1).
- **The code:**
  - `ApprovalStartCoordinator` catches the unique violation (`23505`, by index name), re-reads the running instance and returns it if it can be reused (`FindReusableInstanceAsync`), otherwise «يوجد مسار جارٍ لهذا الطلب».
  - `ExternalApprovalService.StartAsync` does the same: the same key gets the same instance, and another key on a running entity gets 409.
  - The inbox linkage (P-7).
- **Tests:**
  - `Wf044InboxLinkageTests`;
  - the unit tests for the violation handling;
  - the migration's integration test of P-3.

Commit: `feat(wf044): at most one running instance per entity and per idempotency key, existing duplicates resolved under forced row security, the inbox shows only the linked instance`.

## P3 — The decision and its effect in one transaction, and reconciliation (plan §3, P-4, P-6)

- Prove, and complete where needed, that `ApproveAsync` and `RejectAsync` keep the instance, the signature, the handler and the outbox inside `OutboxUnitOfWork`.
- **`ApprovalRepairService`:** a second category, «finished instance, entity still pending», only where `WorkflowInstanceId == instance.Id`. The handler runs again; handlers check state and link, so it is idempotent.
- **`WorkflowOutcomeReconciliationJob`, the `WorkflowIntegrityMetrics`** (`workflow_outcome_unapplied_total{entity_type}`, `workflow_instance_conflict_total{action}`), the `Workflow:Reconciliation` settings, and the operator script's new category.
- **Tests:**
  - the repair's unit tests, including a finished instance that is not linked: it is listed as «غير مرتبطة» and never applied;
  - the fault-injection test (P-4), in P5.

Commit: `feat(wf044): a decision, its effect and its outbox row commit together; a reconciliation job finds outcomes that slipped before, repaired through the operator endpoint after a dry run`.

## P4 — One maker rule, and the legacy path (plan §4, §5, P-5, P-7, P-8)

- **The maker rule:**
  - `WorkflowMakerRules.Makers` in `WorkflowSodRules.cs`;
  - the requisition, offer, establishment, competition and result gates bar the makers from every step;
  - the inbox uses the same function;
  - the scenario changes of P-5.
- **The legacy path:**
  - the role check and the requester-only cancel;
  - the deletions and their architecture guard.
- **Tests:**
  - `Wf044MakerRulesTests` (every type × creator, submitter, requester);
  - `Wf044LegacyApprovalRoleTests`;
  - `Hcm025PanelServerParityTests`, extended: an empty `CreatedBy`, and a submitter who is not the creator, for every type.

Commit: `feat(wf044): the gates and the inbox bar the same makers from every step; the legacy approval path checks the step role and only the requester cancels; dead seeders and the bridge removed`.

## P5 — PostgreSQL, then acceptance (plan Verification and Acceptance, P-2, P-9, P-10)

1. **`Wf044InstanceIntegrityPostgresTests`** (Testcontainers, `[Collection("IntegrationSerial")]`), when Docker is free (P-2):
   - two contexts approve one instance: one wins, the other gets the conflict message;
   - the partial index: a second running instance is refused, and accepted once the first is cancelled; two parallel submissions through the coordinator give one instance; the same for one external key;
   - a failing handler leaves no `Completed` instance, no signature and no `OutboxMessage` row;
   - the migration as a non-superuser owner under forced row security (P-3).

   Then the plan's integration filters.
2. **Acceptance:** take the `acceptance-stack` lock. Start the stack (`acceptance-up.ps1 -Profile ProdLike`) and record the time to ready.
   - Run `scripts/wf044-duplicate-instances-report.ps1` before and after the stack migrates.
   - Then **`AcWf44InstanceIntegrityTests`** (AC-WF-44a..e, positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`).
   - Then the P-10 regression list through the job script with `-Classes`, per profile. All required classes must Pass.
3. `down -v`, then release the lock. Restore the committed evidence.
4. Update `ci-scope.json` and `ac-ownership.json` (P-9).

Commit: `test(wf044): AC-WF-44a..e pass on the prod-like stack; concurrency, the indexes, the transaction and the migration under forced row security proved on PostgreSQL; AC-HCM-25, AC-EST-24 and the PLAN-40 to PLAN-43 suites still green`.

## P6 — Onto `main` (P-1; normal mode: merge `origin/main` only if it moved)

1. **Early mode:** if PLAN-43 is not on `origin/main`, write `WAITING-PEER:` and end the turn. On the next message, `git fetch origin` and check again.
2. **When it is merged:** `git rebase --onto origin/main <base> feature/plan-44-wf-instance-integrity`.
   - Resolve conflicts as the protocol says: keep both sides in the shared list files, and redo the P-3 migration step if needed.
   - Rebuild, and run P1–P5's unit and integration tests again.
   - Rerun the acceptance classes whose code or configuration the conflicts touched, under the lock.
3. Read PLAN-43's merge note and anything else on `main` that changes your work (PLAN-66 or PLAN-45 if merged), and record it.

Commit (only if the move needed fixes): `fix(wf044): rebased onto main after PLAN-43, conflicts resolved, migrations on top`.

## P7 — The demo server (P-13)

Record every server change and its undo command **as you make it**.

1. Take the `demo-server` lock. Check P-13's condition.
2. Take the backup, then **verify** it. Run the duplicates report.
3. Run `scripts/vps/build-on-host.ps1` once, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`. `hrm-bootstrap` applies the `WF044_*` migrations as `*_migrator`. If the deploy stops at `keycloak-reconcile` after PostgreSQL restarts, run the same command again (PLAN-40 incident).

   Record the downtime and the time to all healthy.
4. **Then check**, with evidence in `artifacts/plan44/vps/` (summarised in the final report, never in `acceptance-evidence/`):
   1. The migration applied: the two indexes exist, and `WorkflowInstances` is **still forced** under row security.
   2. The duplicates report shows zero, and the before-picture's duplicates are resolved as the rules say.
   3. The reconciliation job runs: its log line and the metric are present. The operator dry run is recorded and applied as P-13 says.
   4. A short demo tour still works: a recruitment approval by `e2e-checker-mgmt`, and a leave approval.
   5. Two parallel approvals of one step on the server end with one decision and one conflict message.
   6. `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green.
   7. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass, with the PLAN-38..43 server checks. Most of those are partial files of `AcOps36VpsBootTests`, and PLAN-43 added `AcWf43VpsChecks`.
5. Release the lock.
6. **If anything fails and cannot be fixed in the turn:** roll back (P-13), prove the demo works, and record it.

Commit: `test(wf044): demo server on instance integrity (migrations by the bootstrap under forced row security, duplicates resolved, reconciliation running), PLAN-36..43 server checks still green`.

## P8 — Docs, review, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/WF_001_WORKFLOW_ENGINE.md`: concurrency, the single running instance, the transaction, reconciliation;
   - `docs/WF_002_WORK_INBOX.md`: the linkage condition;
   - `docs/SOD_MATRIX.md`: the maker definition;
   - `DATA_001`'s list of protected entities, if it exists: add `WorkflowInstance`;
   - `OPS_VPS_SINGLE_HOST.md`: the backup location, the change table, and how to run the duplicates report and the repair;
   - the «الحالة» lines for G-WF-12 and G-WF-19;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Independent review** (one fresh subagent). It reads this prompt, the plan and the diff, and hunts for:
   - a decision path that writes twice, or keeps a signature after a rollback;
   - a second running instance reachable through any start;
   - a finished instance whose effect can be lost;
   - a migration that leaves row security unforced, or behaves differently as the migrator than as a superuser;
   - a maker the panel and the server judge differently;
   - a legacy step approved without its role;
   - a reconciliation that creates or changes an instance;
   - something the previous tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests.
3. **Local CI, then the pre-push check, before any push** (P-2 for Docker): `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD, then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`. Both must be green.
4. **`reports/plan44-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations.
   4. The inventories: duplicates and stuck outcomes (acceptance and server, before and after), row security, callers, maker scenarios.
   5. The guards and their own negative tests.
   6. Unit, integration and acceptance results; the PR run's artifact link as the evidence of record.
   7. The server: before and after, changes and their undo, the backup location, the repair items.
   8. The mode and any wait for PLAN-43.
   9. Deferred items.
   10. Defects for later plans.
   11. **Timing:**
       - total wall-clock;
       - active time against waiting (CI, peer, locks);
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 44`.
   12. **A readiness note** for PLAN-45 and PLAN-46: what they build on (the conflict message, the indexes, the repair categories, `WorkflowMakerRules`), and the files they share with this plan.
5. **Merge (P-12), by you:**
   1. Merge the feature branch into a new `release/plan-44` from `main` (`--no-ff`).
   2. Write `reports/plan44-pr.md` (short).
   3. `git push -u origin release/plan-44`
   4. `gh pr create --base main --head release/plan-44 --title "PLAN-44: workflow instance integrity under concurrency and failure" --body-file reports/plan44-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`. Record the PR link, each check result, the merge commit and the post-merge run in `reports/plan44-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`. That note rides with the next plan's first commit; **do not open a second PR for it.**

## Definition of done

- **Concurrency:** `WorkflowInstance` carries `xmin`. A conflicting decision returns the Arabic message and writes nothing.
- **One running instance:** no entity has two, and no key has two. The indexes exist, and the duplicates report is zero on the acceptance data and on the server.
- **One transaction:** a decision, its local effect and its outbox row commit together. Reconciliation finds what slipped before this plan; repair goes through the operator endpoint after a dry run.
- **Makers:** the gates and the inbox use the same `WorkflowMakerRules`. `Hcm025PanelServerParityTests` is extended and green, and AC-HCM-25's assertions are unchanged.
- **The legacy path:** it checks the step role, only the requester cancels, and the dead code is gone.
- **The tests:** AC-WF-44a..e = Pass (the PR run's artifact is the evidence of record), and AC-HCM-25, AC-EST-24 and the PLAN-40..43 suites are green.
- **The server:** runs this code with row security still forced, and the PLAN-36..43 server checks are green.
- **The documents:** the status lines, the engine and inbox documents, and the SoD matrix are updated.
- **The merge:** done by you, through a PR, after green checks and after PLAN-43.

Then reply with a short summary in Arabic:
- what changed;
- what changed on the demo server (duplicates resolved, items repaired);
- how long the plan took, and how long it waited for PLAN-43, for locks and for CI;
- the PR link;
- the path of the final report.
