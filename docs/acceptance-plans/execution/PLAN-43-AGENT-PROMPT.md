# Task: PLAN-43 — Workflow definition governance, a guarded engine API, and separation of duties for every workflow type

The deliverables:
- **Definitions are governed.** Only `workflow-admin` or `hr-admin` write and publish workflow definitions. Publishing needs a second person and a validator. A published version never changes, and a start respects the definition's validity window.
- **The public start API is for the owning service account only,** and records the real human requester. No service account approves or rejects a step.
- **Custody outcome events carry the workflow instance id,** and their consumers refuse an instance that is not linked.
- **Step roles are enforced by default for every type.** The requester, the excluded users and the subject employee cannot approve. A definition that requires distinct approvers stops one person from taking two steps.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order.

**There are no owner actions in this plan.** Every decision is taken (see «Decisions»).

**This session may start before PLAN-42 is merged** («early start», P-1). It then works in the **second clone** on top of PLAN-42's unmerged branch, and waits for PLAN-42's merge before anything leaves the machine.

**Standing owner instruction on merges** (PLAN-38 to PLAN-42): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-11). **This plan's own PR only:** PLAN-41 and PLAN-42 are finished by their own sessions (P-1).

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan43-progress.md`** — your checkpoint. It contains:
   - the mode (early or normal), the clone, and the base commit;
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - lock acquisitions and releases;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan43-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check:
- the state of PLAN-42 (`git fetch origin`; is it on `origin/main`?);
- the locks (`scripts/parallel/hrm-lock.ps1 -Action Status -Resource acceptance-stack`, and again with `-Resource demo-server`);
- the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan43-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Record the time spent waiting: for a lock, for PLAN-42 (`WAITING-PEER:`), and for CI (`WAITING-CI:`).
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session **without ultracode**, at `xhigh`):
- Use **at most two subagents at a time**. Good uses:
  - the P0 inventories (definitions, callers, entity types), read-only;
  - root-causing separate failures.
- Run **one independent review** at the end. Use a refuter only for findings rated high.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan43/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.

**Waiting:**
- **For CI** (PLAN-41 rule). One run of the checks takes about two hours. Wait with whatever mechanism this session allows: a background `gh run watch <id> --exit-status` if permitted, or the app's run monitor. If the session cannot wait, end the turn with a line that starts with **`WAITING-CI:`** and names the PR, the run id, its URL and the exact next step.
- **For PLAN-42** (early start). When your local work is done and PLAN-42 is not on `origin/main`, end the turn with a line that starts with **`WAITING-PEER:`** and names what you wait for and the exact next step.
- On the owner's next message (even «اكمل»), check **once** (`gh pr checks <n>`, or `git fetch origin` and `origin/main`) and continue from that step.
- Never merge on a guess.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior .NET engineer for workflow engines and authorization:
- approval engines (definitions, versions, instances, steps, gates), separation of duties;
- ASP.NET Core authorization policies, service accounts against interactive users, Keycloak roles;
- EF Core migrations and optimistic concurrency (`xmin`) on PostgreSQL;
- MassTransit with versioned contracts and the outbox patterns of PLAN-40;
- Docker Compose, PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev`). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..42 did.

**Where things stand** when this prompt was written (2026-11-05):
- **Three sessions share this machine:**
  - **PLAN-41** is being finished by its own session in the clone `D:/hrm-plan41-finish`: PR #36, the evidence of record, the merge. On 2026-11-05 the PR was still open: run 7 (41962709297, on `e6518e9`) was green, and the evidence commit and one more run remained.
  - **PLAN-42** runs in the primary clone `D:\my projects\HRManagement`, on `feature/plan-42-s2s-trust-boundary`. Its code is complete through its P6 and its review (head `6e56ded7` at the time of writing). It is **not pushed**. Its branch starts from PLAN-41 at `3021ea9a`, before PLAN-41's last fixes; those reach you only with the move onto `main` (P6), unless your base is a later commit of PLAN-42's branch. The owner told PLAN-42's session to merge PLAN-41's branch into its own (merge, never rebase), deploy to the demo server (its P7) and prepare its PR (its P8) while PLAN-41's last run goes. It pushes, opens its PR and merges only after PLAN-41 is merged.
  - **This plan** runs in a second clone, normally `D:\my projects\HRManagement-b`.
