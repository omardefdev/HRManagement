# Task: PLAN-46 — Cross-service approvals: the shared contract, and the ID card, discipline and separation wired to it

The deliverables:
- **One written contract** for any decision another service owns: the client (`IWorkflowApprovalClient`), the start request, the outcome event (`IWorkflowOutcomeDecided`), the rules for idempotency, failure and compensation, the inbox item, and server-side role enforcement.
- **An ID card is issued only after someone other than the requester approves it.**
- **A disciplinary action reaches payroll only after** compliance, then the authority, approve it.
- **Separation:** HR.Core's side is ready (registry, enforcement, inbox). PLAN-60 wires the separation record itself to this contract.
- **No published definition without a real owner,** and the architecture guard finds the consumer by reflection, not by a sentence.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder `D:\my projects\HRManagement`. There is no second clone and there are no locks.

**Standing owner instruction on merges** (PLAN-38 to PLAN-45 and PLAN-66): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-14).

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan46-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan46-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan46-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Record the time spent waiting for CI (`WAITING-CI:`).
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session **without ultracode**, at `xhigh`):
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only; root-causing separate failures.
- Run **one independent review**, before the server phase, so that the single server build carries its fixes. Use a refuter only for findings rated high.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan46/`. Read only the failing parts.
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
- service-to-service calls with acting users (PLAN-42, PLAN-43), idempotent starts, owner-side reconciliation, background jobs;
- MassTransit with versioned contracts, and the outbox patterns of PLAN-40 (including the consumer outbox);
- EF Core, PostgreSQL row level security, optimistic concurrency;
- Keycloak roles, Docker Compose, PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev`). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..45 did.

**Where things stand** when this prompt was written (2026-11-07):
- **`main`** = `c8252dc6`. It holds PLAN-43, PLAN-44, PLAN-66 and PLAN-45 (PR 43).
- **PLAN-45's merge note** is a single commit on the local branch `docs/plan-45-merge-note`, not yet on `main`. The local branch `docs/plan-42-merge-note` is already on `main`.
- **Open side PRs** about the E2E Playwright install: #40 and #42. Never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI since PLAN-66:**
  - a full PR run takes about 45 minutes, and one acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - `acceptance-job.ps1` takes `-Classes` and `-Stage`;
  - a new class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs.
- **What earlier plans built, and this plan reuses:**
  - **PLAN-43:**
    - `ExternalApprovalTypes`, with `ExternalOutcomeEvent { AssetLegacy, Generic }`, `AllowedWorkflowCodes`, `AudienceRoles` and `DetailsUrlPrefix`;
    - `WorkflowApprovalStartRequest`, already in `ServiceDefaults/Workflow/CrossService/`;
    - `ActingUserScope`, and `EnsureVersionAsync`;
    - `WorkflowExternalInstancesController` (start and cancel; bearer-only, the antiforgery pattern for the CodeQL gate).
  - **PLAN-44:**
    - `WorkflowInstanceInsert`, the conflict message and 409, `WorkflowInstanceRunningException`, `WorkflowOutcomeRefusedException`;
    - every handler checks state and link and runs inside the decision's transaction.
  - **PLAN-45** (its final report, §11):
    - the generic page: register a type in `ExternalApprovalTypes`, and the page, the inbox and the gate follow;
    - `WorkInboxAudience` takes its roles from `AudienceRoles`, and `ExternalApprovalGate` is one per registered type;
    - the pattern of `AssetApprovalStarter`, `AssetApprovalLoop` and its job, where a service with no tenant table walks tenants through `AuditLogs`;
    - **the exemption mechanism:** close a row in `docs/qa/workflow-step-role-exemptions.json`, and remove the type from the three HR.Core settings files, the prod-like compose file and `DefaultExemptEntityTypes`. Four exemptions remain, all for this plan: `SeparationRecord`, `DisciplinaryAction`, `CardRequest`, `Ticket`;
    - `WorkflowApprovalDetailsViewModel.FactLabels`.
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43); PLAN-44 lifted and restored `FORCE` inside the migration's transaction. This plan has such a change (P-3).
  - A new tenant job sets `TenantIdFlow` before its scope opens a connection, and walks tenants from a source outside row security (PLAN-44, PLAN-45).
  - A role from outside HR.Core acts on the generic page, not on `api/workflow-instances`, which keeps `HrCoreReadOnly` (PLAN-45).
  - An acceptance test posts the URL and the token the page renders (PLAN-44).
  - A persona change needs `scripts/acceptance-reset.ps1` before the first start (PLAN-43).
  - A user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh` (PLAN-45).
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - Run the server build detached (PLAN-44), and run the server suites with nothing heavy beside them (PLAN-45).
  - HR.Core falls back to the national tenant for a start with no tenant (PLAN-45 §8): every start must carry the request's tenant.
- **The demo:**
  - Its realm has `e2e-compliance` (`compliance`), `e2e-checker-mgmt` (`hr-management-approver`) and the two logistics users of PLAN-45, but **no `idcard` user and only one `compliance` user**. P-13 adds what the demo needs.
  - `EmployeeOrgUnitSnapshots` is empty on the demo server (PLAN-45 §9), so demo approvers must be agency-level, not branch-level.
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools.
  - The acceptance services have no container health check: readiness means `/health/ready`.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-46-wf-cross-service-approvals.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report. Its line numbers date from 2026-10-30 and have moved since PLAN-40..45: find the code by its content.
2. `docs/acceptance-plans/PLAN-60-sep-unblock-decision-governance.md` §0.1: the names this contract must keep.
3. The final reports of PLAN-43, PLAN-44 and PLAN-45: deviations, deferred items, readiness notes.
4. `reports/plan40-final-report.md` §11 (the publisher contract, the consumer outbox) and `docs/EVENT_PUBLISHER_INVENTORY.md`.
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.
6. `AssetApprovalStarter.cs`, `AssetApprovalLoop.cs`, `AssetApprovalStartRetryJob.cs` and `WorkflowOutcomeReconciliationJob.cs`: the patterns to follow.
7. `docs/WF_001_GAP_STUDY.md` (G-WF-01, G-WF-17), `docs/WF_001_WORKFLOW_ENGINE.md`, `docs/WF_002_WORK_INBOX.md`, `docs/SOD_MATRIX.md`, ADR-FUN-004, the `FUN_005_*` card documents.
8. `docs/OWNER_DECISIONS_REGISTER.md`: D-WF-1, D-ER-2, D-SEP-4.
9. Every file the plan cites.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **You are in `D:\my projects\HRManagement`, on `main`,** and no other plan session is running.
2. **`git fetch origin`**, and `origin/main` holds PLAN-45 (`c8252dc6` or later).
3. **The working tree is clean,** except for these, which may be present:
   - the owner's local `global.json` edit;
   - `tmp-vendors.html`;
   - git-ignored `artifacts/`;
   - untracked prompt files under `docs/acceptance-plans/execution/`.

   Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
4. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
5. **`gh auth status`** shows `omardefdev` with the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

Accepted by the owner on 2026-10-31 (wave-1 questionnaire, «accept the recommended» for every plan decision). The plan's defaults apply as written:

- **D-WF-1, the card chain:** `ID-CARD-ISSUANCE`, a new version with one step, «مراجعة مكتب البطاقات», role `idcard`, the requester excluded.
- **D-WF-1 / D-ER-2, the discipline channel: the engine.** `DISCIPLINARY-APPROVAL`, a new version:
  1. «مراجعة امتثال», `compliance`;
  2. «اعتماد صاحب الصلاحية», `hr-management-approver`.

  `RequireDistinctApprovers=true`. The penalty ladder and investigations are wave 2 (ER).
- **The separation chain** is PLAN-60's (D-SEP-4). This plan only registers `SEPARATION-APPROVAL` and `SEPARATION-APPROVAL-SENSITIVE`.
- **No automatic card submission on hire:** `IdCard:Approval:AutoSubmitOnHire=false`. The request created by `EmployeeCreatedConsumer` stays a `Pending` draft until an `idcard` employee submits it.
- **Existing records:**
  - disciplinary actions already published become `Approved` with `ApprovedBeforeEngine=true`, and are never published again;
  - `Pending` card requests stay drafts that need submitting, and `Processed` ones are unchanged.
- **Owner-side reconciliation:** every `WorkflowApproval:Sweep:IntervalMinutes=10`, for records in `PendingApproval` older than `StaleAfterMinutes=15`. In the acceptance profile, 1 and 2.
- **Orphan definitions:** `HIRE-WIZARD`, `TRANSFER-APPROVAL`, `IT-TICKET` and `SEPARATION-WIZARD` are retired (`RetiredCodes`). The policy rules that fire them are PLAN-49's.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-46-wf-cross-service-approvals` from `origin/main`, in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-45's merge note from `docs/plan-45-merge-note`;<br>&nbsp;&nbsp;– commit this prompt file;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **Docker hygiene.**<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• Before a Docker-heavy run (`ci-local.ps1`, Testcontainers suites, a stack), check `docker ps`. Remove only this repository's leftovers: compose projects `hrm-*` and Testcontainers.<br>• Never prune volumes or images of other projects. |
| P-3 | **Migrations and rollback safety.**<br>• **`WF046_CardRequestApproval` in IDCard:**<br>&nbsp;&nbsp;– `PendingApproval` and `Approved` go at the end of `CardRequestStatus`;<br>&nbsp;&nbsp;– the new fields, nullable or with a database default, and `xmin`;<br>&nbsp;&nbsp;– no data change.<br>• **`WF046_DisciplinaryApproval` in Compliance:** existing rows must become `Approved`, `ApprovedBeforeEngine=true`, and must never be published again. Under forced row security an `UPDATE` by the migrator changes nothing (PLAN-43). Use one of:<br>&nbsp;&nbsp;– column defaults that fill existing rows: `ADD COLUMN … DEFAULT <Approved>` and `DEFAULT true`, then `ALTER COLUMN … SET DEFAULT` to `Draft` and `false` for new rows. This works whatever row security says;<br>&nbsp;&nbsp;– PLAN-44's pattern, `NO FORCE` and `FORCE` inside the migration's transaction, for a value that needs a per-row expression (`OutcomePublishedAt = CreatedAt`);<br>&nbsp;&nbsp;– or leave `OutcomePublishedAt` null for legacy rows and say so.<br>**Prove it** with an integration test that applies the migration as a non-superuser owner under forced row security, on rows seeded in two tenants.<br>• **Rollback safety.** The previous tag must run beside the new schema. Record what it does with a card in `PendingApproval` or `Approved` (unknown values) and with a disciplinary action in `Draft` or `PendingApproval`: it knows no approval, so such an action has no payroll effect until the roll-forward.<br>• No byte order mark in migration files. Run `has-pending-model-changes` for IDCard, Compliance and HR.Core, and check `scripts/build-ef-bundles.sh`. |
| P-4 | **The shared contract (plan §1).**<br>• **The types:**<br>&nbsp;&nbsp;– keep PLAN-43's `WorkflowApprovalStartRequest` and add the plan's types beside it, with PLAN-60 §0.1's names and order;<br>&nbsp;&nbsp;– `SubjectEmployeeId` stays optional and last.<br>• **`HttpWorkflowApprovalClient`:**<br>&nbsp;&nbsp;– maps every status code as the plan says, and never throws except `OperationCanceledException`;<br>&nbsp;&nbsp;– a missing base address means `EngineUnavailable` and an error log, never assumed success;<br>&nbsp;&nbsp;– it sends the acting user (`forwardActingUser: true`) **and the record's tenant**; a start with no tenant is refused locally.<br>• **The idempotency key is pinned per submission:** `"{EntityType}:{Id}:{SubmissionNo}"`. The same key returns the same instance, even under a race (PLAN-44). A finished instance for the key: its outcome is applied through `GetStatusAsync`.<br>• **Rule 3:** no `PendingApproval` without `InstanceId` in the same save; an architecture test per owner.<br>• **Rules 4 and 5:** a save that fails after a start cancels it on a best-effort basis. A cancel answered `AlreadyTerminal` applies the final outcome and never returns the record to draft.<br>• **`WorkflowOutcomeGuard`:** link, pending state, `SchemaVersion`, an excluded decider, and the metric `workflow_outcome_ignored_total{entity_type,reason}`.<br>• **`WorkflowApprovalSweepJob<TStore>`:**<br>&nbsp;&nbsp;– walks tenants from a source outside row security (the `AuditLogs` pattern of PLAN-45, or whatever each service has), setting `TenantIdFlow` before each scope opens a connection, with one leader per cycle (`AdvisoryLeaderElection`);<br>&nbsp;&nbsp;– applies a finished outcome with the same `ApplyOutcomeAsync` as the consumer.<br>• **`FakeWorkflowApprovalClient`** in `HRManagement.TestSupport`; PLAN-60 needs it. |
| P-5 | **HR.Core's side (plan §2).**<br>• **The registry:** `CardRequest`, `DisciplinaryAction` and `SeparationRecord` as `Generic`, with the owners, codes, audiences and prefixes of the plan. Use the existing service client id constants; add them if missing.<br>• **`ExternalOutcomePublisher`:**<br>&nbsp;&nbsp;– publishes `IWorkflowOutcomeDecided` on approve, reject and cancel, inside the transaction and before the last save (PLAN-40, PLAN-44);<br>&nbsp;&nbsp;– `Decisions` come from `StepHistoryJson`;<br>&nbsp;&nbsp;– the inventory row goes in the same commit.<br>• **`GET api/workflow-instances/{id}/external-status`** goes on `WorkflowExternalInstancesController`, bearer-only, for the owning service only (`SourceClientId == caller`); otherwise 404.<br>• **`WorkflowGuardrails`** for the three types, as the plan says.<br>• **The policies:** `idcard` and `compliance` join `WorkflowStepAction`, and `idcard` joins `HrCoreWorkInbox`. Never `ModuleHrCore` or `HrCoreReadOnly`; their own negative test is a 403 on `/ui/hr-core/Employees`.<br>• **The exemptions are all lifted:** `ExemptEntityTypes = []`. `Ticket` is moot once `IT-TICKET` is retired. Close the four rows in the exemption registry and their dated obligations, by PLAN-45's mechanism.<br>• **The wiring:**<br>&nbsp;&nbsp;– `deploy/wiring/service-wiring.json` gets `calls[]` for start, cancel and `external-status` for `svc-idcard`, `svc-compliance` and `svc-hr-contracts`, with `serverPolicy: WorkflowExternalStart`; then run `scripts/int037-render-wiring-doc.ps1`;<br>&nbsp;&nbsp;– check, without editing them by hand, that the scopes and `Integration__HrCoreBaseUrl` reach the three services;<br>&nbsp;&nbsp;– the prod-like compose gets `IdCard__Approval__AutoSubmitOnHire: "false"` and the sweep intervals of the acceptance profile. |
| P-6 | **The card (plan §3).**<br>• No path in IDCard creates an `IssuedCard` except from an `Approved` request. Pin it with an architecture test over every `IssuedCards.Add` and every publish of `ICardIssued`.<br>• `Renew` issues nothing: it opens a `Replacement` request.<br>• Update the inventory rows of `ICardIssued` in the same commit.<br>• The persona `a-idcard-2` (`idcard`, agency A) goes into `testdata/acceptance/personas.json` **and** `AcceptancePersonaCatalog`, and its realm entries. |
| P-7 | **Discipline (plan §4).**<br>• `Create` saves a `Draft` and publishes nothing, then submits.<br>• **Only `DisciplinaryApprovalOutcomeConsumer` publishes `IDisciplinaryActionTaken`,** with the consumer outbox (`consumerOutbox: true`, already on in Compliance), in one transaction with the status change. A redelivery publishes nothing twice. Pin it with `Wf046DisciplinaryPublishTests`.<br>• The consumer refuses an approval whose decisions include the creator (`HasExcludedDecider`), and logs it.<br>• `Edit` works only in `Draft` and `Rejected`; `Appeal` and `DecideAppeal` only on `Approved`.<br>• Update the inventory row of `IDisciplinaryActionTaken` in the same commit. |
| P-8 | **Separation and PLAN-60 (plan §5).**<br>• HR.Core's side only: the registry entry, enforcement, the inbox, and retiring `SEPARATION-WIZARD`.<br>• Read PLAN-60 §0.1 against what you built, and record the alignment. Differences 3 to 5 should already be fixed in PLAN-60's text.<br>• If PLAN-60 still names `Workflow:EnforceStepRoleEntityTypes`, or misses owner-side reconciliation, or the `AlreadyTerminal` rule, correct PLAN-60's document in this plan's docs commit, and record it. |
| P-9 | **Orphan definitions and the guard (plan §6).**<br>• `RetiredCodes` gets the four codes. Instances already running stay resolvable on their pinned definition.<br>• `WorkflowDefinitionsHaveHandlersTests` maps each external type to `(ServiceName, ConsumerTypeName)`, and finds the consumer by reflection as an `IConsumer<IWorkflowOutcomeDecided>`.<br>• `Wf046RegistryConsistencyTests`: every `Generic` registry type is in the guard, and every guard entry is registered.<br>• The policy rules that fire the retired codes stay for PLAN-49. Prove that they start nothing (AC-WF-46j). |
| P-10 | **Acceptance placement (PLAN-41 and PLAN-66 rules).**<br>• `AcWf46CrossServiceApprovalTests` goes in `prodLike[]`, `required: true`, in the **isolated** group: AC-WF-46i stops `hr-core-web`, and AC-WF-46j changes a policy mode. If 46j needs an overlay, use a `composeFiles` stage as PLAN-42 did.<br>• `ac-ownership.json` gets `AC-WF-46` (owner PLAN-46, `provesClaim: true`).<br>• Tests post the URL and the token the page renders.<br>• Copy what the report needs into `artifacts/plan46/`, then restore the committed evidence: local evidence is never committed. |
| P-11 | **Local testing (owner's rule of 2026-11-06).** Locally, run only the acceptance classes this plan touches and their direct regressions, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`. **The full suite runs once, in CI.** The list, at least:<br>• **prod-like:** `AcWf46CrossServiceApprovalTests`, `AcWf45AssetApprovalLoopTests`, `AcWf43DefinitionGovernanceTests`, `AcWf44InstanceIntegrityTests`, `AcHcm25ProductionApprovalTests`, `AcS2s42TrustBoundaryTests`, `AcMsg40AtomicPublishingTests`;<br>• **lab:** `Ac11PolicyWorkflowTests`, `AcWf23WorkInboxUiTests`, and both recruitment journeys (a hire creates a card draft);<br>• and any class P0 finds that touches ID cards, disciplinary actions, Payroll's disciplinary consumer, the inbox, the gateway rules or the policies you change.<br>Known local-only failures of lab classes that need PowerShell 7 (PLAN-43 §6) are recorded, not chased. |
| P-12 | **PowerShell 5.1.**<br>• Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile ProdLike` (it prepares the realm; the plan's `prepare-keycloak-import.ps1` line is dated). Run `scripts/acceptance-reset.ps1` first, since a persona is added. |
| P-13 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan46-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Build once,** on the final code, detached; then `scripts/vps/deploy.ps1 -Tag <sha12>`.<br>• **Demo users the chains need,** added to the manifest's `demoUsers` and to the live realm through `scripts/vps/add-demo-user.sh`, with the demo password already in the server's `.env`, never printed. All are agency-level in the demo agency:<br>&nbsp;&nbsp;– two `idcard` users, a requester and a reviewer;<br>&nbsp;&nbsp;– a second `compliance` user, for step 1, beside `e2e-compliance` who creates.<br>&nbsp;&nbsp;Step 2 is `e2e-checker-mgmt`. Record the usernames for the owner's QA guide.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn: `scripts/vps/deploy.ps1 -Rollback` to PLAN-45's tag, safe by P-3. Restore the backup only if data was damaged, prove the demo works, and record the downtime. |
| P-14 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P8, after local CI and the pre-push check are green.<br>2. `gh pr create`; wait («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>` and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-15 | **No regression of PLAN-25 and PLAN-36..45, and PLAN-66.** These stay green:<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests` (unchanged);<br>• `AcWf43`, `AcWf44`, `AcWf45`, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, and both recruitment journeys;<br>• `OutboxPublishOrderingArchitectureTests` (with the new rows), `Data001ConcurrencyArchitectureTests`, the `Ten002c` row-security tests, `ProductionWiringMatrixTests`, `S2s042InternalRoutesAreServiceOnlyTests`, `GatewayPathAuthorizationMiddlewareTests`, the exemption tests, `Qa066*`;<br>• `scripts/wf043-validate.ps1`, `scripts/hcm025-validate.ps1`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan):
- **Done earlier:** publishing after the last save (PLAN-40); service trust (PLAN-42); the external start, the registry and default enforcement (PLAN-43); concurrency and the unique running-instance index (PLAN-44); the generic page and the inbox for external types (PLAN-45).
- **PLAN-60:** the separation state machine, its consumer, its execution guard, and its two definitions.
- **PLAN-61:** dismissal by a disciplinary decision, and suspending pay (G-ER-02).
- **PLAN-49:** removing the orphan policy rules.
- **Wave 2:**
  - the card journey's other fixes (`ICardIssued` from `ProcessRequest`, G-IDA-03; the UI actions, G-IDA-04);
  - the discipline model (investigation, the penalty ladder, `PenaltyDays`, G-ER-01);
  - moving AdminLogistics' starter onto the shared client.

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
- give `idcard` or `compliance` any HR.Core access beyond the inbox and the approval page;
- publish a disciplinary action again that was published before this plan;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, mark a class not required, or keep an exemption to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails.**
- **Existing disciplinary actions cannot be marked approved** under forced row security by any of P-3's paths.
- **A sweep job cannot find a tenant list** outside row security without new trust between services (P-4).
- **A chain cannot work** without giving `idcard` or `compliance` `ModuleHrCore` or `HrCoreReadOnly`.
- **AC-HCM-25 or `Hcm025PanelServerParityTests`** would need a change.
- **A server deploy fails,** and neither `-Rollback` nor the restore brings the demo back.

---

## P0 — Timing, preconditions, baseline, inventories

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions.
2. `git switch -c feature/plan-46-wf-cross-service-approvals origin/main`. Bring in PLAN-45's merge note and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan46-baseline.md`:
   - **0.2:** PLAN-40, PLAN-43 and PLAN-45 are on `main`.
   - **0.3:** the build.
   - **0.4:** the listed filters.
   - **0.5:** card requests by status, issued cards with no request, disciplinary actions, and instances of the six types in HR.Core. Take them from the acceptance data when you next start the stack, and read-only from the server now.
   - **0.6:** the PLAN-60 alignment (P-8).
4. **Inventories:**
   - the owner of `DisciplinaryActions` and `CardRequests`, and their row security flags (P-3);
   - a tenant source outside row security in IDCard and Compliance (P-4);
   - every `IssuedCards.Add`, every publish of `ICardIssued` and `IDisciplinaryActionTaken`, and their consumers (Payroll's included);
   - every caller of the actions this plan changes;
   - the policies and gateway rules for `idcard` and `compliance`;
   - the acceptance classes of P-11;
   - the demo users and the demo agency (P-13).
5. **The server, read-only:**
   - the running tag;
   - the counts of 0.5;
   - the definitions' versions and published state, including the four orphans;
   - the realm's users with `idcard`, `compliance` or `hr-management-approver`.

Commit: `chore(wf046): baseline, cards and disciplinary actions on the acceptance data and the server, definition versions, row security, publishers and consumers, tenant sources, demo users`.

## P1 — The shared contract (plan §1, P-4)

- **The types** in `ServiceDefaults/Workflow/CrossService/` and `MessageContracts/Events/`.
- **The machinery:**
  - `HttpWorkflowApprovalClient` and `AddWorkflowApprovalClient`;
  - `WorkflowOutcomeGuard`;
  - `WorkflowApprovalSweepJob<TStore>` with `IWorkflowApprovalOwnerStore`;
  - `FakeWorkflowApprovalClient`.
- **Tests:**
  - `Wf046HttpWorkflowApprovalClientTests`: every status code, a timeout, a missing address, the tenant and acting user sent, never a throw;
  - `Wf046OutcomeGuardTests`: a wrong instance, a state that is not pending, the creator among the deciders, a higher version;
  - the sweep job's unit tests.

Commit: `feat(wf046): one cross-service approval contract: client, start request, outcome event, guard, owner-side sweep, and a fake for tests`.

## P2 — HR.Core's side (plan §2, §6, P-5, P-9)

- **The registry and the publishing:**
  - the three registry entries;
  - `ExternalOutcomePublisher`;
  - `HandledEntityTypes`;
  - `external-status`.
- **The rules and the reach:**
  - the guardrails;
  - the policies;
  - all four exemptions lifted;
  - the wiring and the prod-like compose entries.
- **The definitions:**
  - `ID-CARD-ISSUANCE` and `DISCIPLINARY-APPROVAL`, new versions through `EnsureVersionAsync`;
  - `RetiredCodes`.
- **The guard:** `WorkflowDefinitionsHaveHandlersTests` by reflection, `Wf046RegistryEnforcedTests`, `Wf046RegistryConsistencyTests`.
- **Tests:** the policy and gateway tests, with the 403 on `/ui/hr-core/Employees` for both roles.

Commit: `feat(wf046): HR.Core registers the card, discipline and separation approvals, publishes one outcome event, answers status to the owner, enforces every step role, and retires four orphan definitions`.

## P3 — The card (plan §3, P-6)

- **The model and its schema:** the statuses, the fields, the `WF046_CardRequestApproval` migration, and `CardIssuanceWorkflow`.
- **The approval flow:**
  - `CardRequestApprovalService` (submit, withdraw);
  - `CardRequestApprovalOutcomeConsumer`;
  - `CardRequestApprovalStore` with the sweep job;
  - `AddWorkflowApprovalClient`.
- **Issuing:** `ProcessRequest`, `IssuedCards/Create`, `Renew` and `Edit`, as the plan says.
- **The UI:** `Details.cshtml` and `Index.cshtml`.
- **The persona:** `a-idcard-2`.
- **Tests:** `Wf046CardApprovalTests` (through `FakeWorkflowApprovalClient`), and the architecture test of P-6.

Commit: `feat(wf046): an ID card is issued only from a request someone other than the requester approved; renewal opens a request`.

## P4 — Discipline (plan §4, P-3, P-7)

- **The model and its schema:**
  - `DisciplinaryApprovalStatus`, the fields;
  - the `WF046_DisciplinaryApproval` migration, with the existing rows handled under forced row security (P-3).
- **The approval flow:**
  - `Create`, `Submit` and `Withdraw` (`DisciplinaryApprovalService`);
  - `DisciplinaryApprovalOutcomeConsumer`, on the consumer outbox;
  - `DisciplinaryApprovalStore` with the sweep job.
- **Edit, appeal and the UI,** as the plan says.
- **Tests:** `Wf046DisciplinaryApprovalTests` and `Wf046DisciplinaryPublishTests`.

Commit: `feat(wf046): a disciplinary action reaches payroll only after compliance and the authority approve it; actions published before stay approved and are never published again`.

## P5 — PostgreSQL, acceptance, review (plan Verification and Acceptance, P-10, P-11)

1. **PostgreSQL** (Testcontainers):
   - `Wf046ExternalOutcomePostgresTests` (HR.Core):
     - the same key twice, a wrong owner (403), another key (409);
     - the last approval writes the `IWorkflowOutcomeDecided` outbox row with every decision, in the same transaction;
     - a cancel writes `Cancelled`, and a cancel after completion answers `AlreadyTerminal`;
   - `Wf046DisciplinaryConsumerPostgresTests` (Compliance):
     - the consumer on the consumer outbox gives `Approved` and exactly one event;
     - a redelivery gives no second event;
     - `xmin` stops a withdraw racing the outcome;
   - the Compliance migration as a non-superuser owner under forced row security (P-3);
   - each sweep job under forced row security in two tenants (P-4).

   Then the plan's integration filters.
2. **Acceptance.** Run `scripts/acceptance-reset.ps1`, then start the stack (`acceptance-up.ps1 -Profile ProdLike`) and record the time to ready.
   - Run **`AcWf46CrossServiceApprovalTests`**: AC-WF-46a..j, positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   - Then the P-11 list through the job script with `-Classes`, per profile. All required classes must Pass.

   `down -v`. Restore the committed evidence. Update `ci-scope.json` and `ac-ownership.json` (P-10).
3. **Independent review** (one fresh subagent), before the server, so the single build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - a card issued, or a disciplinary event published, without an approval by someone other than the requester;
   - an old disciplinary action published again;
   - `PendingApproval` without an instance;
   - a start without the record's tenant;
   - an outcome applied to an unlinked instance, or to a record the owner already closed;
   - a withdraw answered `AlreadyTerminal` that returns to draft;
   - a sweep that sees nothing under forced row security;
   - `idcard` or `compliance` reaching anything in HR.Core beyond the inbox and the page;
   - a guard entry found by name instead of by reflection;
   - something the previous tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(wf046): AC-WF-46a..j pass on the prod-like stack; the outcome event, the consumer outbox, the migration and the sweeps proved on PostgreSQL; review fixes; CI scope and ownership updated`.

## P6 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to the same context after yours, remove yours and add them again on top. Rebuild, and rerun the unit and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you.

Commit (only if the merge needed fixes): `fix(wf046): main merged, conflicts resolved, migrations on top`.

## P7 — The demo server (P-13)

Record every server change and its undo command **as you make it**.

1. Check P-13's condition.
2. Take the backup, then **verify** it.
3. **Build and deploy.** Run the build once, detached, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`:
   - `hrm-bootstrap` applies the two `WF046_*` migrations as `*_migrator`;
   - `keycloak-reconcile` brings the realm in line;
   - if the deploy stops at `keycloak-reconcile` after PostgreSQL restarts, run the same command again (PLAN-40 incident).

   Record the downtime and the time to all healthy.
4. **The demo users** of P-13, and proof that each logs in.
5. **Then check**, with evidence in `artifacts/plan46/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them.
   1. The migrations applied. Every disciplinary action from before is `Approved` with `ApprovedBeforeEngine=true`, and none was published again: Payroll's processed records for them are unchanged.
   2. The definitions: the new card and discipline versions are published, and the four orphans are retired. No type is exempt in the running settings.
   3. **A real card on the demo:** the requester submits; the reviewer approves on the generic page reached from the inbox; the requester processes it; the card is `Active`. The requester cannot approve his own request.
   4. **A real disciplinary action:** `e2e-compliance` creates it, the second compliance user approves step 1, `e2e-checker-mgmt` approves step 2. Payroll records `DisciplinaryActionTakenConsumer` once.
   5. A withdraw of a pending card returns it to draft.
   6. The sweep jobs' log lines and the metric are present.
   7. `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green.
   8. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass, with the PLAN-38..45 server checks.
6. **If anything fails and cannot be fixed in the turn:** roll back (P-13), prove the demo works, and record it.

Commit: `test(wf046): demo server on cross-service approvals (migrations by the bootstrap, card and discipline chains, demo users, orphans retired), PLAN-36..45 server checks still green`.

## P8 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `WF_001_WORKFLOW_ENGINE.md`: the section «الاعتماد العابر للخدمات»;
   - `WF_002_WORK_INBOX.md`, `SOD_MATRIX.md`, ADR-FUN-004 (its claim that discipline is not issued outside the workflow is now true), and the `FUN_005_*` card documents;
   - `EVENT_PUBLISHER_INVENTORY.md`, if not already done;
   - `OPS_VPS_SINGLE_HOST.md`: the backup location, the change table, the sweep jobs;
   - PLAN-60's document if P-8 found a difference;
   - the «الحالة» lines for G-WF-01 («مغلق للبطاقة والتأديب، والفصل مع PLAN-60») and G-WF-17;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local CI, then the pre-push check, before any push** (P-2): `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD, then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`. Both must be green.
3. **`reports/plan46-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations.
   4. The inventories: cards, disciplinary actions and instances (acceptance and server, before and after), definition versions, row security, tenant sources.
   5. The guards and their own negative tests.
   6. Unit, integration and acceptance results; the PR run's artifact link as the evidence of record.
   7. The server: before and after, changes and their undo, the backup location, **the demo users for the owner's QA guide** (usernames only).
   8. The PLAN-60 alignment.
   9. Deferred items.
   10. Defects for later plans.
   11. **Timing:**
       - total wall-clock;
       - active time against waiting for CI;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 46`.
   12. **A readiness note** for PLAN-47 and PLAN-60: what they reuse (the contract, the fake client, the sweep, the guard entry), and the files they share with this plan.
4. **Merge (P-14), by you:**
   1. Merge the feature branch into a new `release/plan-46` from `main` (`--no-ff`).
   2. Write `reports/plan46-pr.md` (short).
   3. `git push -u origin release/plan-46`
   4. `gh pr create --base main --head release/plan-46 --title "PLAN-46: cross-service approvals (the shared contract, the ID card and discipline wired, separation ready)" --body-file reports/plan46-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`. Record the PR link, each check result, the merge commit and the post-merge run in `reports/plan46-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`. That note rides with the next plan's first commit; **do not open a second PR for it.**

## Definition of done

- **The contract** is published with PLAN-60 §0.1's names and compatible additions, and documented in `WF_001_WORKFLOW_ENGINE.md`.
- **No card is issued, and no disciplinary action reaches payroll,** before an approval in the engine by someone other than the requester, with live evidence. Actions published before this plan stay approved and are never published again.
- **Separation:** HR.Core's side is ready; PLAN-60 closes HR.Contracts' side.
- **The guard** points at consumer types that exist, and no orphan definition is published. No type is exempt from role enforcement.
- **The tests:** AC-WF-46a..j = Pass (the PR run's artifact is the evidence of record), and AC-WF-45, AC-11 and the PLAN-25 and PLAN-36..45 suites are green.
- **The server:** runs this code, and the demo has users who can walk the card and discipline chains.
- **The documents:** the status lines, the engine and inbox documents, the SoD matrix and ADR-FUN-004 are updated.
- **The merge:** done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed;
- what changed on the demo server, with the new demo usernames (no password);
- how long the plan took, and how long it waited for CI;
- the PR link;
- the path of the final report.