- **The parallel protocol** is `docs/PARALLEL_PLANS.md`, with the locks in `scripts/parallel/hrm-lock.ps1` (both from PLAN-42's branch). Two acceptance stacks do not fit (Docker has 15.48 GiB; one prod-like stack uses 6.63 GiB), so the plans take turns on one stack.
- **From PLAN-42** (on its branch):
  - the service-only internal enforce endpoint, which derives mode, actor and tenant on the server;
  - `PolicyWorkflowTriggerAllowlist`, which still lets `ASSET-ASSIGN-APPROVAL` start from enforce;
  - `ForwardActingUserHandler`, now actually registered;
  - gateway proof v2, under which service accounts pass without a proof;
  - `AcceptanceHarness.CreateServiceClientAsync` and `ACCEPTANCE_SVC_CLIENT_SECRET`.

  Its review left **M3** open: any `hr-core.workflow` token can start a workflow for any entity on `WorkflowInstancesController`. **This plan closes M3 for the registered types.**
- **CI (PLAN-41):**
  - the `Acceptance` job runs both profiles and is a required check;
  - every acceptance class must be listed in `tests/acceptance/ci-scope.json`, and every AC needs an entry in `tests/acceptance/ac-ownership.json`;
  - the evidence of record is the PR run's artifact, and local evidence is **never committed**.
- **The demo server** runs `3d0b2df84f82` (PLAN-40) until PLAN-42 deploys.
- **Practical notes from PLAN-42:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools.
  - The acceptance services have no container health check: readiness means `/health/ready`.
  - Disk fills with build cache: `docker builder prune` helps, but **never** while another session builds or holds the stack lock.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-43-wf-definition-governance-engine-api.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report. Its line numbers date from 2026-10-30 and may have moved; find the code by its content. Where it names `PoliciesController.Enforce`, read PLAN-42's `PolicyEnforceInternalController`.
2. `docs/PARALLEL_PLANS.md` (the protocol you follow).
3. PLAN-42's checkpoint and, when it exists, its final report (`reports/plan42-progress.md`, `reports/plan42-final-report.md`, in your base): the internal endpoint, the allow-list, the acting-user handler, the review's open items.
4. PLAN-41's final report (`reports/plan41-final-report.md` once merged; until then `reports/plan41-progress.md`): the scope file, the ownership file, the evidence contract, and the job's duration.
5. `reports/plan40-final-report.md` §11 (the publisher contract) and `docs/EVENT_PUBLISHER_INVENTORY.md`.
6. `docs/WF_001_WORKFLOW_ENGINE.md`, `docs/SOD_MATRIX.md`, and G-WF-04..06 in `docs/WF_001_GAP_STUDY.md`.
7. `docs/OPS_VPS_SINGLE_HOST.md`: build-on-host, deploy, rollback, smoke, backup and restore, the server change table.
8. `docs/OWNER_DECISIONS_REGISTER.md`: D-WF-4 and D-WF-8 are accepted.
9. Every file the plan cites.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **Mode and clone.** Run `git fetch origin`, then check whether PLAN-42 is on `origin/main` (`gh pr list --state merged --head release/plan-42`, or its merge commit in `git log origin/main`).
   - **Merged → normal mode:** branch from the updated `main` in the clone you are in, provided no other session uses that clone.
   - **Not merged → early mode (P-1):** you must be in a **second clone**, not in the primary clone `D:\my projects\HRManagement`, which PLAN-42's session owns. If you are in the primary clone, write `BLOCKED:` with the commands for the owner:
     ```
     git clone "D:\my projects\HRManagement" "D:\my projects\HRManagement-b"
     Copy-Item "D:\my projects\HRManagement\docs\acceptance-plans\execution\PLAN-43-AGENT-PROMPT.md" "D:\my projects\HRManagement-b\docs\acceptance-plans\execution\"
     ```
     Then they start this session in that folder.
2. **The second clone is set up** as `docs/PARALLEL_PLANS.md` says. Fix whatever is missing:
   - `git remote set-url origin https://github.com/omardefdev/HRManagement.git`, then `git remote add primary "D:/my projects/HRManagement"`;
   - the hooks: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scm/install-hooks.ps1`;
   - the owner's local `global.json` edit, copied from the primary clone and **never committed**;
   - any git-ignored local file a script needs that only the primary clone has: copy it without printing it, never commit it, and list it for the protocol doc (P8).
3. **The base:**
   - **Early mode:** `git fetch primary`. The base is the head of `primary/feature/plan-42-s2s-trust-boundary`. It must contain PLAN-42's P6 and its review fixes (`6e56ded7` or later). Record the base SHA in the progress file; P6 needs it.
   - **Normal mode:** `origin/main`.
4. **The working tree is clean** except for these, which may be present:
   - the owner's local `global.json` edit;
   - git-ignored `artifacts/`;
   - this prompt file.

   Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
5. **No other session uses this clone.** The PLAN-41 and PLAN-42 sessions work in their own clones:
   - never touch their working trees, branches or PRs;
   - never push their commits;
   - **do not finish PLAN-41 or PLAN-42 yourself** (P-1). This is a deviation from the standing instruction, because their sessions are alive.
6. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
7. **`gh auth status`** shows `omardefdev` with the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

Accepted by the owner on 2026-10-31 (wave-1 questionnaire):
- **D-WF-4:** a new role `workflow-admin`, and `hr-admin` too, write and publish definitions. In v1, only from the national tenant (`TenantDefaults.DefaultTenantId`). The publisher is neither the creator nor the one who submitted it. Per-agency customisation is wave 2 (WF-H).
- **D-WF-8:** for instances a service started, the human requester and `ExcludedApproverUserIds` are barred from every step, approving and rejecting, and so is the subject employee (`SubjectEmployeeId`). The recruitment and establishment rules stay in their gates.

**The plan's other defaults apply as written.** It keeps each one behind a setting or a tested table, so the owner can change it later without code:
- the guardrail table;
- `RequireDistinctApprovers`: `true` for new definitions and for `ASSET-ASSIGN-APPROVAL`, `false` for the existing national recruitment and establishment definitions;
- the temporary exemption `Workflow:StepRole:ExemptEntityTypes = ["AssetLossReport"]` until PLAN-45;
- the requester is derived on the server from `X-Acting-User`, accepted **only** from the type's owning service account, and never from the request body.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Early start.** While PLAN-42 is not on `origin/main`:<br>• **Allowed:** P0 to P5 on your own branch `feature/plan-43-wf-definition-governance-engine-api`, cut from the recorded base: code, unit and integration tests, and acceptance runs under the stack lock (P-2).<br>• **Not allowed:** any push, PR or GitHub change; any write to the demo server; any change in another clone; finishing PLAN-41 or PLAN-42.<br>• **The PLAN-42 follow-ups this plan specifies** (P-6) are yours. Keep them small, so the move onto `main` stays easy.<br>• **When PLAN-42 is merged (P6):** `git fetch origin`, then `git rebase --onto origin/main <base> feature/plan-43-wf-definition-governance-engine-api`. Only your own commits move; PLAN-42's old commits never enter your PR. The protocol says to merge `origin/main`. Rebase instead: your branch starts on PLAN-42's unpushed branch, which its session may still rewrite, and `--onto` works either way. It is safe because nothing of yours was pushed. Resolve conflicts as the protocol says.<br>• If PLAN-42 is still not merged when P5 is done: `WAITING-PEER:`. |
| P-2 | **Sharing the machine:**<br>• **The acceptance stack:** take `acceptance-stack` with `hrm-lock.ps1` before `acceptance-up.ps1`; renew it during long runs; release it only after `down -v`. Never use `-NoBuild` after re-acquiring it.<br>• **Docker-heavy runs** (`ci-local.ps1`, Testcontainers suites): start one only when `docker ps` shows no Testcontainers (`label=org.testcontainers=true`) or `hrm-acceptance` containers that you did not start. Otherwise do other work and retry. PLAN-41 measured how flaky those suites get when Docker is under load.<br>• **The demo server:** take `demo-server` for every server write.<br>• **PLAN-42 goes first:** it is on the critical path. Until its PR is open (`gh pr list --head release/plan-42 --state open`), its session may rebuild, deploy and run its local CI at any time. In that window, run nothing in Docker longer than about 30 minutes (no full acceptance job, no `ci-local.ps1`), and start a shorter run only when `docker ps` shows nothing of another session. A run already started may finish. Write code and unit tests instead. PLAN-42's session was told the same: it waits for your short runs and never stops them.<br>• **Never** prune Docker images, volumes or build cache, or bring down a stack you did not start, while another session may be using them.<br>• Record every acquisition, release, wait and takeover in the progress file. |
| P-3 | **Migrations.**<br>• Two migrations with the plan's prefix are allowed: `WF043_DefinitionGovernance` (P1) and `WF043_ExternalStart` (P2). The plan names one; record this as a deviation.<br>• **Rollback safety:** every new column is nullable, or has a database default, so the previous tag keeps running after a rollback. `xmin` concurrency needs no schema change.<br>• Backfill as the plan says.<br>• Run `has-pending-model-changes` with the repository's tool manifest (`dotnet tool restore`), and check that `scripts/build-ef-bundles.sh` picks the migrations up.<br>• After the move onto `main` (P6): if PLAN-42 added HR.Core migrations after your base, remove yours and add them again on top, as the protocol says. |
| P-4 | **Turning on step-role enforcement for every type is a behaviour change.**<br>• P0 lists every `DomainEntityType`: from the seeded definitions, the code that starts instances, and `WorkflowInstanceActionService.HandledEntityTypes`. For each one: its step roles, whether each role exists in the role catalog, and who can reach the approval endpoint for it today.<br>• A type other than `AssetLossReport` whose step role cannot reach approval before PLAN-45 or PLAN-46 is added to `ExemptEntityTypes`, with a reason, an owner plan, and a dated obligation in `docs/qa/dated-obligations.json`. Record it as a deviation.<br>• **Never exempt a type only because a test fails.** |
| P-5 | **Callers of the start API** (plan 0.6).<br>• `Admin.Logistics`'s `WorkflowTriggerService` is the only production caller expected.<br>• Tests and acceptance helpers that start instances with a user token (`EstablishmentChangeFlow.cs`, `AcMsg40AtomicPublishingTests`, `Ac11PolicyWorkflowTests`, and any other the inventory finds) move to one of two paths:<br>&nbsp;&nbsp;– a service identity (`AcceptanceHarness.CreateServiceClientAsync`) with `X-Acting-User`;<br>&nbsp;&nbsp;– a start through the coordinator.<br>• `Hcm025ReviewFixTests` proves 403 for a user.<br>• Another production caller: register it in `ExternalApprovalTypes` if it is the service that owns the type; a user-facing caller with no owning service is a **Stop**.<br>• Add the start call to `calls[]` in `deploy/wiring/service-wiring.json`, with `serverPolicy: WorkflowExternalStart`, so `ProductionWiringMatrixTests` stays green. |
| P-6 | **PLAN-42 follow-ups, as the plan specifies:**<br>• `PolicyWorkflowTriggerAllowlist` becomes empty. Keep the class and its check: an empty list means no workflow starts from enforce.<br>• The positive of AC-S2S-42c becomes «200, the `TriggerWorkflow` effect in the decision, and no instance».<br>• AC-11 checks the decision, then starts the instance through `Start` as `svc-admin-logistics` with `X-Acting-User`.<br>• Record that this closes PLAN-42's review item M3 for the registered types, and what remains for PLAN-51. |
| P-7 | **Custody events v2 and the PLAN-40 contract.**<br>• `WorkflowInstanceId` comes from `instance.Id`, never from `ContextJson`, and `SchemaVersion` is 2.<br>• The four consumers use `supportedMax: 2`. They apply an outcome only when the instance id equals the assignment's, take the asset from the assignment row, and otherwise log a warning and count it in `workflow_outcome_ignored_total`. A v1 message is ignored with a log.<br>• Update `EVENT_PUBLISHER_INVENTORY.md` in the same commit (R4).<br>• Publish with the PLAN-40 patterns, and keep `OutboxPublishOrderingArchitectureTests` green. |
| P-8 | **Personas and the realm.**<br>• `p-workflow-admin` and `p-workflow-admin-2` (`PLATFORM` tenant, role `workflow-admin`) go into `testdata/acceptance/personas.json` **and** `AcceptancePersonaCatalog` together. The counts are derived since PLAN-41.<br>• The role goes into `docker/keycloak/realm-manifest.json`. `scripts/keycloak/render-realm.ps1`, `Ops039RealmManifestParityTests` and `Ops039RenderRealmTests` stay green.<br>• On the demo server, `keycloak-reconcile` creates the role. No new demo user is needed: `hradmin` and `e2e-hr-admin` both hold `hr-admin`, so two-person publishing works there. |
| P-9 | **Acceptance placement (PLAN-41 rules).**<br>• `AcWf43DefinitionGovernanceTests` goes in `prodLike[]`, `required: true`. Use an isolated group only if it stops or recreates containers.<br>• `ac-ownership.json` gets `AC-WF-43` (owner PLAN-43, `provesClaim: true`). Update the AC-11 entry to what it now proves; its real UI journey stays with PLAN-45.<br>• Copy what the report needs into `artifacts/plan43/`, then restore the committed evidence: local evidence is never committed. |
| P-10 | **PowerShell 5.1.**<br>• Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block.<br>• Start the prod-like stack with `scripts/acceptance-up.ps1 -Profile ProdLike`, which prepares the realm, instead of the raw compose command.<br>• Build local images one at a time. |
| P-11 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P8, after local CI and the pre-push check are green, and only with PLAN-42 on `main`.<br>2. `gh pr create`; wait («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, push that branch only, and wait again.<br>5. If branch protection requires an up-to-date branch (for example after PLAN-47 merges), update it and wait again.<br>6. Record whether the post-merge dedupe guard skipped.<br>**The evidence of record is the PR run's artifact:** link it in the final report, and commit no evidence. Never push to `main` directly, never force-push. |
| P-12 | **The demo server.**<br>• Write to it only when PLAN-42 is on `origin/main` **and** the running tag (`/opt/hrm/current-tag`) is on `origin/main` (`git branch -r --contains <tag>`). Never deploy over, or roll back to, an unmerged tag of another plan.<br>• Hold `demo-server` for the whole server phase.<br>• Back up first with `pg_dumpall` under `/opt/hrm/backups/pre-plan43-<utc>/` (700/600), verified as before (all 16 `CREATE DATABASE` statements, `SHA256SUMS`).<br>• Build **once**, on the final code after P6 (`scripts/vps/build-on-host.ps1`, then `scripts/vps/deploy.ps1 -Tag <sha12>`).<br>• The demo stays usable at the end of every turn. If a step fails and cannot be fixed in the turn: `deploy.ps1 -Rollback` to PLAN-42's tag, safe by P-3. Restore the backup only if data was damaged, prove the demo works, and record the downtime. |
| P-13 | **No regression of PLAN-25 and PLAN-36..42.** These stay green:<br>• **AC-HCM-25a..h (`AcHcm25ProductionApprovalTests`) and `Hcm025PanelServerParityTests`, without modification** (plan §3, rule 3). If one needs a change to stay green, that is a regression to fix in code, not in the test.<br>• `AcS2s42TrustBoundaryTests` (with 42c as P-6 says), `AcMsg40AtomicPublishingTests`, `AcInt37ProductionWiringTests`, `AcOps38FirstBootTests`, both recruitment journeys, and every lab class that starts or acts on a workflow;<br>• `ProductionWiringMatrixTests`, `S2s042InternalRoutesAreServiceOnlyTests`, the `Ops039` realm tests, `OutboxPublishOrderingArchitectureTests`, `Scm001`, and the PLAN-41 scope guard and baseline validator;<br>• `scripts/vps/lint-compose.ps1` (both chains) and `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-45:** the trusted custody start, the approval page, the custody inbox, the D-WF-2 roles, and lifting the exemption.
- **PLAN-46:** the general cross-service contract (the new route, `IWorkflowApprovalClient`, `IWorkflowOutcomeDecided`) and the new types.
- **PLAN-44:** the concurrency token, the unique index, and an atomic outcome.
- **PLAN-42 and wave 2:** the `policies/enforce` endpoint and service trust (PLAN-42); managing `PolicyWorkflowBindings` (wave 2, POL; PLAN-47 only checks that a binding exists).
- **Wave 2 (WF-H, G-WF-16):** a definitions admin screen, per-agency customisation, and parallel steps.
- **PLAN-50:** delegation.

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- any secret, token or password (templates keep their placeholders, `__SVC_SECRET__` or `${…}`).

**Never:**
- push to `main`, force-push, or push anything before PLAN-42 is on `main`;
- merge with a pending or red check;
- touch another session's clone, branch, PR, stack or lock;
- disable host-key checking;
- let a service account approve or reject a step, or write a definition;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, mark a class not required, or exempt a type to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** — including being in the primary clone while PLAN-42 is unmerged.
- **AC-HCM-25 or `Hcm025PanelServerParityTests` would need a change** to pass under the new rules (P-13).
- **A user-facing caller of the start API** has no owning service (P-5).
- **The move onto `main` conflicts** in a way that would change PLAN-42's merged behaviour beyond the follow-ups in P-6.
- **A server deploy fails, and neither `-Rollback` nor the restore brings the demo back.**

---

## P0 — Timing, mode, preconditions, baseline, inventories

1. Write the `Started (UTC)` line (see «Timing»). Decide the mode and check the Preconditions.
2. `git switch -c feature/plan-43-wf-definition-governance-engine-api <base>`. Commit this prompt file.
3. **The plan's baseline** («المرحلة 0») into `reports/plan43-baseline.md`:
   - **0.2:** on the base, PLAN-42's internal enforce endpoint ignores `Mode` and `ActorUserId` from the body. Its `S2s042PolicyEnforceInternalTests` must be green.
   - **0.3:** the build.
   - **0.4:** the listed filters. Integration tests follow P-2.
   - **0.5:** the definitions: every `WorkflowDefinition` (code, version, `IsPublished`, `DomainEntityType`, the roles in `StepsJson`) and every `PolicyWorkflowBinding`. Take them from the seeders now, and confirm them with SQL when you next hold the stack lock (P5). Flag roles that are not in the catalog (`security`, `it-support`).
   - **0.6:** every caller of `api/workflow-instances` and every use of `EnforceStepRoleEntityTypes` (P-5).
4. **The P-4 inventory** of entity types and their reach.
5. **The server, read-only:**
   - the running tag;
   - the `WorkflowDefinitions` rows;
   - in-flight `WorkflowInstances` by type and status (enforcing step roles changes what can approve them);
   - the realm's roles.

Commit: `chore(wf043): baseline, definitions and callers inventory, entity types and who can reach them, server before-picture`.

## P1 — Definition lifecycle governance (plan §1)

- The model and its schema:
  - `WorkflowDefinitionStatus`, and the new `WorkflowDefinition` fields with `xmin` concurrency;
  - the `WF043_DefinitionGovernance` migration with the plan's backfill (P-3).
- The rules:
  - `WorkflowGuardrails`, and `WorkflowDefinitionValidator` with its Arabic error list;
  - `HrManagementRoles.WorkflowAdmin` and `All`, and the `WorkflowDefinitionAdmin` policy (no service accounts).
- The service and its endpoints:
  - `WorkflowDefinitionLifecycleService` with rules 1–8;
  - the controller's new routes, with the one-step publish removed;
  - the gateway rule.
- The engine and the seeds:
  - `WorkflowStarter` honours `ValidFrom` and `ValidTo`;
  - `WorkflowPackSeeder.EnsureVersionAsync`;
  - `WorkflowDefinitionHealthReport` (a warning and a metric, never a failed boot).
- The personas and the role (P-8).
- **Tests:**
  - `Wf043DefinitionLifecycleTests` (the two-person rule, the national tenant, immutability, the binding warning, retire);
  - `Wf043DefinitionValidatorTests` (every guardrail row, unknown roles, levels, conditions);
  - `GatewayPathAuthorizationMiddlewareTests`;
  - the realm and persona tests.

Commit: `feat(wf043): workflow definitions are written and published only by workflow-admin or hr-admin, published by a second person after validation, never edited once published; starts honour the validity window`.

## P2 — The guarded external start (plan §2, P-5, P-6, P-7)

- **The start:**
  - `ExternalApprovalTypes`, the wire contract `WorkflowApprovalStartRequest` (names and order as PLAN-60 §0.1), and `ActingUserScope` with `X-Acting-User-Name` in `ForwardActingUserHandler`;
  - `ExternalApprovalService` with rules 1–10;
  - the new `WorkflowInstance` columns and the `WF043_ExternalStart` migration;
  - `WorkflowEngine.Withdraw` and `WorkflowStepStatus.Withdrawn`.
- **The endpoints and policies:**
  - the `WorkflowExternalStart` and `WorkflowStepAction` policies;
  - `Start` → `ExternalApprovalService`, a new `cancel`, and `Approve`/`Reject` → `WorkflowStepAction`;
  - the gateway rules.
- **The callers:**
  - `WorkflowTriggerService` sends the new body with `forwardActingUser: true`;
  - `PolicyOrchestrator` no longer starts a registered type;
  - the PLAN-42 follow-ups (P-6).
- **The events:** custody events v2 and the four consumers (P-7).
- **Tests:**
  - `Wf043ExternalStartTests`: owner only, the requester from the header, the same key returning the same instance even when finished, 409 for another key, length and content limits, cancel by the owner only, no step action for a service account;
  - `Hcm025ReviewFixTests` updated: a user gets 403;
  - the consumer unit tests;
  - the S2s042 suites with 42c changed.

Commit: `feat(wf043): only the owning service starts a registered approval, with the real requester recorded; no service account acts on a step; custody outcomes carry their instance id and unlinked outcomes are ignored`.

## P3 — Separation of duties in every type (plan §3, P-4)

- `WorkflowStepRoleOptions` turns into exemption semantics: the old key is ignored, with a start-up warning.
- `WorkflowSodRules` (`Excluded`, `Subject`, `DistinctApprover`) is used by the evaluator in the plan's order and by `WorkflowPanelBuilder`.
- The configuration moves to `Workflow:StepRole:ExemptEntityTypes` in the three HR.Core settings files and `docker-compose.acceptance-prodlike.yml`, plus any exemption P-4 found.
- **Tests:**
  - `Wf043SodRulesTests`: requester, excluded user, subject employee, distinct approvers, approve against reject, `AdminBypass`;
  - `WorkflowStageGuardRoleTests` updated;
  - `Hcm025PanelServerParityTests` green **unchanged**.

Commit: `feat(wf043): step roles are enforced by default for every type; requester, excluded users and the subject employee cannot act; distinct approvers where the definition requires them`.

## P4 — Integration on PostgreSQL (plan Deliverables, P-2)

**`Wf043GovernanceIntegrationTests`** (Testcontainers):
- two-person publishing;
- `xmin`: publish and return at once on one definition, one wins;
- the validity window;
- the external start: 403 for a non-owner, 409 for another key, the same key returning the instance even when finished;
- `ApprovalStartCoordinator` in its transaction: no instance when the save fails;
- a service account approving gets 403;
- approving the last step of a custody instance writes an `OutboxMessage` row whose event carries `WorkflowInstanceId`;
- the custody consumer ignores an unlinked instance.

Then the regression filters of the plan's Verification block. Run them when Docker is free (P-2).

Commit: `test(wf043): governance, external start, separation of duties and v2 custody outcomes proved on PostgreSQL`.

## P5 — Acceptance on the prod-like stack (plan Acceptance, P-2, P-9)

1. Take the `acceptance-stack` lock. Start the stack (`acceptance-up.ps1 -Profile ProdLike`) and record the time to ready. Confirm the 0.5 inventory with SQL.
2. Run **`AcWf43DefinitionGovernanceTests`** alone first, with `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`: AC-WF-43a..f, positive and negative, all Pass.
3. **Then the whole job once, the same as CI:** `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci/acceptance-job.ps1 -Profile Both`. `-NoBuild` is fine within the same lock hold if step 1 built your HEAD. If P-2's window is still open (PLAN-42 has no open PR), take the stack down, release the lock, and end the turn with `WAITING-PEER:` naming this step.
   - **Why the whole job:** §3 changes who may approve in every workflow type, and the lab suites exercise most of them. In early mode it costs no wall-clock time, because you wait for PLAN-42 anyway. In normal mode it is cheaper than a red CI cycle.
   - Renew the lock during the run.
   - Check at least:
     - `AcHcm25ProductionApprovalTests` (unchanged), `Ac11PolicyWorkflowTests` (P-6), `AcS2s42TrustBoundaryTests` (42c changed);
     - `AcMsg40AtomicPublishingTests`, `AcInt37ProductionWiringTests`, `AcOps38FirstBootTests`, both recruitment journeys;
     - the lab workflow classes: `AcHcm21RequisitionApprovalUiTests`, `AcHcm22RecruitmentWorkflowsUiTests`, `AcEst24EstablishmentWorkflowUiTests`, `AcWf23WorkInboxUiTests`, `Ac33AssetLossLiveTests`.
   - After a fix, rerun only the failing classes with `-Classes`.
4. Every required class must Pass.
5. `down -v`, then release the lock. Restore the committed evidence.
6. Update `ci-scope.json` and `ac-ownership.json`, and run `scripts/wf043-validate.ps1`:
   - no service-account step action;
   - no `EnforceStepRoleEntityTypes` left;
   - no start of a registered type outside `ExternalApprovalService`;
   - the allow-list is empty.

Commit: `test(wf043): AC-WF-43a..f pass on the prod-like stack; AC-HCM-25, AC-11 and the PLAN-40 to PLAN-42 suites still green; CI scope and ownership updated`.

## P6 — Onto `main` (P-1; normal mode: merge `origin/main` only if it moved)

1. **Early mode:** if PLAN-42 is not on `origin/main`, write `WAITING-PEER:` and end the turn. On the next message, `git fetch origin` and check again.
2. When it is merged: `git rebase --onto origin/main <base> feature/plan-43-wf-definition-governance-engine-api`.
   - Resolve conflicts as the protocol says: keep both sides in the shared list files, and redo the P-3 migration step if needed.
   - Rebuild, and run P1–P4's tests again.
   - Rerun the acceptance classes whose code or configuration the conflicts touched, under the lock.
3. Read PLAN-42's final report (now on `main`) for anything that changes your work, and record it.

Commit (only if the move needed fixes): `fix(wf043): rebased onto main after PLAN-42, conflicts resolved, migrations on top`.

## P7 — The demo server (P-12)

Record every server change and its undo command **as you make it**.

1. Take the `demo-server` lock. Check P-12's condition: PLAN-42 on `main`, and the running tag on `origin/main`.
2. Take the backup, then **verify** it.
3. Run `scripts/vps/build-on-host.ps1` once, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`:
   - `hrm-bootstrap` applies the `WF043_*` migrations as `*_migrator`;
   - `keycloak-reconcile` creates `workflow-admin`;
   - if the deploy stops at `keycloak-reconcile` after PostgreSQL restarts, run the same command again (PLAN-40 incident).

   Record the downtime and the time to all healthy.
4. **Then check**, with evidence in `artifacts/plan43/vps/` (summarised in the final report, never in `acceptance-evidence/`):
   1. The definitions carry `Status`, and the backfill matches the before-picture.
   2. The start-up health report lists the invalid definitions (expected: those with `security` or `it-support` roles) without stopping the service.
   3. The realm has `workflow-admin`.
   4. A short demo tour still works: a recruitment approval by `e2e-checker-mgmt`, and a leave approval.
   5. A user token on `POST /api/hr-core/api/workflow-instances` gets 403 through the gateway.
   6. The in-flight instances from the before-picture are still actionable by the roles that should act on them, or the change is explained.
   7. `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green.
   8. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass, with the PLAN-38..42 server checks. Most of those are partial files of `AcOps36VpsBootTests`; PLAN-42's final report names its own.
5. Release the lock.
6. **If anything fails and cannot be fixed in the turn:** roll back (P-12), prove the demo works, and record it.

Commit: `test(wf043): demo server on governed definitions and the guarded start (migrations by the bootstrap, workflow-admin reconciled), PLAN-36..42 server checks still green`.

## P8 — Docs, review, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/WF_001_WORKFLOW_ENGINE.md`: governance, the external start, and the three rules;
   - `docs/SOD_MATRIX.md`;
   - `docs/EVENT_PUBLISHER_INVENTORY.md` (if P2 did not already);
   - `OPS_VPS_SINGLE_HOST.md`: the backup location and the change table;
   - **`docs/PARALLEL_PLANS.md`:** what this first parallel run taught: early start, `rebase --onto`, `WAITING-PEER:`, the Docker etiquette and priority of P-2, the git-ignored files a fresh clone needed, and the measured waits;
   - the «الحالة» lines for G-WF-04, G-WF-05 and G-WF-06;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Independent review** (one fresh subagent). It reads this prompt, the plan and the diff, and hunts for:
   - a path where `hr-core` or a service account writes or publishes a definition;
   - a one-person publish;
   - an edited published version;
   - a start that ignores the validity window;
   - a start of a registered type by a non-owner, or a requester taken from the body;
   - a service account acting on a step;
   - an outcome applied to an unlinked instance;
   - a type left without role enforcement and without a recorded exemption;
   - a rule the panel and the server judge differently;
   - a migration the previous tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests.
3. **Local CI, then the pre-push check, before any push** (P-2 for Docker): `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD, then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`. Both must be green.
4. **`reports/plan43-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations.
   4. The inventories: definitions, callers, entity types and their reach, exemptions.
   5. The guards and their own negative tests.
   6. Unit, integration and acceptance results; the PR run's artifact link as the evidence of record.
   7. The server: before and after, changes and their undo, the backup location.
   8. **The parallel run:**
      - the mode;
      - the base and the move onto `main`;
      - the conflicts;
      - lock waits;
      - `WAITING-PEER:` time;
      - how much of this plan overlapped PLAN-42's remaining work.
   9. Deferred items.
   10. Defects for later plans.
   11. **Timing:**
       - total wall-clock;
       - active time against waiting (CI, peer, locks);
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 43`.
   12. **A readiness note** for:
       - **PLAN-44 and PLAN-45**, which can run in parallel after this plan: what each builds on (`EnsureVersionAsync`, `ExternalApprovalService`, the v2 events, the exemption PLAN-45 lifts), and the files both touch;
       - **PLAN-47**, if it runs beside this plan.
5. **Merge (P-11), by you:**
   1. Merge the feature branch into a new `release/plan-43` from `main` (`--no-ff`).
   2. Write `reports/plan43-pr.md` (short).
   3. `git push -u origin release/plan-43`
   4. `gh pr create --base main --head release/plan-43 --title "PLAN-43: workflow definition governance, a guarded engine API, and separation of duties" --body-file reports/plan43-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`. Record the PR link, each check result, the merge commit and the post-merge run in `reports/plan43-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`. That note rides with the next plan's first commit; **do not open a second PR for it.**

## Definition of done

- **Definitions:** no route of `WorkflowDefinitionsController` lets `hr-core` or a service account write. Publishing takes two people and the validator, and a published version is immutable.
- **Validity:** `WorkflowStarter` and `ExternalApprovalService` honour the validity window.
- **Starts:** only the owning service account starts an instance from outside the coordinator, and the human requester is recorded. No service account acts on a step.
- **Events:** custody events v2 carry `WorkflowInstanceId`, and their consumers refuse an unlinked instance.
- **Separation of duties:** step-role enforcement is the default for every type; the exemptions are `AssetLossReport` until PLAN-45, plus any P-4 recorded with an owner. The three rules hold on the server and in the panel.
- **The tests:** AC-WF-43a..f = Pass (the PR run's artifact is the evidence of record). AC-HCM-25 is green unchanged, AC-11 and AC-S2S-42 pass, and the PLAN-36..42 suites are green.
- **The server:** runs this code, and the PLAN-36..42 server checks are green.
- **The documents:** the status lines, the engine document and the parallel protocol are updated.
- **The merge:** done by you, through a PR, after green checks and after PLAN-42, with the timing and parallel-run sections.

Then reply with a short summary in Arabic:
- what changed;
- what changed on the demo server;
- how much time the early start saved, and how long the plan waited for PLAN-42, for locks and for CI;
- the PR link;
- the path of the final report.
