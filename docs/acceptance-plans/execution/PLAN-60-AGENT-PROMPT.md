# Task: PLAN-60 — The separation journey unblocked, and its decision governed (SEP-A)

The deliverables:
- **A separation is executed from the interface, with production settings, and only on an approved, numbered decision:**
  - the record has an explicit state machine on the server: `Draft` → `PendingApproval` → `Approved` → `Executing` → `Completed`, with `Rejected`, `AwaitingIntervention` and `Cancelled`;
  - its approval runs through HR.Core's workflow engine by PLAN-46's mechanism (D-SEP-4), and the creator never approves;
  - no execution without an administrative decision numbered `DEC-SEP-YYYY-#####` and the employee's notification;
  - the clearance checklist can be completed from the interface: HR's items by hand with evidence, the parties' items by the party's acknowledgement, never by HR;
  - no separation of an employee who does not exist or whose service ended, no two open records for one employee, no contract of another employee, and no change of substance after submission.
- **AC-SEP-60a..i pass** on the production-like profile, required in CI, with a leaver fixture that PLAN-61..65 extend.
- **A migration (`SEP060`) gives every existing record its status** under forced row security, proved as the migrator.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P10** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**Why this plan exists** (`docs/FUN_002_SEPARATION_GAP_STUDY.md`: G-SEP-01, G-SEP-04, G-SEP-11):
- **No separation can be carried out from the interface** (G-SEP-01). No form completes a checklist item: `Step2CompleteStep` and `OverrideChecklistStep` have no caller. And the publication demands all six items, some of which only happen after execution. Every journey stops at Step 2.
- **Nothing governs the decision** (G-SEP-04):
  - the record has no status, only two flags;
  - its approval definition `SEPARATION-WIZARD` was an orphan, retired by PLAN-46;
  - `ApprovedBy` is the creator, and `Documents` is free text with no link to an administrative decision;
  - the linked transaction is pushed to `Executing` with no actor;
  - the contract is terminated at a manual item, before the effective date and with no end date;
  - «today» is UTC.
- **The record's integrity** (G-SEP-11):
  - nothing checks the employee, or another open record, and no index stops a second one;
  - the contract list is the newest 500 of every employee;
  - the employee and the contract can be edited after the checklist;
  - the transaction is found as «the employee's latest», and `Advance` moves it by hand.
- **PLAN-46 built the cross-service approval** and left `SeparationRecord` registered with `PendingPlan: "PLAN-60"`. **PLAN-61..65 build on this plan,** through its `AcceptanceLeaverFixture`.

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder. There is no second clone and there are no locks.
- **The main folder** is `/home/omar/HRManagement` on the owner's Linux notebook, where PLAN-53..59 ran. If the owner opened this session elsewhere, that folder is the main folder: record it. A folder's path is never a reason to stop.
- **PLAN-59 is merged** (Preconditions).

**Complete execution** (owner, 2026-11-12, standing since PLAN-51): the owner wants every plan carried out in full, however long it takes.
- Time is never a reason to skip, defer or allowlist anything.
- Only what the plan itself assigns elsewhere stays out: its «Out of scope».
- Where the plan speaks in general, this prompt makes it concrete, and all of it is in scope:
  - **the state machine** (§1, P-4): its table, the derived flag, the linked transaction moved with a real actor, the concurrency token;
  - **the approval** (§2, P-7): two definitions seeded, submission and withdrawal, the outcome consumer, the sweep, the guard's entry;
  - **the decision** (§3, P-8);
  - **the checklist** (§4, P-9): the modes, the four new items, manual completion with evidence, exceptions by two people, the parties' acknowledgements;
  - **the execution and the policies** (§5, P-10): the order of checks, the contract's end date, Damascus's date, the explicit policies with their architecture tests, cancellation by state, interventions, the saga's end;
  - **the integrity** (§6, P-6): one creation path for three entries, the employee and contract checks, the employee's contracts only, the lock, the partial unique index, the transaction's link, `Advance`'s refusal;
  - **the migration and its backfill** (the plan's five rules, and P-5's two more), proved as the migrator;
  - **the screens** (P-11);
  - **the tests:**
    - the plan's six unit classes;
    - its PostgreSQL class with its four cases;
    - the migration's own test;
    - `Sep060ExclusionTests` (P-7), `Sep060ServicePolicyHttpTests` (AC-SEP-60d's half without the gateway) and `Sep060RollbackReadingTests` (P-5);
  - **AC-SEP-60a..i** (P-12), required, with `AcceptanceLeaverFixture` and the persona `a-leaver`, repeatable on one stack without a reset;
  - **the scripts** `sep060-validate.ps1` (with `-SelfTest`) and `sep060-report-unlinked.ps1` (P-15);
  - **the documents** of the plan's Deliverables, and the study's three «الحالة» lines (P-16);
  - **what this plan changes for older tests and server checks** (P-13), each with its meaning kept;
  - **from its first commit:** every new action names its policy and its org-scope stance, and every new or changed write validates an antiforgery token.

**Where this prompt departs from the plan's text** (each with its reason in the decisions, and in the final report):
- **Two acceptance classes, not one** (P-12, P-14). AC-SEP-60i stops `hr-contracts-web` and empties its outcome queue, so it runs in the `isolated` group, where each class runs alone. AC-SEP-60a..h stay in `prodLike[]` `main`, as the plan says.
- **PLAN-51's policy split stays** (P-10). The plan asks `HrContractsApprove` on every POST. PLAN-51 already gave each POST an explicit policy under D-51-2, so that an agency can tell makers from checkers by configuration:
  - the maker's acts keep `HrContractsWrite`;
  - the acts of a second person take `HrContractsApprove`;
  - `Step3Execute` moves from `HrContractsWrite` to `HrContractsApprove`: an execution is a checker's act, as `PublishSeparation` already was;
  - by default both policies hold the same roles.
- **AC-SEP-60c's first refusal is a role's, not the exclusion's** (P-12). No persona holds `hr-contracts` with `hr-core`, so the maker is refused by a role (HR.Core's module, or the step's) before any separation-of-duties rule: the engine checks the role first. The test records the refusal it meets, and `Sep060ExclusionTests` proves the exclusion of someone who holds the role.
- **The two definitions are seeded as version 1 through `WorkflowPackSeeder`'s list** (P-7). `EnsureVersionAsync` only raises a published definition, and throws when there is none; the two codes have none. `EnsureCrossServiceChainsAsync` then keeps their roles. Both require distinct approvers.
- **A record no person created has no creator to exclude** (P-7). This covers the inbound requests (a judgment, a medical decision, a resignation from another system) and the seed. For such a record, the submission excludes the submitter, and the subject employee through `SubjectEmployeeId`. The plan's «refused if the creator is empty» holds for every record a person created.
- **The backfill also cancels the records with no employee** (`EmployeeId = Guid.Empty`; the plan counts them in 0.5). **It never cancels, as a duplicate, a record whose saga started** (P-5).
- **`Delete` goes, and the interventions' cancel refuses a separation** (P-10). A cancellation now needs a reason. Cancelling an executing separation is PLAN-63's (the plan's rule).
- **The demo goes only as far as `Approved`, then cancels** (P-17). A decision spends an official number, and an execution would end a demo employee's service.
- **The rollback is fix-forward** (P-5, P-18). The previous tag can still run on the migrated schema, and a start-up reconciler brings back in line what it changed.
- **The work is pushed and merged by you.** The plan's DoD says «local commits only; PLAN-35 is the owner's». The owner's standing instruction since PLAN-38 replaces that (P-19).

**Standing owner instruction on merges** (PLAN-38 to PLAN-59, PLAN-66 and PLAN-67): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-19).
- **Never ask the owner whether to merge:** his instruction is the permission. Asking cost PLAN-51 2 h 09 min.

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Check formatting on the files you changed (see «Lessons»).
4. Commit, using the phase's commit message.
5. Update **`reports/plan60-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan60-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan60-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `date -u +%Y-%m-%dT%H:%M:%SZ` (on Windows, `(Get-Date).ToUniversalTime().ToString('o')`).
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of:
  - the inventories, the image builds (the migrator's apart), the stack starts and the migration's first run;
  - each acceptance run (each AC-SEP-60 run apart, the second run on the same stack included);
  - the GHCR build and the deploy, and every GitHub Actions run.
- Record the time spent waiting for CI, and **any time the session stood still** (a stopped turn, the usage limit, the machine asleep or restarted, a full disk, a blocked port, GitHub), each with its cause.
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session in Claude Code at `xhigh`). PLAN-50 stopped once at the usage limit of the owner's plan. Economy is about tokens, never about doing less of the plan:
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only; root-causing separate failures.
- Run **one independent review**, before the push and the server phase, so that the server's images carry its fixes. Use a refuter only for findings rated high.
- If the owner switches the session's multi-agent mode on, use it for one read-only verification of the review's fixes, two agents at a time.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan60/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.
- Build the acceptance images once per code state. Run the unit and integration filters before a stack run, and rerun only the classes that failed.

**Long commands.** A foreground command that outlives the tool's time limit ends the turn. So:
- Run anything that can outlive the limit in the background or detached, with its log under `artifacts/plan60/`, and poll it. That covers the unit suite, the Testcontainers step, an image build, a stack start, each acceptance run, the local CI steps, the GHCR build and the server suites.
- **Keep every wait short: poll a background job with calls of about five minutes at most, never one call near the tool's limit.** PLAN-55 and PLAN-56 stopped three times during one long wait on a job (9 h 38 min, 7 min, 12 min), each ended only by the owner's «Try again». PLAN-57..59 polled in short calls and no turn ended on a wait.
- **A wait that returns is not the end of anything: the very next action is another wait or check, in the same turn.** A turn ends only with the final reply, `BLOCKED:` or `WAITING-CI:`.
- Run **one heavy job at a time**: never an image build beside a host test run or a browser check (P-2).
- Stop a process **by its id**, never by a pattern: PLAN-53's watch loop killed its own shell with a pattern that matched itself, and PLAN-59's `pgrep -f` matched its own shell once.

**Waiting — for CI and for your own work.**
- **Never end the turn to wait** for CI or for work you started (a review, a build, a stack, a long check). Wait for it in the turn, as PLAN-49, PLAN-50 and PLAN-57..59 did.
- **For CI:** run `gh pr checks <n> --watch --interval 60` in the foreground with a time limit of about five minutes. When it returns because of the limit, run it again, until every check has finished. A call that returns at the limit is not a refusal: run it again.
  - **For the GHCR build** (P9): `gh run watch <id> --exit-status` blocks until the run ends; give it the same limit of about five minutes and run it again, or poll `gh run view <id> --json status,conclusion`.
  - Do not lean on a background watch whose end may never wake you: PLAN-52 stood still 4 h 51 min after its run had finished.
- **Then act at once:**
  - every check green → merge (P-19), with no question to the owner;
  - a red check → read its log as soon as it fails, without waiting for the other checks; fix it on the release branch, push that branch only, and wait again the same way.
- **Only if this app truly cannot keep you in the turn.** PLAN-51 ended its turn after opening the PR, believing the app would not let it poll, and then asked the owner whether to merge: the merge waited 2 h 09 min. In that case:
  1. End the turn with `WAITING-CI:` (see below), and ask nothing.
  2. On the owner's next message, whatever it says, check **once** with `gh pr checks <n>`.
  3. Every check green → merge at once. A check still running → wait for it in that turn, as above.
  4. Record the path you took, with the times. Do not use GitHub's auto-merge.
- **A run that never starts:** if the PR's run stays queued with no jobs for 15 minutes, look at GitHub's status page. During an Actions incident such a run can be neither cancelled nor rerun. Close and reopen the PR to start a new run on the same HEAD, as PLAN-50 did, and record it.
- **The app's controls:** if the owner turns on **Auto-fix** for the PR, its wake-ups are welcome, but they do not replace your waiting. Never use **Merge when ready**: it squashes, and you merge with `gh pr merge <n> --merge`.
- **`WAITING-CI:`** ends the turn with a line that starts with `WAITING-CI:` and names the PR, the run id, its URL and the exact next step.
- Never merge on a guess.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior .NET engineer for HR systems whose decisions end people's employment and must be lawful, auditable and reversible only by rule:
- explicit state machines on the server, with optimistic concurrency and every transition attributed to a real actor;
- maker-checker through a workflow engine in another service: outcome events through an outbox, idempotent consumers, reconciliation sweeps, withdrawal races;
- numbered administrative decisions, clearance checklists with evidence, two-person exceptions, sagas with party acknowledgements;
- EF Core 10 with PostgreSQL: partial unique indexes, data migrations under forced row security, proved as a non-superuser migrator;
- ASP.NET Core MVC behind the YARP gateway: named policies, org-scope stances, antiforgery on every write;
- acceptance tests on Docker Compose and in CI stage jobs, with fixtures that never collide across test classes; PowerShell (ASCII, 5.1-compatible syntax, run by `pwsh` 7 here) and bash; the GHCR release path.

You work on the `HRManagement` repository on the owner's Linux notebook (P-2), signed in to GitHub with push rights, and you operate the demo server through `ssh hrm-vps`, as PLAN-36..59 did.

**Where things stand** when this prompt was written (2026-11-28):
- **`main` is `96c9594d`:** PLAN-59 merged through PR #60 at 2026-11-27T22:06:26Z.
  - Before the merge, its release branch took `a8a3b831`: the demo server's checks (`AcPay59VpsChecks`), and the cycle stage's limit by QA-066's rule (27 min). Its server results and its final report followed.
  - Its merge note is on the local branch `docs/plan-59-merge-note` of the owner's machine. Read it, and PLAN-59's final report.
- **The GHCR path is in force** (PR #58): the 17 release images are built in GitHub Actions and pulled by tag; **nothing is built on the server** (`docs/OPS_VPS_SINGLE_HOST.md`, «صور الإصدارات على GHCR»). The branch `ops/ghcr-release-images` and the worktree `/home/omar/HRManagement-ghcr`, if still there, are the owner's: never touch them.
- **The demo server runs PLAN-59's tag `8086ac8629af`** (deployed 2026-11-27T12:16Z, previous `25be64cf9fa8`): your rollback target, on GHCR.
  - every container healthy, and 47 GB free, after PLAN-59's checks; its last smoke 50 passed, 0 failed, 1 warning (the client-side port check);
  - its last backup is `/opt/hrm/backups/pre-plan59-20261127T115333Z`;
  - **what the earlier checks leave on every run** is listed in `docs/OPS_VPS_SINGLE_HOST.md` (among them PLAN-56's two cancelled runs of 2028-09, rejected requests, revoked delegations, PLAN-57's closed August 2026 of `HR-ATT`): never touch it;
  - **its separation data** came from the demo seed (`DemoContractsSeeder`) and from whatever its users did since: P0's picture says what the migration will meet there (P-17);
  - **its realm's people:** `hradmin`, `e2e-hr-admin`, `e2e-maker-contracts` (`hr-contracts`), `e2e-delegate-hr` (`hr-core`, PLAN-50), `e2e-checker-mgmt` (`hr-management-approver`), `e2e-compliance-checker` (`compliance`, PLAN-46) and the other e2e users. This plan adds none.
- **PLAN-59 took 16 h 47 min to its merge** (started 2026-11-27T05:19:45Z), with no stop: no stopped turn, no usage limit, no `WAITING-CI`.
  - **Its stretches:**
    - P0–P8 6 h 33 min; the GHCR build 14 min; the deploy 8 min 17 s; its own server checks 7 min;
    - **the older plans' server checks 9 h 16 min:** a first pass of 5 h 37 min stopped after a browser tour hung silently for 39 min, then 3 h 39 min class by class; 80 of 83 passed;
    - two CI runs on the PR, both green (289 and 227 job-minutes; 564 Actions job-minutes in all with the GHCR build).
  - **Its local waits:** a solution build 4 min 49 s; 16 service images one at a time 23 min 50 s; prod-like stacks 11 to 15 min to ready, one start failed with a segfault under memory pressure and passed when retried; the Testcontainers step 19 min 44 s; the Release build 2 min 24 s; the local CI steps 44 min 14 s; the unit suite 8 min 9 s (`--blame`, after a test host crash).
- **The owner's machine** (PLAN-53..59 progress files):
  - Linux 6.14, 15 GB RAM with a 4 GB swap; PowerShell 7.7 at `~/bin/pwsh`, with `~/bin/powershell` linked to it as CI has; .NET SDK 10.0.400;
  - `/` is 233 GB, with 9 to 50 GB free across the last seven plans. Docker also holds other projects' images, containers and volumes, and the owner's dev containers: never touch them;
  - images are built one service at a time from local base images; **the migrator image through `docker compose build`** (no buildx here, so the job's `docker build` cannot read `COPY --exclude`);
  - **`scripts/ci-local.ps1` cannot run there:** the same `scripts/ci/steps.json` runs natively with `scripts/ci/run-steps.sh --all` in a fresh clone of HEAD, every step not run named, and `scripts/plan035-prepush.ps1` accepts those by name;
  - **its compiler servers go stale** (Razor parse errors on unchanged files): run the local CI steps with `UseSharedCompilation=false`, `UseRazorBuildServer=false`, `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0`, and `dotnet build-server shutdown` after such a failure;
  - **the owner's dev container `hrm-keycloak` restarts about every minute:** Chromium then meets `ERR_NETWORK_CHANGED`, and stalls over HTTP/3. The browser checks launch Chromium without QUIC, probe page by page (`Ux067PageProbe.ProbeEachAsync`), and never run beside a build;
  - **its memory under a stack start:** PLAN-55's prod-like stack failed three starts at a load of 54 with the swap full; PLAN-59's segfaulted once with the swap full from other applications;
  - a full unit run was once aborted by a test host crash; it passed when rerun with `--blame`;
  - port 22 reached the server without the VPN, but refused this machine for 8 minutes after many ssh connections (PLAN-56); `kubectl` is absent (`Ac42i` fails locally only); the job's Chromium step uses `--with-deps` (sudo): use the Chromium in `~/.cache/ms-playwright`.
- **The side PRs** about the E2E Playwright install (#40, #42) and Dependabot's PRs, if still open: never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI today:**
  - a PR run has 16 checks (about an hour). One acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - the required checks on `main` are `CI`, `CodeQL`, `Gitleaks`, `E2E` and `Acceptance`. The last step of `Acceptance` («Every stage job succeeded») fails when the image build or any stage failed;
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - **the acceptance stage jobs** are the `stageJobs[]` of `tests/acceptance/ci-scope.json`, mirrored by the `acceptance-stage` matrix of `.github/workflows/ci.yml`, each with a time limit (QA-066's rule: about 1.5 times the measured duration):
    - `lab-main` 35 and `lab-rest` 30;
    - `prodlike` 55: the groups `journey`, `main`, `isolated` and `s2s42` on one stack;
    - `prodlike-cycle` 27 (PLAN-59's cycle);
    - `prodlike-final` 60: it waits for `lab-main`, `lab-rest`, `prodlike` and `prodlike-cycle`, and reads their folders;
    - `prodlike-policy` 25, `leastpriv` 20 and `meta` 60.
    Each job starts its own stack. **The acceptance project runs one class at a time,** in an order that changes from run to run;
  - **the prod-like groups:**
    - `journey`: the recruitment journey, which hires people into agency A before `main` runs;
    - `main`, 17 classes: `AcHcm25`, `AcWf43`, `AcTen48National`, `AcPol49EngineScope`, `AcIam50`..`AcIam53`, `AcPay54`..`AcPay56`, `AcPay58`, `AcLva57`, `Ac12`, `Ac21`, `Ac23` and `AcUx67`;
    - `isolated`, 7 classes, **each alone in its own test process** (`scripts/ci/acceptance-job.ps1`): `AcInt37`, `AcMsg40`, `AcWf44`, `AcWf45`, `AcWf46CrossService`, `AcPol47` and `AcOps39`;
    - `s2s42` (`AcS2s42`, `AcWf46PolicyOrphan`, with its overlays and recreated services), `cycle`, `final`, `pol49` and `leastpriv` (`AcOps38FirstBoot`, `AcTen48LeastPrivilege`);
  - **D-QA-20:** the evidence of record is the PR run's artifact `acceptance-evidence`. Nothing of a local run is committed;
  - **the build and test steps live in `scripts/ci/steps.json`;** `CiStepsParityTests` keeps `ci.yml` in line with it. The Testcontainers step's filter is `Category=Testcontainers&Category!=ObjectStorage`;
  - **red on `main` and not this plan's:** `scripts/pol049-validate.ps1` (28 findings) and `scripts/acceptance-baseline-validate.ps1`. Record them; do not chase them.
- **From PLAN-42..59** (their final reports and readiness notes):
  - **PLAN-42 (G-SEP-12, D-S2S-4):** the lifecycle gate.
    - `IEmployeeLifecycleGate` (`AssertCanStartSeparationAsync`, `AssertCanPublishSeparationAsync`) and `HttpEmployeeLifecycleGate` read HR.Core's `GET api/internal/employees/{id}/lifecycle-flags` with a service token, fail-closed.
    - That answer also carries `status` (`CurrentStatus.ToString()`), which the gate does not read yet.
    - Its messages: 404 «الموظف غير موجود في السجل المركزي.»; no answer «تعذّر التحقق من الحجز القانوني — لم يُنفَّذ الإجراء.»; a hold «الموظف تحت حجز قانوني (Legal Hold) — لا يمكن تنفيذ الفصل.».
  - **PLAN-43:**
    - `WorkflowPackSeeder.EnsureVersionAsync` publishes a new version of a definition that is already published, and **throws when there is none** («No published definition {code} to raise.»);
    - the validator `WorkflowDefinitionCatalog.Validate`, and the step's role enforced for every type;
    - `WorkflowSodRules`: the requester, the excluded approvers and the subject employee act on no step; `RequireDistinctApprovers` per definition;
    - a data change in a migration on a tenant table is invisible under forced row security to `*_migrator` (Lessons).
  - **PLAN-46, the cross-service contract** (`src/Shared/HRManagement.ServiceDefaults/Workflow/CrossService/`; its §8 and §12 are PLAN-60's notes):
    - `IWorkflowApprovalClient` (`StartAsync`, `CancelAsync`, `GetStatusAsync`), `WorkflowApprovalStartRequest`, the results of `WorkflowApprovalResults.cs` (`WorkflowApprovalStartResult`, `WorkflowApprovalCancelResult`, `WorkflowApprovalStatusDto`), `WorkflowApprovalErrorCodes` (with `MessageFor`), `WorkflowOutcomeGuard` (`ShouldApply`, `HasExcludedDecider`, the metric), `WorkflowApprovalSweepJob` through `AddWorkflowApprovalSweep<TStore>`, and `IWorkflowOutcomeDecided` (`HRManagement.MessageContracts/Events/`);
    - **`WorkflowApprovalSubmission`** holds the submission's rules for every owner: `SubmitAsync(client, submissionNo, WorkflowApprovalSubmitSteps { Request, LinkAndSaveAsync, SpendNumberAsync, IsLinkedToAsync, ApplyOutcomeAsync }, logger, ct)`, `WithdrawAsync` and `UseTenant`. A key whose instance was withdrawn before the record pointed at it is spent, and the next number is used;
    - `IWorkflowApprovalOwnerStore`: `EntityType`, `LeaderDatabase`, `LeaderLockKey`, `ListTenantsAsync`, `ListPendingAsync(submittedBefore, after, take)` paging on `(SubmittedAt, Id)`, and `ApplyOutcomeAsync`. The stores list their tenants from `AuditLogs`;
    - **the patterns to copy:** IDCard's `CardRequestApprovalStore`, `CardRequestApprovalService` and `CardRequestApprovalOutcomeConsumer`; Compliance's `DisciplinaryApprovalStore` and `DisciplinaryApprovalOutcomeConsumer`; `FakeWorkflowApprovalClient` (`src/Tests/HRManagement.TestSupport/Workflow/`);
    - **HR.Core's side of `SeparationRecord`:**
      - `ExternalApprovalTypes`: owner `ServiceClientIds.HrContracts`, codes `SEPARATION-APPROVAL` and `SEPARATION-APPROVAL-SENSITIVE`, default `SEPARATION-APPROVAL`, outcome `Generic`, audience `compliance`, prefix `/ui/hr-contracts/`;
      - `WorkflowGuardrails`: at least two steps, the last `hr-management-approver`, and the sensitive code with `compliance`;
      - `SEPARATION-WIZARD` is in `WorkflowPackSeeder.RetiredCodes`;
    - **HR.Contracts already calls `AddWorkflowApprovalClient`** (`Program.cs` about `:49`). Its `calls[]` in `deploy/wiring/service-wiring.json` already hold `hr-core.workflow-instances.start`, `.cancel` and `.external-status`. No sweep is registered;
    - **for every plan:** agency users see only their tenant's definitions (read national definitions as a national persona); an overlay class waits for the stack (`StackReadyAsync`) when it may run first in its stage; server checks need `VPS_TARGET_IP` with the domain variables.
  - **PLAN-50, the engine's order of checks** (`WorkflowApprovalEvaluator.GetBlockReasonAsync`): structure → the step's role (direct, then by delegation) → the separation-of-duties rules → the entity's gate → the initiator rule. So «a user without the role sees the role, a creator sees the SoD rule».
  - **PLAN-51 (D-51-2):** two policies per module, `{Module}.Write` and `{Module}.Approve`, with their roles from `Authorization:Policies:{name}:Roles`:
    - `HrContractsWrite` and `HrContractsApprove` are both Admin + `hr-contracts` by default (`HrManagementAuthorizationExtensions.cs` about `:186-191`);
    - every HR.Contracts POST names one (`WriteActionsHaveExplicitPolicyTests`);
    - the gateway asks `HrContractsApprove` on every mutating `hr-contracts` path outside `HireWizard` (`GatewayPathAuthorizationMiddleware.cs` about `:333-337`).
  - **PLAN-52:** both separation controllers are `[OrgScoped(typeof(SeparationRecord), OutOfScope = Forbid)]`; `OrgScopeCoverageTests`; `ContractPickList.InScopeAsync` for a restricted caller's contracts.
  - **PLAN-59:** the months' registry (`testdata/acceptance/payroll-months.json`); `AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync` before a payroll calculation on a shared stack; the gateway's rate limit of 2,000 requests a minute per user on the acceptance stacks.
- **The code today** (read on `96c9594d`; the plan was written on 2026-10-30, so find code by its content where a line moved):
  - **The record** (`HR.Contracts.Domain/Entities/SeparationRecord.cs`, 55 lines):
    - `EmployeeId`, `ContractId?`, `SeparationType`, `EffectiveDate` (`DateOnly`), `Reason`, `ApprovedBy` (`Guid?`, `:30`), `Documents`;
    - `SeparationEventPublished` (`:36`), `SourceLegalRegimeId`, `PayrollRegimeCode`, `IsCancelled` (`:47`), `CancelledAt`, `CancellationReason`;
    - **no status, no concurrency token.**
    - `CreatedBy` (`AuditableEntity`, `Guid?`) is stamped by `AuditSaveChangesInterceptor` from the signed-in user: an inbound consumer and the seed leave it null.
  - **The checklist** (`SeparationChecklist`): `Step`, `IsCompleted`, `CompletedAt`, `CompletedBy`, `EvidenceRef`, `SourceSystem`, `IsRequired`, `Notes`, `OwnerRole`, `SlaHours`, `CompletedByName`.
  - **The enums:**
    - `SeparationStep` has six values, stored as text, with a unique `(SeparationRecordId, Step)` index (`ContractsDbContext` about `:237-251`): ContractTerminated, PayrollSettled, CardRevoked, AccountDisabled, AssetsReturned, Archived;
    - `SeparationType` has seven, stored as text (about `:107-120`): Resignation, Termination, EndOfContract, Retirement, Medical, Judicial, Disciplinary;
    - `SeparationRecord` has no index but its key. `LeaveRequest` carries the `xmin` token (`ConfigureOptimisticConcurrency`, about `:135`).
  - **`SeparationChecklistDefaults`:** `DefaultSteps = Enum.GetValues<SeparationStep>()`, and `GovernanceFor` gives each item its owner role and SLA.
  - **`SeparationWorkflowRules`** (`HR.Contracts.Domain/Services/`):
    - `CanComplete`, `GetIncompleteRequiredSteps` and `SoftCancel`;
    - `CanCancel`: after publication, only while the effective date is in the future;
    - `IsEffectiveDateReached` (`:21-27`): both of the last two judge «today» in **UTC**.
  - **`SeparationRecordHandlers.cs`** (346 lines):
    - **create** (`:63-101`): the PLAN-42 gate first; then no check of the employee's status, of the contract, or of an open record; `ApprovedBy` from the DTO; a `ServiceTransaction` of type `Separation` with no link to the record;
    - **update** (`:103-122`): refused only once published or cancelled; it can change the employee and the contract;
    - **delete** (`:124-134`): soft-cancel, no reason asked;
    - **publish** (`:136-227`):
      - the effective date in UTC, then the hold gate, then all six items required (`:165-174`);
      - the regime's snapshot through `SeparationRegimeValidationService`, then `StartSeparationSagaAsync`;
      - the employee's latest `Separation` transaction pushed `Draft`→…→`Executing` with null actors (`:212-223`);
    - **complete an item** (`:230-276`): only `Guid.Empty` is refused as the actor (null passes). `ContractTerminated` calls `TerminateLinkedContractAsync` (`:255-257`);
    - **override an item** (`:278-312`): the second approver is a GUID from the form;
    - **cancel** (`:314-346`): a published record's saga is cancelled; the employee's latest transaction is moved with a null actor.
  - **The inbound path** (`ProcessSeparationRequestHandler`, `HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs`):
    - fed by `LegalJudgmentIssuedConsumer` (Judicial), `MedicalDecisionIssuedConsumer` (Medical) and `ResignationSubmittedConsumer` (Resignation);
    - the same holes as the create path;
    - a refusal by the hold gate throws, so the message stays in the broker's retry and error path.
  - **`SeparationRecordSupport.cs`:** `SeedChecklistAsync`, `TerminateLinkedContractAsync` and `ComputeAnnualLeaveRemainingAsync`.
  - **The controllers:**
    - `SeparationRecordsController` (248 lines) and `SeparationWizardController` (224 lines), both `ModuleHrContracts` and `[OrgScoped(typeof(SeparationRecord), OutOfScope = Forbid)]`;
    - `HrContractsWrite` on `Create`, `Edit`, `Delete`, `Step1`, `Step2CompleteStep`, `Step2Confirm` (a navigation guard that writes nothing) and `Step3Execute` (it sends `PublishSeparationCommand`);
    - `HrContractsApprove` on `PublishSeparation`, `Cancel` (no reason is posted) and `OverrideChecklistStep(id, step, reason, Guid approvedBy)`;
    - `Create` and `Step1` (`:85`) set `ApprovedBy` to the creator, and `Edit` keeps it.
  - **`ProcessInterventionsController`** (`api/process-interventions`, `[ApiController]`, `OrgScopeCentralOnly`):
    - `Resume` (`HrContractsWrite`) publishes `IResumeSeparationSaga`;
    - `Cancel` (`HrContractsWrite`) publishes `ICancelSeparationSaga`, which compensates an executing separation;
    - neither validates an antiforgery token.
  - **`AdministrativeDecisionsController`:** `Create`, `Edit` and `Delete` (`HrContractsWrite`) act on any decision.
  - **The decision's pattern** (`IssueAppointmentDecisionHandler`):
    - `CodeGenerationRequests.AdministrativeDecision("APPT")`; HR.Core's code policy gives `DEC-{TypeCode}-YYYY-#####`, sequenced per type code, so `SEP` needs no new policy;
    - its issue date from UTC;
    - once per transaction, and it marks the hire checklist's item.
  - **`ContractWorkflowRules`:** `Terminate(contract)` sets `Terminated` with no `EndDate`; `CanTerminate` accepts `Active` and `Pending`; `ValidateCanActivate` needs `Pending`, a signature, a commencement record and a passed probation when one is required.
  - **The service transaction** (`ServiceTransaction`):
    - no `SeparationRecordId`;
    - `MoveTo(next, byUserId, reason)` follows `AllowedTransitions` and writes a history row. That table has no `PendingApproval` → `Draft` and no `PendingApproval` → `Cancelled`;
    - `ServiceTransactionsController.Advance` (`HrContractsApprove`) moves any transaction by hand.
  - **The contract list** (`GetContractsLookupQuery`): the newest 500 contracts of every employee (PLAN-52's scope for a restricted caller), labelled `NameAr — EmployeeId`.
  - **The saga** (`HR.Contracts.Infrastructure/Sagas/SeparationStateMachine.cs`):
    - `SeparationSagaState` (table `SeparationSagaStates`, with `TenantId` and `SeparationRecordId`);
    - `CurrentState` takes `AwaitingParties`, `AwaitingIntervention`, `Compensating`, `Completed`, `Cancelled` or `Faulted`, and finished rows are kept;
    - the mandatory parties are `Payroll`, `HR.Core`, `IDCard`, `Auth.Directory`, `Admin.Logistics`, `IT.Infra` and `Records.Archive` (`ProcessParties`), with a 30-minute timeout;
    - `IExecuteSeparationParty` carries `Compensate`. `ISeparationPartyCompleted` does not.
  - **The consumers around the saga** (`HR.Contracts.Infrastructure/Consumers/SagaSupportConsumers.cs`):
    - `SeparationSagaFinalizedConsumer` (about `:56-94`) finds «the employee's latest `Separation` transaction»;
    - `SeparationPartyCompletedChecklistConsumer` (about `:97-119`) stamps evidence on `PayrollSettled` only;
    - `IProcessInterventionRequested` carries `SagaType`, `CorrelationId`, `Reason`, `FailedParty` and `TenantId`.
  - **Payroll's party** (`HandleSeparationCompletedHandler`, `Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs`):
    - the employee's active pay profile ends at the effective date and goes inactive;
    - an end-of-service settlement is created, and an adjustment of 0;
    - and a deduction, if advances are outstanding.
  - **Payroll on a new contract** (`HandleContractActivatedHandler`): a pay profile version from the activation. With no salary, or a salary outside the grade's bounds, it places a PAY029 review hold (a manual `PaymentHold`).
  - **The time:** `ICalendarService.ToLocalTime` (`ServiceDefaults/Localization`, `DamascusCalendarService`) is registered in HR.Contracts by `AddHrLocalization()` (`Program.cs:20`) and not used by the separation code yet.
  - **HR.Core's terminal statuses** (`EmploymentStatusRules.IsTerminal`, HR.Core.Domain): Terminated, Retired, Resigned, Deceased, Merged, Anonymized. HR.Contracts cannot reference HR.Core.Domain.
  - **HR.Core's seeds** (`HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs`):
    - `SeedAsync` inserts every code of its `Workflows` list that is missing, inside the tenant, **at every start**: `NationalSeedOrchestrator.ApplyIncrementalPatchesAsync` runs it on an existing database;
    - the list's ids run to `f0050017-…`, and no separation code is in it;
    - `DistinctApproverCodes` holds the two custody codes and `DISCIPLINARY-APPROVAL`;
    - `EnsureCrossServiceChainsAsync` (about `:359`) raises `ID-CARD-ISSUANCE` and `DISCIPLINARY-APPROVAL` when their latest version lacks the chain's roles.
  - **The engine's API:** `WorkflowInstancesController` (`api/workflow-instances`, class `HrCoreReadOnly`; `POST {id}/approve` and `{id}/reject` `WorkflowStepAction`); the inbox `ui/hr-core/WorkInbox`; the approval form `ui/hr-core/WorkflowApprovals/ApproveWorkflow/{id}`.
  - **The guard tests:**
    - `WorkflowDefinitionsHaveHandlersTests.cs:27` holds `["SeparationRecord"] = new("HR.Contracts", "SeparationApprovalOutcomeConsumer", PendingPlan: "PLAN-60")`; its type may publish no definition until the consumer exists;
    - `Wf046RegistryEnforcedTests.Every_seeded_code_of_a_registered_type_passes_the_validator` (`Wf046RegistryTests.cs`) skips the two separation codes as unseeded;
    - `AdvisoryLockKeys` runs to `PayProfileChangeActivation = 4_001_013` (the card's sweep `4_001_011`, the discipline's `4_001_012`).
  - **The sweep's settings:**
    - `WorkflowApproval:Sweep:IntervalMinutes` defaults to 10 and `StaleAfterMinutes` to 15;
    - `docker-compose.acceptance-prodlike.yml` sets 1 and 2 for `idcard-web` and `compliance-web` only;
    - `HR.Contracts.Web/appsettings.json` has no `Separation` section and no `WorkflowApproval` section.
  - **Row security:** HR.Contracts' tables are under forced row security (TEN002's `EnableRls`, IAM002's `EnableRlsOnAllTenantIdTables`): `SeparationRecords`, `SeparationChecklists`, `SeparationSagaStates`, `ServiceTransactions`, `ServiceTransactionStateHistories`, `AdministrativeDecisions`, `Contracts`…
    - the last HR.Contracts migration is `20261104012037_MSG040_InboxStateKeys`;
    - Compliance's `20261107211709_WF046_DisciplinaryApproval` lifts `FORCE` for one statement and puts it back as it was;
    - `Pay055MigrationPostgreSqlTests` proves a migration as a `NOSUPERUSER NOBYPASSRLS` owner.
  - **The seeds:**
    - `DemoContractsSeeder.SeedSeparationsAsync` (about `:473-520`): one record per employee with no current contract, effective 30–365 days ago, `ApprovedBy = EmployeeId(0)`, `SeparationEventPublished = n % 3 != 0`, every default item, some completed;
    - `AcceptanceSeedCatalog.TotalSeparations` = 86 (`AcceptanceV2CatalogTests:18`; `DATA-SEPARATION` in `scripts/acceptance-verify.ps1` about `:534`);
    - no duplicate, no `Guid.Empty` and no saga state in the seed.
  - **Personas** (`testdata/acceptance/personas.json`, 70):
    - `a-contracts-maker` and `a-contracts-checker` (`hr-contracts`, HQ 4 and 5);
    - `central-a` and `central-a2` (`hr-core`);
    - `a-management-approver` and `-2` (`hr-management-approver`);
    - `a-compliance` and `-2` (`compliance`).
    - No persona holds `hr-contracts` with `hr-core`, or `hr-core` with `hr-management-approver`. `a-leaver` does not exist.
    - `AcceptancePersonaCatalog.ApprovedRegularPersonas` (`src/Shared/HRManagement.ServiceDefaults/Seeding/`): AGENCY-A 49, AGENCY-B 8, AGENCY-C 4, the national tenant 5, with 4 negative fixtures; `DATA-AUTH` 70.
  - **The tests that touch separations** (P0 lists them all):
    - unit: `Fun002SeparationJourneyTests`, `Phase7_BusinessLogicDepthTests.cs`, `S2s042LifecycleGateTests`, `SeparationSagaCompensationTests`, `Wf043DefinitionValidatorTests`, `Wf043SodRulesTests`, `Wf046HrCoreSideTests`, `WorkflowStageGuardRoleTests`, `CrossTenantBoundedContextIsolationTests`;
    - integration: `ContractLifecycleIntegrationTests`, `EndToEndReleaseWorkflowSmokeIntegrationTests` (one of PLAN-59's fourteen), `Fun002SeparationRecordIntegrationTests`, `G3SeparationMidSagaFailureIntegrationTests`, `Int003ProcessSagaTests`, `Iam052ServiceScopeHttpTests`, `IdempotentConsumerTests`, `Ops038DemoDefectTests`, `OutboxAtomicityIntegrationTests`, `Plan034ScopedIndexTests`, `SoDForgedApproverTests`;
    - **the journey matrix:** `Fun002SeparationJourneyTests` reads `docs/fun-separation/journey-matrix.json`. Its nine items are SeparationDecision, ContractTerminated, PayrollSettled, CardRevoked, AccountDisabled, AssetsReturned, ITAccessRevoked, Archived and SeparationCompleted.
  - **The acceptance classes that create or publish separations:**
    - **AC-INT-37a** (`AcInt37ProductionWiringTests`, `isolated`): `Step1` for the first agency-A employee by `CreatedAt` with no leave, advance or separation, with no status filter and no contract;
    - **AC-INT-37e:** a Resignation for the law50 employee that `EmployeeAsync` prefers (`EmpBr1Id`), asserting one new record;
    - **AC-S2S-42h** (`s2s42`): `Step1`, then the checklist completed and the regime set by psql, then `PublishSeparation` without a hold, under a hold, and with `hr-core-web` stopped;
    - **AC-QA-34e** (`AcQa34PushableBaselineTests`, `meta`, CI only): it counts eleven integration tests by name in a trx, among them `ContractLifecycleIntegrationTests.PublishSeparation_*`, `Fun002SeparationRecordIntegrationTests.Publish_*` and `Int003ProcessSagaTests.DOM001_Publish_DoesNotAutoComplete_ContractTerminated`;
    - **`AcWf43VpsChecks`** (server): `["Published|t|12", "Retired|f|9"]`, and the distinct-approver codes `ASSET-ASSIGN-APPROVAL`, `ASSET-LOSS-APPROVAL` and `DISCIPLINARY-APPROVAL`;
    - the E2E separation tests (`SeparationJourneyE2ETests`) only open the wizard.
  - **Acceptance helpers:**
    - `CustodyJourney.ApproveFromInboxAsync(persona, instance, recorder)` (`AcWf45AssetApprovalLoopTests.cs:123`): it opens `WorkInbox`, finds the item and posts the form with the page's token. There is no reject helper yet;
    - `AcWf46CrossServiceApprovalTests.PublishOutcomeAsync(database, entityType, entityId, instance, outcome)`: a forged outcome on the bus, consumed for certain before it returns;
    - `AcceptanceMessagingHelper.ConnectBusAsync()`;
    - the RabbitMQ management API of `AcInt37ProductionWiringTests.RabbitClient()` (`api/queues/%2F`);
    - `AcS2s42TrustBoundaryTests`: `UiClientAsync(persona)`, `StopAsync(service)`, `StartAsync(service)` and `StackReadyAsync()`;
    - `Iam052PgServiceHost<TEntry, TContext>`: a service's `WebApplicationFactory` on PostgreSQL, without the gateway (`Category=Testcontainers`);
    - `EvidenceRecorder`, with the eight elements: `metadata.json`, `steps.md`, `http/`, `before.json`, `after.json`, `audit.json`, `db-checks.json` and `timing.json`, then `result.json`; `AcceptanceGate.RequireData`; `[RequiredAcceptance]`.
  - **The screens a leaver copy needs, all present:**
    - HR.Core: `Employees/Create` (`HrCoreWrite`), and `ServiceRegimeExceptions/Resolve` (`HrCoreWrite`), which classifies a record's service regime without SQL;
    - HR.Contracts: `Contracts/Create`, `ContractSignatures/Create`, `CommencementRecords/Create` and `Contracts/Activate`.
  - **The documents:**
    - `docs/FUN_002_SEPARATION_GAP_STUDY.md`: G-SEP-01 (about `:88`), G-SEP-04 (about `:139`) and G-SEP-11 (about `:267`) have a «الخطة» line and no «الحالة» line;
    - `docs/FUN_002_JOURNEY_MATRIX.md`, `docs/FUN_002_EXCEPTION_PATHS.md`, `docs/ADR-FUN-002-separation-journey-closure.md` and `docs/PROCESS_SAGAS.md`;
    - `docs/WF_001_WORKFLOW_ENGINE.md` (about `:160` and `:225`) names PLAN-60.
  - **STR-002:** the interim legal pack's exceptions expire on 2027-01-31, and the dashboard warns from 2026-12-02. After that date the payroll classes of the `main` group stop calculating (`PayrollInterimGate`) until the owner renews them.
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43). **This plan's migration changes data:** lift `FORCE` inside it for each statement, on each table it reads or writes, as Compliance's `WF046` does, and prove it as the migrator (P-5).
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role.
  - **A new PostgreSQL test uses the connection strings of `PostgreSqlFixture`,** whose idle connections close after seconds. Make sure CI's Testcontainers filter picks up your new classes.
  - **The prod-like stack runs `ASPNETCORE_ENVIRONMENT=Acceptance`,** so it does not load `appsettings.Production.json`. A value in a Production file must be mirrored in `docker-compose.acceptance-prodlike.yml`. The `Separation` section goes in `appsettings.json`, which every environment reads.
  - **Personas:**
    - a persona change needs `scripts/acceptance-reset.ps1` before the first start (the personas' hash is in the seed pack's fingerprint);
    - **never write `personas.json` while an image is building** (PLAN-53 built one image again for it);
    - a new persona also changes `DATA-AUTH` and the tests that read `personas.json`; a user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh`.
  - **The classes of an acceptance group run one at a time in an order that changes from run to run.**
    - A class never relies on what another class did or did not do, and leaves nothing that changes another class's work;
    - before any payroll calculation on a shared prod-like stack, the payroll classes call `ClassifyRecordsAddedSinceTheSeedAsync`; a leaver copy classifies its own regime through the screen (P-12).
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - **Formatting:** before every commit, run `dotnet format whitespace HRManagement.slnx --verify-no-changes --no-restore --include <the files you changed>`. CI runs the full check.
  - **Acceptance tests:** a test posts the URL and the token the page renders (PLAN-44); an API write needs the antiforgery token (PLAN-47, PLAN-53).
  - **CI builds Release, where a nullable warning is an error** (PLAN-58's `bc4703ce`): run `dotnet build HRManagement.slnx -c Release -warnaserror` before the push.
  - **The stage jobs' limits follow QA-066's rule** (PLAN-58's first PR run hit the prod-like stage's 35 minutes): a class added to a group adds its time; measure and set the limit.
  - **CodeQL runs in CI only:**
    - a new POST or PUT without `[ValidateAntiForgeryToken]` fails `cs/web/missing-token-validation` (PLAN-53). So does an existing one that starts to write something new (PLAN-56): give it the token check, and update its clients;
    - a value whose name reads as sensitive (account, password, secret, token, national number) that flows into a log, an audit text or a stored field fails `cs/cleartext-storage-of-sensitive-information`, even when it is an opaque key (PLAN-55). Name an identifier by what it is;
    - a log line that writes a value built from the request is `cs/log-forging`;
    - never answer CodeQL with a baseline entry or an ignore attribute.
  - **Gitleaks reads every commit of the branch:** a literal that looks like a key fails the check even if a later commit removes it (PLAN-56). Never write such a literal; never rewrite history after a push.
  - **The outbox guard matches a marked method by its name** (PLAN-57's `CloseAsync`): two methods with one name confuse it. The execution saves the contract, the status and the saga's message in one outbox unit (P-10).
  - **The machine** (P-2): the disk (PLAN-53 filled it once); its memory under a stack start (PLAN-55, PLAN-59); stale compiler servers; never delete build outputs while a step uses them; helpers outside the tree; never change `~/.ssh/config` in the middle of a run; after the host sleeps, the Docker VM's clock may lag (PLAN-50).
  - **The session may stand still.** Keep the progress file's next step exact, and go from the pre-push check straight to the push and the PR, in the same turn.
  - **The server:**
    - **the GHCR path** (PLAN-58): images are built in GitHub Actions from a pushed branch, so the push comes before the server phase; nothing is built on the server; a rollback pulls an earlier tag from GHCR;
    - **keep ssh connections few:** never poll in a tight ssh loop (PLAN-56's 8-minute refusal);
    - **run the older server checks class by class, and the browser tours one at a time, each under `--blame-hang-timeout 20m`** (PLAN-59: a tour that hung silently cost 39 minutes and a rerun of 27 checks);
    - **`Server_services_log_the_repository_legal_pack_fingerprint` reads a start-up line:** run it right after the deploy, before any check restarts a service (logs rotate at 3 × 10 MB; PLAN-59 lost the line to an earlier restart);
    - **a deploy may stop at `keycloak-reconcile` after PostgreSQL is recreated** (PLAN-40, PLAN-54, PLAN-56), leaving the application containers created and not started: run the same deploy again at once;
    - the server suites write into the committed `acceptance-evidence/` and `reports/`: restore those files, or move them under `artifacts/plan60/vps/` (PLAN-48);
    - a server check cleans up what it leaves open, and decodes a page before it compares Arabic text (PLAN-49);
    - **Production and the demo keep the password grant closed:** a script that signs in there uses a signed-in session's cookie or a bearer token (PLAN-55);
    - **when a plan changes what an older server check counts or who may act, that check is adapted, its meaning kept** (PLAN-58's `64dfffff`, P-13's `AcWf43VpsChecks`);
    - in a psql concatenation, `||` with a `NULL` gives `NULL`: use `coalesce` (PLAN-58);
    - older server checks that fail or flicker from this machine only (PLAN-55..59). Record them, rerun them alone, and do not chase them:
      - `AC_POL_47_vps_the_migration…` (it reads a file lost with the formatted Windows machine);
      - `AC_WF_46` (it reads a 5,000-line tail of a log that grows fast under the checks): it passes alone just after a sweep cycle;
      - `AC_OPS_39_vps_e_Keycloak…`;
      - the browser tours, on `ERR_NETWORK_CHANGED` (`AC_UX_67_vps_every_key_page…` hung three times in PLAN-59).
  - **Before the push, and before any fix pushed to the release branch:**
    - run the unit suite (its tests scan every source file) and the format check. After a test host crash, rerun it with `--blame`;
    - compare the seeded counts of `scripts/acceptance-verify.ps1` on your local stack, by psql;
    - run the full Testcontainers step on the host with CI's filter.
  - **The E2E suite runs in CI only.**
  - **The architecture project's first run after a build** takes about 2 min 25 s, outside the tests (PLAN-51).
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools, or a script that reads and writes UTF-8 without a byte order mark.
  - The acceptance services have no container health check: readiness means `/health/ready`.
  - Damascus is UTC+3 all year: between 21:00 and 24:00 UTC its date is already tomorrow's. Every date this plan judges or a test computes is Damascus's (`Asia/Damascus`).
  - In this prompt's tables, `\|` stands for `|`.

## Read first (mandatory)

1. **The specification:**
   - `docs/acceptance-plans/PLAN-60-sep-unblock-decision-governance.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
   - `docs/FUN_002_SEPARATION_GAP_STUDY.md`: G-SEP-01, G-SEP-04, G-SEP-11, and G-SEP-12 (the lifecycle gate this plan extends).
   - `docs/acceptance-plans/PLAN-46-wf-cross-service-approvals.md` §1: the contract.
2. **These reports:**
   - `reports/plan59-final-report.md` (§3, §12, §14, §15) and its merge note;
   - `reports/plan46-final-report.md` (§8 «PLAN-60 alignment» and §12 «Readiness note»);
   - `reports/plan42-final-report.md` (the gate), `reports/plan51-final-report.md` (D-51-2), `reports/plan52-final-report.md` (HR.Contracts' scope).
3. **These documents:**
   - `docs/FUN_002_JOURNEY_MATRIX.md`, `docs/FUN_002_EXCEPTION_PATHS.md`, `docs/ADR-FUN-002-separation-journey-closure.md`, `docs/PROCESS_SAGAS.md`, `docs/WF_001_WORKFLOW_ENGINE.md`;
   - the rows D-SEP-4, D-SEP-6, D-WF-1, D-51-2, D-S2S-4 and D-QA-20 of `docs/OWNER_DECISIONS_REGISTER.md`.
4. The code named in «The code today».
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md` (the GHCR path).

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **No other plan session is running,** and you are in the main folder (see «How to run»), on `main` or on `docs/plan-59-merge-note`.
2. **`git fetch origin`**, and `origin/main` holds PLAN-59: the merge of `release/plan-59` is in `git log origin/main --merges --oneline -5`.
3. **The working tree is clean,** except for these, which may be present:
   - the owner's local edits to `global.json` and `tmp-vendors.html`, if any;
   - git-ignored files (`artifacts/`, `.env`, the local key rings of the dev scripts);
   - untracked prompt files under `docs/acceptance-plans/execution/`, this one included.

   Anything else uncommitted: leave it untouched, record it, and go on if it does not touch this plan's files. `BLOCKED:` only if it does. Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
4. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
   - **If port 22 is blocked** from the owner's network, this precondition does not stop P0. Record it, take the server's read-only picture at the start of P9, and go on with the local phases.
5. **`gh auth status`** shows an account with push rights to `omardefdev/HRManagement` and the `repo` and `workflow` scopes (P9 starts `deploy.yml`).

## Decisions — already taken (no owner questions)

The owner accepted the plan's defaults on 2026-10-31 (the wave 1 survey; the rows of `OWNER_DECISIONS_REGISTER.md`). If the owner changed one in a message before P0 starts, his message wins; record it.

- **D-SEP-4, the approval chains:**
  - `SEPARATION-APPROVAL`: step 1 `hr-core` (HR's review), step 2 `hr-management-approver` (the authority);
  - `SEPARATION-APPROVAL-SENSITIVE`, for the types in `Separation:Approval:SensitiveTypes` (default `Disciplinary`, `Judicial`): `hr-core` → `compliance` → `hr-management-approver`;
  - the creator is excluded from every step.
- **D-SEP-6 (its part here):** the execution is manual, a «تنفيذ» button usable once the effective date is reached in Damascus's time. A scheduled execution is wave 2 (SEP-G). Cancelling an executing separation is PLAN-63's.
- **The plan's other defaults,** each behind a key of the `Separation` section of `HR.Contracts.Web/appsettings.json`:
  - the decision's issuer is any holder of `HrContractsApprove` but the creator (`Separation:Decision:IssuerMustDifferFromCreator=true`);
  - notifying the employee is a condition of execution: a manual item `EmployeeNotified` with evidence (`Separation:Checklist:RequireEmployeeNotification=true`);
  - a cancellation in this plan only from `Draft`, `PendingApproval` and `Approved`.
- **D-WF-1:** every decision that ends a person's service passes an approval on the server.
- **D-51-2:** the module's two policies and their configurable roles (P-10).
- **D-S2S-4:** a lifecycle check that cannot answer refuses (P-6).
- **D-QA-20:** the evidence of record is the PR run's artifact.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-60-sep-unblock-decision-governance` from `origin/main` (plan 0.2), in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-59's merge note from `docs/plan-59-merge-note`. If that branch is gone, rebuild the record from GitHub (PR #60, its runs, the merge commit `96c9594d`), and say so;<br>&nbsp;&nbsp;– commit this prompt file. If the owner pasted it into the session rather than saving it, write it from the session's text first, as PLAN-54..59 did;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **The machine (Linux): awake, the disk, the memory, Docker, the ports.**<br>• **Keep the machine awake for the session, changing no setting:** `systemd-inhibit --what=sleep:idle --who=plan60 --why="PLAN-60" sleep infinity`, detached. Record its PID in `artifacts/plan60/keepawake.pid`, and stop it **by that PID** at the end. If the machine restarts, start it again and record it.<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• **Remove only this repository's leftovers:** compose projects `hrm-*`, Testcontainers, this plan's images and this repository's build cache. Never prune, stop or remove another project's images, containers or volumes, or the owner's dev containers.<br>• **The disk:** read `df -h /` and `docker system df` before every image build and every stack start.<br>&nbsp;&nbsp;– Build an image only with at least 12 GB free. Below that, prune this repository's build cache and remove this plan's unused images first;<br>&nbsp;&nbsp;– a watch that stops a build near 2 GB free stops it **by its PID**;<br>&nbsp;&nbsp;– if 12 GB cannot be reached without touching other projects: `BLOCKED:`, one line — the owner frees space.<br>• **The memory, before a stack start:**<br>&nbsp;&nbsp;– nothing heavy beside it: no build, no test host, no browser, no idle stack of this repository;<br>&nbsp;&nbsp;– read `uptime` and `free -m` first, and start only when the load is below the number of CPUs;<br>&nbsp;&nbsp;– a start that fails from the load (a health check timing out, a 401 during the identity sync, a segfault) is retried after the load falls, three times at most, each failure and its cause recorded;<br>&nbsp;&nbsp;– wait for each start in the turn, in short polls («Long commands»).<br>• **The images:** one service at a time from the local base images (`acceptance-up.ps1 -UseLocalBaseImages`, and the helpers PLAN-53..59 kept outside the tree).<br>&nbsp;&nbsp;– This plan changes HR.Contracts (all four layers and a migration), HR.Core (the seeds), ServiceDefaults, SharedKernel and the tests;<br>&nbsp;&nbsp;– build every service image once from this tree, and **the migrator image** (a migration is added) through `docker compose build`;<br>&nbsp;&nbsp;– then rebuild only the images a fix reaches. Never commit a helper.<br>• **One heavy job at a time:** never an image build beside a host test run or a browser check. Close what you opened before a full unit run (a browser, an idle stack).<br>• **After an image build,** check that every service image and the migrator were rebuilt from this tree. A failed build leaves stale images, and a `-NoBuild` run on them proves nothing.<br>• **Before each stack start,** check the stack's host ports with `ss -ltn` and `docker ps`. A port held by something that is not this repository's: `BLOCKED:` with the port.<br>• **The local CI steps:** `scripts/ci/run-steps.sh --all` natively in a fresh clone of HEAD, with the compiler servers off (see «Where things stand»), naming each step not run and why.<br>• **If this session runs on Windows again,** use PLAN-67's forms of these checks, and record it. |
| P-3 | **The baseline (plan 0.1, 0.3–0.5),** into `reports/plan60-baseline.md`.<br>• **0.1, PLAN-46's contract by name:** `IWorkflowApprovalClient`, `IWorkflowOutcomeDecided`, `WorkflowOutcomeGuard`, `WorkflowApprovalSweepJob` with `AddWorkflowApprovalSweep<TStore>`, `IWorkflowApprovalOwnerStore`, `WorkflowApprovalSubmission`, `FakeWorkflowApprovalClient`, and the registry entry `SeparationRecord`. A name that differs gets a thin adapter, recorded in the progress file. A piece that is missing: Stop.<br>• **0.3:** `dotnet build HRManagement.slnx -warnaserror`.<br>• **0.4, the plan's filters, widened to every test that touches separations:**<br>&nbsp;&nbsp;– `dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Fun002\|FullyQualifiedName~Separation\|FullyQualifiedName~DOM001\|FullyQualifiedName~S2s042\|FullyQualifiedName~Wf043\|FullyQualifiedName~Wf046\|FullyQualifiedName~WorkflowDefinitionsHaveHandlers\|FullyQualifiedName~WorkflowStageGuardRole\|FullyQualifiedName~Iam051"`;<br>&nbsp;&nbsp;– `dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Fun002\|FullyQualifiedName~Int003\|FullyQualifiedName~G3Separation\|FullyQualifiedName~ContractSeparationChain\|FullyQualifiedName~ContractLifecycle\|FullyQualifiedName~EndToEndReleaseWorkflow\|FullyQualifiedName~SoDForgedApprover\|FullyQualifiedName~OutboxAtomicity\|FullyQualifiedName~IdempotentConsumer"`, then once more with `RUN_TESTCONTAINERS=true` and `Iam052ServiceScope` added;<br>&nbsp;&nbsp;– the architecture tests `WriteActionsHaveExplicitPolicyTests` and `OrgScopeCoverageTests`.<br>&nbsp;&nbsp;Record each class with its result, and whatever is red with its cause.<br>• **0.5, the data:**<br>&nbsp;&nbsp;– the seed's, read from `DemoContractsSeeder` (deterministic: 86 records, a third unpublished, no duplicate, no `Guid.Empty`, no saga state). On a fresh stack the migration meets empty tables and the seeder writes the statuses by the backfill's rules (P-13): the first local stack shows them (P6);<br>&nbsp;&nbsp;– the demo's, from P-17's picture. These numbers feed the migration's tests (P-5).<br>• **From this prompt,** generated by search into `artifacts/plan60/inventory.*`, never typed, and summarised:<br>&nbsp;&nbsp;– **the writers:** every path that sets `SeparationEventPublished`, `IsCancelled` or `ApprovedBy` on a `SeparationRecord`, calls `StartSeparationSagaAsync` or `CancelSeparationSagaAsync`, or moves a `Separation` transaction; and every reader of `SeparationEventPublished` (rule 3 keeps the column for them);<br>&nbsp;&nbsp;– **the entries:** every action, handler and consumer that creates a separation record;<br>&nbsp;&nbsp;– **the POSTs:** each POST of `SeparationRecordsController`, `SeparationWizardController`, `AdministrativeDecisionsController` and `ProcessInterventionsController`, with its policy, its stance and its antiforgery check;<br>&nbsp;&nbsp;– **the tests:** every test, acceptance class, server check and script that creates, publishes, completes an item of, overrides or cancels a separation, reads its flags, or counts workflow definitions (P-13);<br>&nbsp;&nbsp;– **HR.Core's side:** the registry entry, the guardrail, the inbox's audience, and the tests of the exclusion rule;<br>&nbsp;&nbsp;– the demo's picture (P-17). |
| P-4 | **The state machine (plan §1).**<br>• **`SeparationStatus`** (`HR.Contracts.Domain/Enums/`): `Draft`, `PendingApproval`, `Approved`, `Rejected`, `Executing`, `AwaitingIntervention`, `Completed`, `Cancelled`, each with its Arabic `[Display]` name, stored as text.<br>• **The record's new fields:** `Status`, `WorkflowInstanceId`, `WorkflowCode`, `SubmissionNo`, `SubmittedBy`/`At`, `DecidedBy`/`At`, `RejectionReason`, `DecisionId` (a foreign key to `AdministrativeDecision`), `ExecutedBy`/`At`, `CompletedAt`, and a mark for the records the backfill set to `Draft` (P-5). `ApprovedBy` stays for history and is no longer written by any code.<br>• **`SeparationWorkflowRules`:** an explicit table, with `CanTransition(from, to)`, `Transition(record, to, actor, reason)` and `IsOpen(status)`:<br>&nbsp;&nbsp;– `Draft` → `PendingApproval` \| `Cancelled`;<br>&nbsp;&nbsp;– `PendingApproval` → `Approved` \| `Rejected` \| `Draft` (withdrawn) \| `Cancelled`;<br>&nbsp;&nbsp;– `Rejected` → `Draft` (reopened) \| `Cancelled`;<br>&nbsp;&nbsp;– `Approved` → `Executing` \| `Cancelled`;<br>&nbsp;&nbsp;– `Executing` ↔ `AwaitingIntervention`, and `Executing` → `Completed`.<br>• **`Status` changes only through `Transition`,** which refuses `PendingApproval` without an instance id. A guard test finds any other assignment (P-10).<br>• **`Executing`, `AwaitingIntervention` and `Completed` are never cancelled here:** «إلغاء فصل قيد التنفيذ يتطلب مسار التعويض (PLAN-63)».<br>• **`SeparationEventPublished` is derived:** `true` from `Executing` on. The column stays, for the consumers that read it.<br>• **`SeparationStatusSync`** (`HR.Contracts.Application/Features/SeparationRecords/`) moves the linked transaction with each transition:<br>&nbsp;&nbsp;– it maps each status onto the transaction's own allowed transitions (a withdrawal through `InProcess`, a reopening through `InProcess`, a cancellation after `PendingApproval` through `InProcess`);<br>&nbsp;&nbsp;– it adds a row to `AllowedTransitions` only if a status cannot be reached otherwise, with a test that the other transaction types' paths are unchanged.<br>• **Every transition writes a history row with its actor:**<br>&nbsp;&nbsp;– a person's act names that person;<br>&nbsp;&nbsp;– a consumer's names the event's `DecidedByUserId` when the event has one;<br>&nbsp;&nbsp;– the saga's end and an intervention name none, with a reason naming the system.<br>• **The automatic `Draft`→…→`Executing` push in the publication goes.** `SeparationRecord` takes the `xmin` token (`ConfigureOptimisticConcurrency`, as `LeaveRequest`).<br>• **`Sep060SeparationStateMachineTests`:** every allowed pair and every refused one, the message of each refusal, and the derived flag. |
| P-5 | **The migration and its backfill** (plan §6, with §1's token).<br>• **`HR.Contracts.Infrastructure/Migrations/<ts>_SEP060_SeparationStatusGovernance.cs`,** with no byte order mark. Every new column is nullable or has a default (so the previous tag can still write: see the last bullet).<br>• **The columns:**<br>&nbsp;&nbsp;– P-4's fields on `SeparationRecords`;<br>&nbsp;&nbsp;– on `SeparationChecklists`: `CompletionMode`, `PartyName`, `ExceptionRequestedBy`/`At`/`Reason` and `ExceptionApprovedBy`/`At`;<br>&nbsp;&nbsp;– on `ServiceTransactions`: `SeparationRecordId`, with a unique index where it is not null.<br>• **The backfill, deterministic SQL, in this order:**<br>&nbsp;&nbsp;1. `IsCancelled` → `Cancelled`.<br>&nbsp;&nbsp;2. A published record, by its saga's `CurrentState` (`SeparationSagaStates.SeparationRecordId`): `Completed` → `Completed`; `AwaitingIntervention` → `AwaitingIntervention`; `Cancelled` → `Cancelled`; anything else (no row, `AwaitingParties`, `Compensating`, `Faulted`) → `Executing`.<br>&nbsp;&nbsp;3. The rest → `Draft`, marked, so the list shows «سجل قديم — يتطلب اعتماداً» until its first submission.<br>&nbsp;&nbsp;4. `EmployeeId = Guid.Empty` → `Cancelled`, with a reason naming PLAN-60: a record with no employee can never be approved.<br>&nbsp;&nbsp;5. **Open duplicates per `(TenantId, EmployeeId)`:**<br>&nbsp;&nbsp;&nbsp;&nbsp;– keep the newest record whose saga started, else the newest;<br>&nbsp;&nbsp;&nbsp;&nbsp;– cancel the rest with «PLAN-60: سجل مكرر»;<br>&nbsp;&nbsp;&nbsp;&nbsp;– **never cancel a record whose saga started.** Two such records of one employee: the migration stops with their ids (`RAISE EXCEPTION`) rather than cancel either, and so do you: Stop (P0's picture tells, before any deploy).<br>&nbsp;&nbsp;6. **The checklist:** every row gets its `CompletionMode` and `PartyName` (P-9). The four new items are added to every open record.<br>&nbsp;&nbsp;7. **The transactions:** each record is linked to the nearest `Separation` transaction of its employee created within 5 minutes of it, each transaction linked once. What stays unlinked is `sep060-report-unlinked.ps1`'s (P-15).<br>&nbsp;&nbsp;8. Then the indexes: `IX_SeparationRecords_Tenant_Employee_Open` on `(TenantId, EmployeeId)` where `Status` is one of `Draft`, `PendingApproval`, `Approved`, `Executing` or `AwaitingIntervention`.<br>• **Forced row security:**<br>&nbsp;&nbsp;– each statement lifts `FORCE` on each table it reads or writes (`SeparationRecords`, `SeparationChecklists`, `SeparationSagaStates`, `ServiceTransactions`), inside the migration's transaction;<br>&nbsp;&nbsp;– and puts it back exactly as it was (Compliance's `WF046` is the model).<br>• **`Sep060MigrationPostgreSqlTests`** (`Category=Testcontainers`), on `Pay055MigrationPostgreSqlTests`' model:<br>&nbsp;&nbsp;– a `NOSUPERUSER NOBYPASSRLS` owner, forced row security, two agencies;<br>&nbsp;&nbsp;– planted rows for each rule: the four flag and saga combinations, a duplicate with and without a started saga, `Guid.Empty`, transactions inside and outside the 5 minutes;<br>&nbsp;&nbsp;– it asserts each rule's result, the indexes, and `FORCE` back on.<br>• **`Sep060SeparationPostgresIntegrationTests`** (the plan's four):<br>&nbsp;&nbsp;– the partial index refuses a second open record, and accepts one after a cancellation;<br>&nbsp;&nbsp;– of two concurrent submissions, `xmin` lets one through;<br>&nbsp;&nbsp;– the execution saves the contract's end, `Executing` and the `IStartSeparationSaga` outbox row together, or nothing;<br>&nbsp;&nbsp;– the backfill on planted data.<br>• `dotnet ef migrations has-pending-model-changes -p src/Services/HRContracts/HR.Contracts.Infrastructure -s src/Services/HRContracts/HR.Contracts.Web` is clean.<br>• **After a rollback** (P-18):<br>&nbsp;&nbsp;– **`Sep060RollbackReadingTests`** (PostgreSQL) proves that PLAN-59's tag can run on this schema: an insert with only the columns its model writes succeeds, and its reads return the same rows;<br>&nbsp;&nbsp;– that tag moves only the old flags. When this tag starts again, **`SeparationStatusReconciler`** puts back in line what it changed: inside each tenant, idempotent, like `WorkflowPackSeeder.BackfillGovernanceAsync`. It applies rules 1, 2, 6 and 7 to the records whose flags and status disagree, and it is tested. |
| P-6 | **The record's integrity** (plan §6).<br>• **`SeparationRecordFactory`** (`HR.Contracts.Application/Features/SeparationRecords/`): one creation path for `Step1`, `Create` and `ProcessSeparationRequestHandler`. It checks, seeds the checklist, then creates the transaction already linked (`SeparationRecordId`), in one save.<br>• **The employee:**<br>&nbsp;&nbsp;– through PLAN-42's gate, in the same call that reads the hold: `lifecycle-flags` already answers `status`;<br>&nbsp;&nbsp;– the employee exists, and his status is not terminal. The terminal names live in SharedKernel, with a test that they equal HR.Core's `EmploymentStatusRules.IsTerminal`;<br>&nbsp;&nbsp;– no answer refuses (D-S2S-4), with PLAN-42's messages for a missing employee and a failed check, and an Arabic message naming the status for a terminal one.<br>• **No second open record:** checked before the save; the index is the last word, its violation answered «يوجد سجل فصل مفتوح لهذا الموظف».<br>• **The contract:**<br>&nbsp;&nbsp;– `ContractId` must be a contract of the same employee, `Active` or `Pending`;<br>&nbsp;&nbsp;– an employee with one such contract has it chosen for him; with several, the choice is required; with none, the record has no contract.<br>• **`GetEmployeeContractsLookupQuery`** (`Features/Lookup/ContractLookupHandlers.cs`): the chosen employee's `Active` and `Pending` contracts only, inside PLAN-52's scope. It replaces the general list in the separation forms; the general query stays for its other users.<br>• **The substance is locked** (`UpdateSeparationRecordHandler`):<br>&nbsp;&nbsp;– edits only in `Draft`;<br>&nbsp;&nbsp;– once any item is completed or excepted, `EmployeeId` and `ContractId` no longer change;<br>&nbsp;&nbsp;– `SeparationEventPublished` and `ApprovedBy` are never taken from a form.<br>• **The link:** every lookup of a separation's transaction goes by `SeparationRecordId`. `ServiceTransactionsController.Advance` refuses a `Separation` transaction: «الحالة يملكها سجل الفصل».<br>• **The inbound path:**<br>&nbsp;&nbsp;– through the factory: the same employee, contract and open-record checks;<br>&nbsp;&nbsp;– a refusal throws, as PLAN-42's hold does, so the broker's retry and error path keeps the message, neither lost nor acted on;<br>&nbsp;&nbsp;– the existing check of the same request already recorded (employee, date, type, not executed) stays its idempotency;<br>&nbsp;&nbsp;– its record has no `CreatedBy` (P-7).<br>• **Tests:** `Sep060RecordIntegrityTests` (each refusal, the contract rule's three cases, the lock, the factory used by all three entries) and the gate's new branch. |
| P-7 | **The approval through HR.Core's engine** (plan §2).<br>• **`SeparationApprovalRouting`** (`HR.Contracts.Application/Services/`): a type in `Separation:Approval:SensitiveTypes` (default `Disciplinary`, `Judicial`) → `SEPARATION-APPROVAL-SENSITIVE`; any other → `SEPARATION-APPROVAL`.<br>• **`SubmitSeparationCommand`** (`SeparationApprovalHandlers.cs`), through `WorkflowApprovalSubmission.SubmitAsync` and `IWorkflowApprovalClient.StartAsync`:<br>&nbsp;&nbsp;– it checks P-6 again first;<br>&nbsp;&nbsp;– `IdempotencyKey` `SeparationRecord:{id}:{submissionNo}`, so a reopening after a rejection takes a new number;<br>&nbsp;&nbsp;– `SubjectEmployeeId` = the employee; `OrgUnitId` = his unit when HR.Core's lookup gives it;<br>&nbsp;&nbsp;– `DisplayTitle` «إنهاء خدمة — <الاسم> — <النوع>», with no national number; `DetailsUrl` `/ui/hr-contracts/SeparationRecords/Details/{id}`;<br>&nbsp;&nbsp;– `PendingApproval` is saved only with its `InstanceId`, in the same save. A failure keeps `Draft`, with the Arabic message of its `ErrorCode` (`WorkflowApprovalErrorCodes.MessageFor`, `Conflict` and `Invalid` among them).<br>• **`ExcludedApproverUserIds` = the creator and the submitter:**<br>&nbsp;&nbsp;– the submitter comes from the identity, never from a form; a submission with no user id is refused;<br>&nbsp;&nbsp;– the creator, when the record has one. A record no person created (inbound or seeded, `CreatedBy` null) excludes the submitter alone, and the subject through `SubjectEmployeeId`;<br>&nbsp;&nbsp;– the Details page names who created the record, or its source.<br>• **`WithdrawSeparationCommand`,** through `WorkflowApprovalSubmission.WithdrawAsync`:<br>&nbsp;&nbsp;– `CancelAsync`, and on success `PendingApproval` → `Draft`;<br>&nbsp;&nbsp;– on `AlreadyTerminal`, no return to `Draft`: its `FinalOutcome` is applied through `ApplyOutcomeAsync`.<br>• **`SeparationApprovalOutcomeConsumer`** (`HR.Contracts.Infrastructure/Consumers/`):<br>&nbsp;&nbsp;– it takes `EntityType == "SeparationRecord"` only;<br>&nbsp;&nbsp;– `WorkflowOutcomeGuard.ShouldApply` refuses, logs and counts an outcome whose instance is not the record's, or a record that is not `PendingApproval`;<br>&nbsp;&nbsp;– `HasExcludedDecider` refuses an `Approved` whose decisions name the creator or the submitter;<br>&nbsp;&nbsp;– then `Approved`, `Rejected` (with `RejectionReason`) or, on `Cancelled`, `Draft`;<br>&nbsp;&nbsp;– `ApplyOutcomeAsync` is shared with the sweep and the withdrawal.<br>• **The sweep:**<br>&nbsp;&nbsp;– `SeparationApprovalOwnerStore : IWorkflowApprovalOwnerStore` (`HR.Contracts.Infrastructure/Workflow/`), its tenants from `AuditLogs` as the card's store, with a new key `AdvisoryLockKeys.SeparationApprovalSweep` (the next free number, `4_001_014` today);<br>&nbsp;&nbsp;– `AddWorkflowApprovalSweep<SeparationApprovalOwnerStore>` in `HR.Contracts.Web/Program.cs`;<br>&nbsp;&nbsp;– PLAN-46's shared keys, no separation key: `WorkflowApproval__Sweep__IntervalMinutes "1"` and `StaleAfterMinutes "2"` for `hr-contracts-web` in the prod-like overlay.<br>• **HR.Core's two definitions:**<br>&nbsp;&nbsp;– two entries of `WorkflowPackSeeder.Workflows`, with fixed new ids after `f0050017-…`, `DomainEntityType` `SeparationRecord`, and D-SEP-4's steps with Arabic names;<br>&nbsp;&nbsp;– both codes in `DistinctApproverCodes`, so that one person never takes two steps of one separation;<br>&nbsp;&nbsp;– `EnsureCrossServiceChainsAsync` gains both chains;<br>&nbsp;&nbsp;– `SeedAsync` seeds them at the next start of every existing database, inside the tenant, so the demo gets them with no migration;<br>&nbsp;&nbsp;– `SEPARATION-WIZARD`'s retirement is only checked.<br>• **The guards:**<br>&nbsp;&nbsp;– `WorkflowDefinitionsHaveHandlersTests`: `SeparationRecord` points at `SeparationApprovalOutcomeConsumer`, with no `PendingPlan`, found by reflection;<br>&nbsp;&nbsp;– `Wf046RegistryEnforcedTests` (`Wf046RegistryTests.cs`): every code of every registered type is seeded and passes the validator (the skip goes).<br>• **HR.Contracts has no «اعتماد» button:** the approval is in HR.Core's inbox only.<br>• **Tests:**<br>&nbsp;&nbsp;– `Sep060ApprovalRoutingTests`;<br>&nbsp;&nbsp;– `Sep060SubmitTests` (with `FakeWorkflowApprovalClient`): the request, the excluded ids, a failure per `ErrorCode`, and no `PendingApproval` without an instance;<br>&nbsp;&nbsp;– `Sep060OutcomeConsumerTests`: a foreign instance, a repeat, `Cancelled`, an excluded decider;<br>&nbsp;&nbsp;– `Sep060SweepAndWithdrawTests`: the sweep applies an ended outcome once, and a withdrawal meeting `AlreadyTerminal` applies its `FinalOutcome`;<br>&nbsp;&nbsp;– **`Sep060ExclusionTests`:** HR.Core's evaluator, with a separation instance whose excluded list holds the creator and the submitter, refuses each of them at approve and at reject while he holds `hr-core` (`WorkflowSodRules.ExcludedMessage`), for both codes. |
| P-8 | **The decision** (plan §3).<br>• **`IssueSeparationDecisionCommand`** (`IssueSeparationDecision.cs`), on `IssueAppointmentDecision`'s pattern:<br>&nbsp;&nbsp;– `CodeGenerationRequests.AdministrativeDecision("SEP")` → `DEC-SEP-YYYY-#####`;<br>&nbsp;&nbsp;– `Subject` «قرار إنهاء خدمة: <الاسم> — <النوع> — نافذ <التاريخ>», `RelatedEmployeeId`, the issue date in Damascus, the issuer;<br>&nbsp;&nbsp;– the record's `DecisionId`, and the item `DecisionIssued` completed (`System`, by the issuer).<br>• **The rules:**<br>&nbsp;&nbsp;– only for an `Approved` record, and once: a second call gives back the same decision and spends no number;<br>&nbsp;&nbsp;– the issuer is not the creator while `IssuerMustDifferFromCreator=true`;<br>&nbsp;&nbsp;– policy `HrContractsApprove`.<br>• **A linked decision is never changed:** `AdministrativeDecisions/Edit` refuses a decision linked to a separation record (the plan's rule), and so does `Delete` (the record's foreign key and its history need it).<br>• **No PDF:** G-SEP-16, wave 2.<br>• **Tests:** `Sep060DecisionTests` (the number's form, once, the issuer rule, a state that is not `Approved`, Edit and Delete refused). |
| P-9 | **The checklist** (plan §4).<br>• **`SeparationStepCompletionMode`** (`HR.Contracts.Domain/Enums/`): `Manual` (HR completes it before execution, with evidence), `System` (the service completes it itself), `PartyAck` (the party's acknowledgement completes it, after execution).<br>• **Four values added at the end of `SeparationStep`:** `DecisionIssued`, `EmployeeNotified`, `ItAccessRevoked` and `HrCoreUpdated`. The text storage keeps every stored value.<br>• **`SeparationChecklistDefaults`** gives each item its mode, owner and party:<br>&nbsp;&nbsp;– `DecisionIssued` `System`; `EmployeeNotified` `Manual`; `ContractTerminated` `System`, at the execution;<br>&nbsp;&nbsp;– `PayrollSettled`, `CardRevoked`, `AccountDisabled`, `AssetsReturned`, `Archived`, `ItAccessRevoked` and `HrCoreUpdated` `PartyAck`, each with its party's name from `ProcessParties`;<br>&nbsp;&nbsp;– `RequireEmployeeNotification=false` makes `EmployeeNotified` not required on new records.<br>• **`CompleteManualStepCommand`** replaces `CompleteSeparationChecklistStepCommand`:<br>&nbsp;&nbsp;– `Manual` items only, in `Draft`, `PendingApproval` or `Approved`;<br>&nbsp;&nbsp;– evidence required: a reference of at least 5 characters, or a `FileId`;<br>&nbsp;&nbsp;– `CompletedBy` is the signed-in user, and an empty one is refused.<br>• **`RequestStepException` and `ApproveStepException`** (`SeparationChecklistException.cs`), two requests from two sessions:<br>&nbsp;&nbsp;– the first records a reason of at least 20 characters;<br>&nbsp;&nbsp;– the second is by another holder of `HrContractsApprove`, neither the requester nor the creator;<br>&nbsp;&nbsp;– **for a `Manual` item only:** `DecisionIssued` is never excepted (no execution without a numbered decision), and no `PartyAck` item is excepted from HR.Contracts;<br>&nbsp;&nbsp;– the form's `approvedBy` goes.<br>• **`SeparationPartyAckChecklistConsumer`** generalises `SeparationPartyCompletedChecklistConsumer`. Each execution's acknowledgement completes the matching `PartyAck` item: `IsCompleted`, `EvidenceRef`, `SourceSystem` = the party, and `CompletedBy` null (allowed here only).<br>&nbsp;&nbsp;– Only while the record is `Executing` or `AwaitingIntervention` and the saga is not compensating;<br>&nbsp;&nbsp;– a compensation's acknowledgement completes nothing.<br>• **The rules:**<br>&nbsp;&nbsp;– execution needs every `Manual` and pre-execution `System` item completed or excepted: `DecisionIssued` and `EmployeeNotified`;<br>&nbsp;&nbsp;– a `PartyAck` item is never completed by hand; its trouble goes through `ProcessInterventions`;<br>&nbsp;&nbsp;– `Step2CompleteStep` goes; `OverrideChecklistStep` is replaced by the two requests;<br>&nbsp;&nbsp;– `Completed` means every mandatory party acknowledged (a test). Open custody and the clearance's state are PLAN-62's, and do not stop `Completed`.<br>• **The journey matrix:**<br>&nbsp;&nbsp;– `docs/fun-separation/journey-matrix.json` and `FUN_002_JOURNEY_MATRIX.md` gain each item's mode and party, `EmployeeNotified` and `HrCoreUpdated`; `SeparationDecision` maps to `DecisionIssued`, `ITAccessRevoked` to `ItAccessRevoked`;<br>&nbsp;&nbsp;– `Fun002SeparationJourneyTests` follows in the same commit, and checks both ways that every `SeparationStep` has its item with the same mode as `SeparationChecklistDefaults`.<br>• **Tests:** `Sep060ChecklistModeTests` (no manual completion of a `PartyAck` item; an exception from two sessions; the requester refused; `DecisionIssued` never excepted; a compensation's acknowledgement ignored). |
| P-10 | **The execution and the policies** (plan §5).<br>• **`ExecuteSeparationCommand`** replaces `PublishSeparationCommand`. It checks, in order:<br>&nbsp;&nbsp;– the status `Approved`;<br>&nbsp;&nbsp;– the decision exists;<br>&nbsp;&nbsp;– the pre-execution items are complete or excepted;<br>&nbsp;&nbsp;– the effective date is on or before today in Damascus (`ICalendarService.ToLocalTime`); a future date is refused with a message naming it, in Damascus's terms;<br>&nbsp;&nbsp;– the legal hold, through PLAN-42's client, fail-closed;<br>&nbsp;&nbsp;– the service regime, with its logic of today (moving it to the creation is PLAN-61's).<br>&nbsp;&nbsp;Then, in **one transaction with the outbox**: the contract terminated with `EndDate = EffectiveDate`, `ContractTerminated` (`System`), `Executing` with its actor, the transaction moved, and `IStartSeparationSaga`.<br>• **`ContractWorkflowRules.Terminate(contract, endDate)`** sets `EndDate`, and refuses a date before `StartDate`. Every other caller is updated.<br>• **No path starts the saga but the execution:** an architecture test finds any other call of `StartSeparationSagaAsync`.<br>• **The policies** (D-51-2: PLAN-51's split stays). Every POST names its policy, pinned per action by **`HrContractsSeparationPostPoliciesTests`** (`src/Tests/HRManagement.UnitTests/Architecture/`), the reads `ModuleHrContracts`:<br>&nbsp;&nbsp;– **`HrContractsWrite`**, the maker's acts: `Step1`, `Create`, `Edit`, `Submit`, `Withdraw`, completing a manual item, requesting an exception, and `Step2Confirm` (it writes nothing);<br>&nbsp;&nbsp;– **`HrContractsApprove`**, a second person's acts: approving an exception, issuing the decision, executing (`Step3Execute` and the Details page's «تنفيذ»; `PublishSeparation` goes), and cancelling;<br>&nbsp;&nbsp;– whatever test pins PLAN-51's map follows, with that reason;<br>&nbsp;&nbsp;– every new action carries the controllers' org-scope stance (`OrgScopeCoverageTests`) and an antiforgery check;<br>&nbsp;&nbsp;– no action of either controller takes a parameter named like an approver's id (`SoDForgedApproverTests` gains them).<br>• **`Delete` goes:** a record is never deleted (FUN-002), and a cancellation needs a reason. The list's delete becomes the cancel form.<br>• **`CancelSeparationCommand`:**<br>&nbsp;&nbsp;– from `Draft`, `PendingApproval` or `Approved` only, with a reason required; the transaction follows;<br>&nbsp;&nbsp;– from `PendingApproval`, `CancelAsync` first. On `AlreadyTerminal`, the `FinalOutcome` is applied, and the request is judged again on the status that results;<br>&nbsp;&nbsp;– from `Approved`, no saga exists. A decision already issued stays, linked, as history (a corrective decision is wave 2);<br>&nbsp;&nbsp;– from `Executing`, `AwaitingIntervention` or `Completed`: PLAN-63's message.<br>• **The interventions:**<br>&nbsp;&nbsp;– **`SeparationInterventionStatusConsumer`:** `IProcessInterventionRequested` of a separation → `AwaitingIntervention`;<br>&nbsp;&nbsp;– `ProcessInterventionsController.Resume` → `Executing`;<br>&nbsp;&nbsp;– `ProcessInterventionsController.Cancel` refuses a separation's saga with PLAN-63's message (a hire's stays as it is);<br>&nbsp;&nbsp;– if `Resume` or `Cancel` start to write the record, they get the antiforgery check with a `GET …/antiforgery`, as PLAN-56 did, and their clients follow.<br>• **`SeparationSagaFinalizedConsumer`:** `Success=true` → `Completed`, its transaction found by `SeparationRecordId`.<br>• **Guard tests:** `Status` assigned only through `SeparationWorkflowRules.Transition`; no code writes `SeparationRecord.ApprovedBy`; no path leaves `PendingApproval` without `WorkflowInstanceId` (the plan's DoD).<br>• **Tests:** `Sep060ExecutionTests` (each refusal of the order, the Damascus date at 22:00 UTC, the contract's end, one unit or nothing, cancellation by state, the intervention's refusal, the saga's end). |
| P-11 | **The screens** (plan §4's interface, §5, §6).<br>• **`Views/SeparationWizard/Step1..4.cshtml`, `Views/SeparationRecords/Details.cshtml`, `Index.cshtml`, `Create.cshtml` and `Edit.cshtml`:**<br>&nbsp;&nbsp;– the status bar, and the approval instance's link and state (`GetStatusAsync`);<br>&nbsp;&nbsp;– the checklist table: mode, owner, state, evidence;<br>&nbsp;&nbsp;– a completion form for each `Manual` item, and the exception's request and approval;<br>&nbsp;&nbsp;– the actions by status: submit, withdraw, issue the decision, execute, cancel with a reason;<br>&nbsp;&nbsp;– the decision's number with its link;<br>&nbsp;&nbsp;– who created the record, or its source.<br>• **The forms:** the employee's contracts only, reloaded when the employee changes; no `approvedBy` field anywhere.<br>• **`Index`:** a status column and a status filter (the published flag's filter goes); «سجل قديم — يتطلب اعتماداً» on a backfilled draft.<br>• Every text in Arabic (`InterfaceTextGuardTests`), and the readable-interface rules of PLAN-67 where they apply to these pages (`AcUx67ReadableInterfaceTests`). |
| P-12 | **AC-SEP-60a..i** (the plan's scenarios, on the production-like profile).<br>• **The persona `a-leaver`:**<br>&nbsp;&nbsp;– `hr-employee`, agency A, HQ, linked to an employee no persona uses, with an active contract and a payable status; its purpose says it is a template that is never separated itself;<br>&nbsp;&nbsp;– in `personas.json`, with `AcceptancePersonaCatalog.ApprovedRegularPersonas["AGENCY-A"]` 49 → 50 and `DATA-AUTH` 70 → 71;<br>&nbsp;&nbsp;– `AC-SEP-60` added to the scenarios of the personas it uses, if the catalog asks;<br>&nbsp;&nbsp;– `scripts/acceptance-reset.ps1` before the first start (Lessons).<br>• **`AcceptanceLeaverFixture.CreateAsync(template, recorder)`** (`src/Tests/HRManagement.AcceptanceTests/`): a new copy of the template for each scenario that separates someone, by the screens alone (the URL and the token each page renders):<br>&nbsp;&nbsp;– an HR.Core employee (`central-a`), in the template's unit and grade, with a unique name marked `AC-SEP-60` and a hire date before the current month;<br>&nbsp;&nbsp;– his service regime classified through `ServiceRegimeExceptions/Resolve`;<br>&nbsp;&nbsp;– a contract by HR.Contracts' screens: create, signature, commencement, activation. Its start before the current month, no probation, and a salary inside the grade's bounds, so Payroll places no PAY029 hold;<br>&nbsp;&nbsp;– it waits until Payroll holds the copy's active pay profile and no review hold (a read), and returns the employee's and the contract's ids;<br>&nbsp;&nbsp;– it never changes the template. PLAN-61..64 extend it, and never change the shared persona.<br>• **Two classes**, both `[RequiredAcceptance]` with `EvidenceRecorder("AC-SEP-60")`, one sub-scenario per test so the folder holds the eight elements of each:<br>&nbsp;&nbsp;– **`AcSep60SeparationGovernanceTests`**, a..h (`main`);<br>&nbsp;&nbsp;– **`AcSep60OutcomeSweepTests`**, i (`isolated`: it stops `hr-contracts-web`).<br>• **The scenarios** (one person per role; every approval from `WorkInbox` through `CustodyJourney.ApproveFromInboxAsync`, with a reject helper added beside it):<br>&nbsp;&nbsp;– **a:** `a-contracts-maker` creates a Resignation and submits it → `PendingApproval`, with an instance. `central-a` approves step 1 and `a-management-approver` step 2 → `Approved` within 30 seconds.<br>&nbsp;&nbsp;– **b:** on a copy whose effective date is today in Damascus, `a-contracts-checker`:<br>&nbsp;&nbsp;&nbsp;&nbsp;– issues the decision → `DEC-SEP-<the issue date's year>-#####`;<br>&nbsp;&nbsp;&nbsp;&nbsp;– completes `EmployeeNotified` with evidence on the screen;<br>&nbsp;&nbsp;&nbsp;&nbsp;– executes → `Executing` at once, the contract `Terminated` with its end date the effective date.<br>&nbsp;&nbsp;&nbsp;&nbsp;Negatives on a second copy, effective tomorrow: an execution before the decision → refused; after the decision and the notification → refused, naming the date.<br>&nbsp;&nbsp;&nbsp;&nbsp;The saga's end on this stack is recorded (`Completed`, or `AwaitingIntervention` with its party): the parties' truth is PLAN-62's.<br>&nbsp;&nbsp;– **c:**<br>&nbsp;&nbsp;&nbsp;&nbsp;– `a-contracts-maker` posts the approval of step 1 of his own record in HR.Core → refused, the instance still at step 1. The test records which layer answered (the gateway's or HR.Core's policy, or the engine's role): he holds neither the step's role nor HR.Core's module. The exclusion itself is `Sep060ExclusionTests`' (P-7);<br>&nbsp;&nbsp;&nbsp;&nbsp;– after the two approvals, `a-contracts-maker` issues the decision of his record → refused (the issuer rule).<br>&nbsp;&nbsp;– **d:** through the gateway, `a-management-approver` posts `Step3Execute` on a real record → 403. Without the gateway, **`Sep060ServicePolicyHttpTests`** (`Iam052PgServiceHost`) gives the same role 403 from the action's policy.<br>&nbsp;&nbsp;– **e:** a second open record for the same copy → refused, still one record; a record with another copy's contract → refused; editing `EmployeeId` after the submission → refused, the fields unchanged.<br>&nbsp;&nbsp;– **f:** after the approval and the decision, `a-contracts-maker` requests an exception on `EmployeeNotified`. His own approval of it → refused. `a-contracts-checker` approves it → the item excepted, and the execution passes → `Executing`.<br>&nbsp;&nbsp;– **g:** `a-management-approver` rejects step 2 with a reason → `Rejected` with the reason; no saga row; Payroll unchanged (the copy's profile still active, no settlement, no adjustment).<br>&nbsp;&nbsp;– **h:** a `Disciplinary` record → three steps. After `central-a`'s step 1, his attempt at the `compliance` step → refused by its role; then `a-compliance`, then `a-management-approver` → `Approved`.<br>&nbsp;&nbsp;– **i, positive:** three copies, submitted in this order: A (the sweep's), C (the negative's), then B (the withdrawal's), just before the stop.<br>&nbsp;&nbsp;&nbsp;&nbsp;– Stop `hr-contracts-web`; approve A and B fully in HR.Core; read the outcome queue (management API) until both outcomes are there, then purge it; start `hr-contracts-web` and wait for `/health/ready`;<br>&nbsp;&nbsp;&nbsp;&nbsp;– at once, withdraw B: its own answer shows `AlreadyTerminal`, and B is `Approved`, not `Draft`. The withdrawal must meet B still `PendingApproval`: the sweep takes only records older than `StaleAfterMinutes`, which is why B is submitted last. If the overlay's 2 minutes cannot make that certain, give `hr-contracts-web` a wider `StaleAfterMinutes` in the overlay (the same shared key), and say why;<br>&nbsp;&nbsp;&nbsp;&nbsp;– A becomes `Approved` through the sweep, within `StaleAfterMinutes` + `IntervalMinutes` of its submission, plus the start-up. The measured delay is recorded.<br>&nbsp;&nbsp;&nbsp;&nbsp;**i, negative:** `AcWf46CrossServiceApprovalTests.PublishOutcomeAsync` publishes an `Approved` naming an unlinked instance for C → ignored and logged, C still `PendingApproval`; then C is withdrawn and cancelled.<br>• **Isolation** (each class may run first, last, or twice on one stack):<br>&nbsp;&nbsp;– each scenario takes its own copies, and relies on no other scenario;<br>&nbsp;&nbsp;– every instance it starts ends (`Approved`, `Rejected` or `Cancelled`) before it ends: a record left `PendingApproval` is withdrawn;<br>&nbsp;&nbsp;– every record it does not execute is cancelled with a reason, in `finally`; no exception request is left open;<br>&nbsp;&nbsp;– an executed copy keeps its saga as it ends (recorded);<br>&nbsp;&nbsp;– no seeded employee, contract or record is touched;<br>&nbsp;&nbsp;– the copies are ordinary agency-A hires (a grade, a salary inside its bounds, a classified regime, an active contract), like the `journey` group's. P-14's run proves that the `main` group's other classes still pass after them;<br>&nbsp;&nbsp;– **no SQL write:** every setup and act by HTTP; psql only reads (the fixture's waits, `db-checks.json`). |
| P-13 | **What this plan changes for older tests and checks** (each adapted with its meaning kept; a test that asserted the old gap changes, with its reason in the report).<br>• **Unit and integration tests** (P-3's list): each reaches its record through the new path (the factory, `FakeWorkflowApprovalClient` and the outcome, the decision, the execution):<br>&nbsp;&nbsp;– `ContractLifecycleIntegrationTests`, `Fun002SeparationRecordIntegrationTests`, `Int003ProcessSagaTests`, `G3SeparationMidSagaFailureIntegrationTests`, `ContractSeparationChainIntegrationTests`, `Fun002SeparationSagaIntegrationTests`;<br>&nbsp;&nbsp;– `EndToEndReleaseWorkflowSmokeIntegrationTests` (one of PLAN-59's fourteen);<br>&nbsp;&nbsp;– `Phase7_BusinessLogicDepthTests.cs`, `Fun002SeparationJourneyTests`, `S2s042LifecycleGateTests`, `SeparationSagaCompensationTests`, `Wf043*`, `Wf046*`, `WorkflowStageGuardRoleTests`, `CrossTenantBoundedContextIsolationTests`, `Iam051ModuleFilterAndPoliciesTests`, `SoDForgedApproverTests`.<br>• **AC-QA-34e** counts eleven integration tests by name: keep a name whose meaning is kept. A test that must be renamed changes that list in the same commit.<br>• **AC-INT-37a:** its employee must now be one whose service has not ended, with the contract passed when he has several. It may take a copy from `AcceptanceLeaverFixture` instead. Its meaning is kept: three transactions, each numbered by HR.Core.<br>• **AC-INT-37e:** with one open record per employee, its second run on one stack would meet its first record. It takes a copy, or cancels what it created, so that it repeats.<br>• **AC-S2S-42h:** its records reach `Approved` through HR.Core's inbox (`central-a`, `a-management-approver`). The checker issues the decision and completes `EmployeeNotified` on the screen, and the execution meets the hold and HR.Core's outage.<br>&nbsp;&nbsp;– No SQL write to a separation's status, items, decision or approval. The hold flag and the regime it sets by psql today may stay: neither is what this plan governs;<br>&nbsp;&nbsp;– its meaning is kept: creation passes without a hold and stops with one; execution stops under a hold, and when HR.Core is down.<br>• **`AcWf43VpsChecks`** (server): `Published` 12 → 14, and the distinct-approver codes gain the two separation codes.<br>• **`WorkflowDefinitionsHaveHandlersTests` and `Wf046RegistryEnforcedTests`:** as P-7.<br>• **The seeders:**<br>&nbsp;&nbsp;– `DemoContractsSeeder.SeedSeparationsAsync` writes the status by the backfill's rules, the items' modes, and no `ApprovedBy`;<br>&nbsp;&nbsp;– `DATA-SEPARATION` stays 86, and whatever test reads the seeded records follows.<br>• **The E2E separation tests** only open the wizard; they stay green (CI only). |
| P-14 | **CI placement and local testing.**<br>• **`tests/acceptance/ci-scope.json`:**<br>&nbsp;&nbsp;– `AcSep60SeparationGovernanceTests` in `prodLike[]` group `main`, `required: true`;<br>&nbsp;&nbsp;– `AcSep60OutcomeSweepTests` in `prodLike[]` group `isolated`, `required: true`;<br>&nbsp;&nbsp;– `ac-ownership.json`: `AC-SEP-60` (PLAN-60, `provesClaim: true`, its reason).<br>• **The `prodlike` job's limit** by QA-066's rule: the local measurement first, then the PR's first run, about 1.5 times the measured duration.<br>• **The new PostgreSQL classes** carry `Category=Testcontainers`, so that CI's filter runs them.<br>• **Local testing** (the owner's rule of 2026-11-06): only what this plan touches, the full suite once in CI.<br>• **A fresh prod-like stack** (`scripts/acceptance-up.ps1 -Profile ProdLike -UseLocalBaseImages -NoBuild`, after `scripts/acceptance-reset.ps1`), under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`:<br>&nbsp;&nbsp;– the seeded records' statuses, against P-3's 0.5 (the seeder writes them by the backfill's rules; the backfill itself is proved by `Sep060MigrationPostgreSqlTests`, and on the demo in P9);<br>&nbsp;&nbsp;– `AcSep60SeparationGovernanceTests`, then **again on the same stack** (the plan's «repeatable with no reset»);<br>&nbsp;&nbsp;– then **every other class of `main`** on the same stack: AC-SEP-60 adds people, contracts and pay profiles to agency A, and any class of the group may meet them. That is the isolation's proof;<br>&nbsp;&nbsp;– then, each alone: `AcSep60OutcomeSweepTests` (twice), `AcInt37ProductionWiringTests`, `AcWf46CrossServiceApprovalTests`, and `AcWf45AssetApprovalLoopTests` if `CustodyJourney` changed;<br>&nbsp;&nbsp;– then the `s2s42` group with its overlays and recreated services;<br>&nbsp;&nbsp;– `scripts/acceptance-evidence-summarize.ps1`, then `down -v`.<br>• **`leastpriv`** (a migration and a realm change): `AcOps38FirstBootTests` and `AcTen48LeastPrivilegeTests`.<br>• **The lab stack** only if a lab class's code or helper changed. **E2E:** in CI only.<br>• Then the seeded counts of `scripts/acceptance-verify.ps1` by psql (`DATA-AUTH` 71, `DATA-SEPARATION` 86). Known local-only failures (`Ac42i` without `kubectl`) are recorded, not chased. |
| P-15 | **Scripts.**<br>• Every new or changed `.ps1` is ASCII-only, 5.1-compatible in syntax, and runs here with `pwsh -NoProfile -File …`.<br>• **`scripts/sep060-validate.ps1`** has a `-SelfTest` with planted defects, as `pay059-validate.ps1` does. It checks at least:<br>&nbsp;&nbsp;– both classes required in their groups, with `[RequiredAcceptance]`;<br>&nbsp;&nbsp;– no SQL write in `AcSep60*` or `AcceptanceLeaverFixture`;<br>&nbsp;&nbsp;– the persona `a-leaver`, and the catalog's count;<br>&nbsp;&nbsp;– `SeparationRecord`'s guard entry with no `PendingPlan`;<br>&nbsp;&nbsp;– the two codes in `WorkflowPackSeeder` with D-SEP-4's roles, and both in `DistinctApproverCodes`;<br>&nbsp;&nbsp;– `StartSeparationSagaAsync` called by the execution alone;<br>&nbsp;&nbsp;– no `approvedBy` parameter in the two controllers, and every POST with an explicit policy;<br>&nbsp;&nbsp;– the migration lifts `FORCE` on each table it changes, and puts it back;<br>&nbsp;&nbsp;– the `Separation` keys in `appsettings.json`, and the sweep's keys for `hr-contracts-web` in the prod-like overlay.<br>• **`scripts/sep060-report-unlinked.ps1`:** reads only (psql, on a local stack or on the server through `ssh`). It reports the records with no linked transaction and the `Separation` transactions with no record: counts by default, ids with `-Detail`, never a name.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile <ProdLike or Lab> -UseLocalBaseImages`. |
| P-16 | **The documents** (the plan's Deliverables).<br>• `docs/FUN_002_JOURNEY_MATRIX.md` and `docs/fun-separation/journey-matrix.json` (P-9).<br>• `docs/FUN_002_EXCEPTION_PATHS.md`: cancellation by status; a decision that stays with a cancelled `Approved` record; the interventions' cancel refused until PLAN-63.<br>• `docs/ADR-FUN-002-separation-journey-closure.md`: an annex on the decision and the approval.<br>• `docs/PROCESS_SAGAS.md` (§DOM-001's gate): only the execution starts the saga, and the record's status follows it.<br>• `docs/WF_001_WORKFLOW_ENGINE.md`: the two chains are seeded and owned.<br>• `docs/FUN_002_SEPARATION_GAP_STUDY.md`: the «الحالة» lines of G-SEP-01, G-SEP-04 and G-SEP-11, linking the PR run's evidence of AC-SEP-60.<br>• `docs/CI_AND_TESTING.md`: the two classes and their groups, why i is isolated, the fixture, the persona.<br>• `docs/EVENT_PUBLISHER_INVENTORY.md`, if it lists the new consumers.<br>• `docs/OPS_VPS_SINGLE_HOST.md`: PLAN-60's section — the migration and the backfill's numbers, the change table, the backup, what the checks leave, and the rollback note.<br>• The plan's `Status` and «ملاحظات التنفيذ والانحرافات». |
| P-17 | **The demo server facts this plan must respect.**<br>• **It runs Production in demo mode** (`Deployment__Mode=demo`). The migration runs there in P9; P0's picture says what it will meet.<br>• **The checks add no user.** They take one Resignation of a demo employee (active, one active contract, no open record, a future effective date) only as far as `Approved`:<br>&nbsp;&nbsp;– `e2e-maker-contracts` creates and submits it;<br>&nbsp;&nbsp;– `e2e-delegate-hr` approves step 1, and `e2e-checker-mgmt` step 2;<br>&nbsp;&nbsp;– then `e2e-maker-contracts` cancels it, with a reason marked «PLAN-60 server check».<br>&nbsp;&nbsp;No decision is issued (it spends an official number), and nothing is executed (it would end a demo employee's service).<br>• **P0's read-only picture:**<br>&nbsp;&nbsp;– the running tag and the free disk;<br>&nbsp;&nbsp;– `SeparationRecords` by `(IsCancelled, SeparationEventPublished)`; the open duplicates per employee, and **whether any employee has two records whose sagas started** (P-5); the records with `EmployeeId = Guid.Empty`;<br>&nbsp;&nbsp;– `SeparationSagaStates` by `CurrentState`; the `Separation` transactions, and how many the 5-minute rule would link;<br>&nbsp;&nbsp;– `WorkflowDefinitions` by status; `AdministrativeDecisions` by type code; contracts by status; HR.Core's employees by status;<br>&nbsp;&nbsp;– Payroll's checksums as PLAN-59 took them (runs, lines, batches, orders, arrears);<br>&nbsp;&nbsp;– never a name, a national number or an amount.<br>• **What the owner will see change:**<br>&nbsp;&nbsp;– the separation screens: status, the checklist by mode, the approval's link, the decision, the execution, the cancellation with a reason;<br>&nbsp;&nbsp;– the old records: the published ones `Executing` or by their saga, the rest `Draft` with «سجل قديم — يتطلب اعتماداً»;<br>&nbsp;&nbsp;– the two definitions in HR.Core's workflow list;<br>&nbsp;&nbsp;– one cancelled check record per run of the checks. |
| P-18 | **The demo server** (the GHCR path of `docs/OPS_VPS_SINGLE_HOST.md`).<br>• **When:** after the release branch is pushed (P8), with the running tag on `origin/main` (`git branch -r --contains 8086ac8629af`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan60-<utc>/` (700/600), verified as before (every `CREATE DATABASE` statement, `SHA256SUMS`).<br>• **Before the deploy, read-only:** P-17's picture, again if P0 took it.<br>• **The disk:** at least 15 GB free before the pull. Never trim Seq's volume.<br>• **Build** in GitHub Actions: `gh workflow run deploy.yml --ref release/plan-60 -f action=build`, find its run (`gh run list --workflow deploy.yml --limit 1`), and wait in short polls (about 14 minutes; «Waiting»). It gives 17 images tagged with the first 12 characters of the branch's head, Trivy-clean and signed. A job red at Trivy has pushed an unsigned image: fix it and build again.<br>• **Deploy:** `scripts/vps/deploy.ps1 -Tag <sha12>`, and again at once if it stops at `keycloak-reconcile`. **Its bootstrap applies `SEP060`;** HR.Core seeds the two definitions at its start.<br>• **Rollback:** `scripts/vps/deploy.ps1 -Rollback` brings back `8086ac8629af` from GHCR.<br>&nbsp;&nbsp;– It is possible: that tag runs on this schema (`Sep060RollbackReadingTests`), and `SeparationStatusReconciler` brings back in line what it changed, once this tag runs again (P-5);<br>&nbsp;&nbsp;– it is not advised: fix forward. `OPS_VPS_SINGLE_HOST.md` says so, with what the previous tag would leave behind.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn:<br>&nbsp;&nbsp;– roll back to the previous tag, or fix forward;<br>&nbsp;&nbsp;– restore the backup only if data was damaged;<br>&nbsp;&nbsp;– prove the demo works, and record the downtime. |
| P-19 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P8, after the local CI steps, the Release build and the pre-push check are green, in the same turn as the pre-push check (Lessons).<br>2. `gh pr create`, then wait («Waiting»), with the server phase (P9) beside it.<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, run the unit suite and the format check, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>`, and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-20 | **No regression of PLAN-25, PLAN-36..59, PLAN-66 and PLAN-67.** These stay green:<br>• the hire journey and its saga (`HireWizard`, AC-HCM-25a..h, `Hcm025PanelServerParityTests`, AC-HCM-29), the leave and advance requests, the contract lifecycle;<br>• PLAN-46's two owners (`Wf046CardApprovalTests`, `Wf046DisciplinaryApprovalTests`, AC-WF-46 with the orphan class), PLAN-45's custody (`AcWf45`), `AcWf43`, `AcWf44`, `AcPol47`, both `AcPol49` classes, both `AcTen48` classes;<br>• `AcIam50`..`AcIam53`, `AcPay54`..`AcPay56`, `AcPay58`, AC-PAY-59, AC-12, AC-21, AC-23, `AcLva57`, `AcUx67`, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, `AcOps39`, `AcQa41`, and both recruitment journeys;<br>• **every existing payroll result, unit for unit:** a separation's payroll party is unchanged;<br>• the architecture tests (`WriteActionsHaveExplicitPolicyTests`, `ModuleOverridesAreDeclaredTests`, `GatewayApproverReachTests`, `GatewayAuditorReachTests`, `OrgScopeCoverageTests`, `InterfaceTextGuardTests`, `GuardSelfTests`, `PayrollWriteActionsTests`), `OutboxPublishOrderingArchitectureTests`, `Rel004CompatibilityTests`, `ProductionWiringMatrixTests`, `CiStepsParityTests`;<br>• the validate scripts `pay059`, `pay058`, `lva057`, `pay056`, `pay055`, `pay054`, `iam053`, `iam052`, `iam051`, `iam050` and `ux067`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. `pol049-validate.ps1` and `acceptance-baseline-validate.ps1` are red on `main` already: record their state before and after. |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-46:** the cross-service mechanism itself (the start API, the event, the role and exclusion rules, the inbox), HR.Core's side of the registry, `SEPARATION-WIZARD`'s retirement, and the sweep's base.
- **PLAN-42:** the authenticated, fail-closed hold client (G-SEP-12); this plan reads one more field of its answer.
- **PLAN-37:** shipping `service-regimes.json` and HR.Core's address and scope for HR.Contracts.
- **PLAN-61:** the catalog of types per regime, the death type, the type's check at creation, and the single entry.
- **PLAN-62:** the truth of the parties' acknowledgements (Keycloak, custody, insurance), and the clearance.
- **PLAN-63:** cancelling after execution, the compensation, vacating the position, re-employment.
- **PLAN-64:** the final settlement.
- **Wave 2 (SEP-G):** the decision's PDF, a scheduled execution, ESS.
- **PLAN-52 (IAM-D):** the unit scope of the separation screens (already in place; this plan keeps its stances).

**Never commit:**
- the owner's local edits (`global.json`, `tmp-vendors.html`) — check `git diff --cached --name-only` before every commit;
- `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- another plan's untracked prompt file, or a helper of the local image builds;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- a screenshot or any other image;
- any secret, token or password (templates keep their placeholders, `__SVC_SECRET__` or `${…}`), or a literal gitleaks would read as one.

**Never:**
- push to `main` or force-push, or rewrite a branch's history after it was pushed;
- merge with a pending or red check;
- touch the side PRs (#40, #42), Dependabot's PRs, the owner's branch `ops/ghcr-release-images`, its worktree, or their branches;
- disable host-key checking;
- widen a policy, a scope or a role to make a page or a test work; relax the excluded approvers, the issuer rule or the exception's two people to get green;
- give HR.Contracts an approval button, or start the saga anywhere but the execution;
- cancel an executing separation (PLAN-63's), or move a separation's transaction by hand;
- write SQL to a separation's status, items, decision or approval in a test — the migration's tests plant their own data on their own database;
- add a row to any ceiling list: they only shrink;
- add users to the demo's realm, or issue a decision or execute a separation there;
- touch another project's Docker images, containers or volumes, or the owner's dev containers;
- print a password, or rotate a secret that exists on the server;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** (port 22 at P0 excepted: Preconditions 4).
- **A piece of PLAN-46's contract is missing** and no thin adapter can stand in for it (P-3).
- **Any data shows an employee with two records whose sagas started** (P-5): the backfill would have to cancel one of them. Name the records first.
- **A data change of the migration cannot be proved as the migrator** under forced row security.
- **An existing payroll result would change,** and the cause cannot be removed without changing the plan's design.
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning.
- **The payroll classes meet the interim gate's message** because STR-002's exceptions expired (after 2027-01-31): the owner renews them.
- **12 GB free for an image build cannot be reached** without touching other projects, **or a port the tests read is held** by something else (P-2).
- **The server's free space is under 15 GB** before the pull.
- **Port 22 is still blocked when P9 starts:** one line asking the owner to restore it.
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, keep-awake, preconditions, baseline

1. Write the `Started (UTC)` line (see «Timing»). Start the keep-awake process (P-2). Check the Preconditions.
2. `git switch -c feature/plan-60-sep-unblock-decision-governance origin/main`. Bring in PLAN-59's merge note, and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan60-baseline.md` (P-3), and the demo's picture (P-17).
4. If port 22 is blocked, record it, and take the demo's picture at the start of P9.

Commit: `chore(sep060): baseline — PLAN-46's contract, the separation tests, the data's flags and duplicates, the paths and classes that create or publish a separation, the demo's picture`.

## P1 — The record: its states, its integrity, the migration (plan §1, §6; P-4, P-5, P-6)

- **The state machine** (P-4), **the factory and the integrity rules** (P-6), **the migration with its backfill and the reconciler** (P-5).
- **Tests:**
  - `Sep060SeparationStateMachineTests` and `Sep060RecordIntegrityTests`;
  - with `RUN_TESTCONTAINERS=true`: `Sep060MigrationPostgreSqlTests`, `Sep060SeparationPostgresIntegrationTests` and `Sep060RollbackReadingTests`;
  - `dotnet ef migrations has-pending-model-changes` clean;
  - P-3's filters, the tests that follow the new path adapted (P-13).

Commit: `feat(sep060): the separation record's state machine, one creation path that checks the employee, the contract and the open record, and the SEP060 migration backfilled under forced row security`.

## P2 — The approval through HR.Core's engine (plan §2, P-7)

- **Routing, submission, withdrawal, the outcome consumer, the sweep with its store and lock key;** HR.Core's two definitions; the guard's entry and the registry test; the prod-like overlay's sweep keys.
- **Tests:** `Sep060ApprovalRoutingTests`, `Sep060SubmitTests`, `Sep060OutcomeConsumerTests`, `Sep060SweepAndWithdrawTests`, `Sep060ExclusionTests`; `WorkflowDefinitionsHaveHandlersTests`, `Wf046RegistryEnforcedTests`, the `Wf043` and `Wf046` classes.

Commit: `feat(sep060): separation approval through HR.Core's engine — two chains seeded with distinct approvers, submission and withdrawal, the outcome consumer and the sweep`.

## P3 — The decision and the checklist (plan §3, §4; P-8, P-9)

- **The numbered decision;** the decision screens refusing a linked decision; **the modes, the four items, the manual completion, the exceptions, the parties' acknowledgements;** the journey matrix and its test; their parts of the screens (P-11).
- **Tests:** `Sep060DecisionTests`, `Sep060ChecklistModeTests`, `Fun002SeparationJourneyTests`.

Commit: `feat(sep060): the numbered separation decision; checklist items by completion mode, exceptions by two people, the parties' acknowledgements`.

## P4 — The execution, the policies, the cancellation, the saga's states (plan §5; P-10, P-11)

- **`ExecuteSeparationCommand` and the contract's end date; the policy map and the architecture tests; the cancellation by status; the interventions; the saga's end;** the remaining screens (P-11).
- **Tests:** `Sep060ExecutionTests`, `HrContractsSeparationPostPoliciesTests`, `SoDForgedApproverTests`, the guard tests; `WriteActionsHaveExplicitPolicyTests`, `OrgScopeCoverageTests`, `InterfaceTextGuardTests`; P-3's filters again, with and without `RUN_TESTCONTAINERS=true`.

Commit: `feat(sep060): execution only on an approved record with its decision and items, on Damascus's date, in one outbox unit; explicit policies; cancellation by status; the separation screens`.

## P5 — Acceptance, the older classes, CI, the scripts (P-12, P-13, P-14, P-15)

1. **The persona `a-leaver`** and its counts; **`AcceptanceLeaverFixture`**; **`AcSep60SeparationGovernanceTests`** and **`AcSep60OutcomeSweepTests`**; **`Sep060ServicePolicyHttpTests`**.
2. **The older classes** P-13 names.
3. **CI scope:** both classes in their groups, `ac-ownership.json`, the `prodlike` job's first limit from the local measurement (P6).
4. **`scripts/sep060-validate.ps1`** with its `-SelfTest`, and **`scripts/sep060-report-unlinked.ps1`.**

Commit: `test(sep060): AC-SEP-60a..i with the leaver fixture; the older classes on the new path; CI placement; sep060-validate and the unlinked report`.

## P6 — Acceptance on the local stacks, then the independent review (P-14)

1. **Images and stacks.** Check the disk and the memory (P-2). Build the images, one service at a time, and the migrator. Record each stack's time to ready, and the migration's run.
2. **The prod-like stack:** P-14's order. Record each scenario's people, ids, numbers and times, the saga's end of each executed copy, and the sweep's delay.
3. **leastpriv,** and the lab stack if P-14 asks.
4. `scripts/sep060-validate.ps1` (and `-SelfTest`), `scripts/sep060-report-unlinked.ps1` on the local stack, the seeded counts by psql, `down -v`, and the committed evidence restored.
5. **The full Testcontainers step on the host,** with CI's filter.
6. **Independent review** (one fresh subagent), before the push, so that the server's images carry its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - **the state machine:** a path that puts a record in `PendingApproval` without an instance, starts the saga outside the execution, writes `ApprovedBy`, or changes `Status` outside `Transition`;
   - **who may act:** a POST without its explicit policy, stance or antiforgery check; a form field naming an approver; the creator or the submitter able to approve, to issue the decision or to approve an exception; a requester approving his own;
   - **the execution:** one that can run before `Approved`, without a decision, with a pre-execution item open, before the effective date in Damascus, under a hold or with HR.Core down; or one that saves the contract, the status and the saga's message apart;
   - **the outcome:** a consumer or the sweep applying an outcome to the wrong record or twice; a `Cancelled` that gives anything but `Draft`; a withdrawal after the end that gives `Draft`;
   - **the backfill:** a statement that matches nothing as the migrator, a record whose saga started cancelled, two open records left for one employee;
   - **the tests:** one changed to pass rather than to keep its meaning (the older classes, AC-QA-34e's names, the server checks);
   - **isolation:** an acceptance class that leaves an instance in progress, an open record or exception request, or a change to a seeded employee;
   - a deviation that defers part of the plan, or a part skipped or listed to save time;
   - something PLAN-59's tag cannot run beside (P-5's rollback reading).

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(sep060): AC-SEP-60a..i pass on the prod-like stack, twice, with the main group after it; review fixes`.

## P7 — The documents, `main` (P-16)

1. **The documents** P-16 lists, and the final report's draft up to the server's section, so that the local gates run on the documents too.
2. **`main`:** if `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to `ContractsDbContext`, rebuild and recheck the model. Rerun the unit, architecture and integration tests and the acceptance classes the merge touched. Record what the merged change means for P9.

Commit: `docs(sep060): the separation journey's documents — the matrix by mode, the exception paths, the decision's annex, the sagas' gate; G-SEP-01, G-SEP-04 and G-SEP-11's status` (and, only if the merge needed fixes, `fix(sep060): main merged, conflicts resolved`).

## P8 — Local gates, the release branch, the PR (P-19)

1. **Local gates, before any push:**
   - the full Testcontainers step on the host, with CI's filter (P6's, if nothing it builds or tests changed since; otherwise again);
   - `dotnet build HRManagement.slnx -c Release -warnaserror` (Lessons);
   - the local CI steps (P-2) on HEAD;
   - then `pwsh -NoProfile -File scripts/plan035-prepush.ps1`.

   All must be green, with every step not run named.
2. **In the same turn as the pre-push check:**
   1. Merge the feature branch into a new `release/plan-60` from `main` (`--no-ff`).
   2. Write `reports/plan60-pr.md` (short).
   3. `git push -u origin release/plan-60`
   4. `gh pr create --base main --head release/plan-60 --title "PLAN-60: the separation journey unblocked and its decision governed" --body-file reports/plan60-pr.md`
3. **Start waiting** for the checks («Waiting»), and begin P9 beside them: alternate short waits on the PR's checks and on the server's work. A red check is read and fixed at once (P-19).

## P9 — The demo server (P-17, P-18), beside the PR's checks

Record every server change and its undo command **as you make it**.

1. Check SSH and P-18's condition.
2. Take the backup, then **verify** it. Take the read-only picture (P-17) again, and compare it with P0's. A record P-5 would have to cancel while its saga started: Stop.
3. **The GHCR build of `release/plan-60`, then the deploy** (P-18). Record the build's run id and duration, the deploy's stretches (the bootstrap's migration apart), the downtime, and the time to all healthy.
4. **Then check**, with evidence in `artifacts/plan60/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them. The new checks go in an `AcSep60VpsChecks` class, and leave nothing open.
   1. **The release:** the running tag, everything healthy, `SEP060` in hr_contracts_db's migration history, no migration pending; `Server_services_log_the_repository_legal_pack_fingerprint` right away (Lessons).
   2. **The backfill:** the statuses as P-17's picture predicts, rule by rule; the duplicates and the records with no employee cancelled with their reasons; the partial index present; the links and `scripts/sep060-report-unlinked.ps1`'s counts.
   3. **The definitions:** `SEPARATION-APPROVAL` and `-SENSITIVE` published with D-SEP-4's steps, and with distinct approvers.
   4. **As far as `Approved`:** P-17's Resignation, then its cancellation; nothing left in progress in HR.Core.
   5. **Nothing else moved:** Payroll's checksums, the contracts by status and HR.Core's employees by status as in the picture.
   6. **Everyone else is unchanged:** the PLAN-36..59 server checks pass, class by class, the browser tours one at a time (Lessons), with `AcWf43VpsChecks` adapted (P-13). `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass. The checks that flicker from this machine are rerun alone.
5. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-18 says, prove the demo works, and record it.

Commit (pushed to the release branch): `test(sep060): the demo server on this release — the backfill as predicted, the two chains seeded, a separation approved through HR.Core and cancelled; PLAN-36..59 server checks still green`.

## P10 — The final report, the merge (by you), the merge note

1. **`reports/plan60-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. **Decisions applied and deviations:** the two classes, PLAN-51's split and `Step3Execute`'s policy, AC-SEP-60c's honest refusal, the definitions' seeding and distinct approvers, records no person created, the backfill's two added rules, `Delete` and the interventions' cancel, the demo's limit, the rollback, the merge by you, and anything else. No deviation defers part of the plan.
   4. **The state machine and the migration:** the table; the seeded statuses; the backfill's numbers in its test and on the demo, rule by rule; the unlinked report.
   5. **The approval:** the chains, the submission, the exclusion, the consumer, the sweep's measured delay, the withdrawal's race.
   6. **The decision and the checklist:** the numbers issued, the modes, the exceptions, the parties' acknowledgements.
   7. **The execution and the policies:** the order of checks, the contract's end date, the policy map, the cancellation by status, the interventions, the saga's end of each executed copy.
   8. **AC-SEP-60a..i:** each scenario with its people, its calls and what it proved; both runs on one stack; the main group after it.
   9. The existing tests and checks that changed, each with the meaning it keeps.
   10. The guards and their own negative tests.
   11. Unit, architecture, integration and acceptance results; the PR run's artifact link as the evidence of record.
   12. **The server:** before and after, changes and their undo, the backup location, the backfill's result, the rollback note.
   13. **Changes for operators and API clients:** the screens, the statuses, the decision's number, the cancellation's reason, the old records, the policies.
   14. Items that other plans own (Scope boundaries), and defects for later plans.
   15. **Timing:**
       - total wall-clock;
       - active time, waiting for CI, and every stop with its cause;
       - the build, migration, acceptance and deploy times; each run of AC-SEP-60;
       - Actions minutes;
       - the output of `pwsh -NoProfile -File scripts/plan-timing.ps1 -From 25 -To 67`.
   16. **A readiness note** for PLAN-61 (the types at creation, the single entry), PLAN-62 (the parties' truth, the clearance), PLAN-63 (cancelling an execution, the compensation), PLAN-64 (the settlement) and PLAN-65 (the rerun of wave 1). It covers `AcceptanceLeaverFixture`, the statuses, and the hooks each plan meets.

   Commit it on the release branch with the documents' last changes. A commit that leaves the code tree unchanged reuses the PR's green run.
2. **The merge (P-19), when every check is green:** `gh pr merge <n> --merge`.
3. **Then:**
   - `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`;
   - record the PR link, each check result, the merge commit and the post-merge run in `reports/plan60-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`;
   - commit that note on a local branch `docs/plan-60-merge-note`, as the plans before did, so the next plan brings it in. **Do not open a second PR for it;**
   - stop the keep-awake process by its PID (P-2).

## Definition of done

- **No path in the code puts a record in `PendingApproval` without `WorkflowInstanceId`,** and no path starts the saga but `ExecuteSeparationCommand`.
- **The separation journey runs from the interface up to `Executing` with production settings,** with no crafted POST: every form the page renders, every approval from HR.Core's inbox.
- **No form carries `approvedBy`,** and no code writes `ApprovedBy`.
- **AC-SEP-60a..i = Pass** under `ACCEPTANCE_REQUIRE_ALL=true`:
  - locally twice on one stack, with the `main` group passing after them;
  - on the PR run, required, its folder `AC-SEP-60` with the eight elements of each scenario.
- **The migration is proved as the migrator,** and on the demo the backfill matches its prediction.
- **The «الحالة» lines** of G-SEP-01, G-SEP-04 and G-SEP-11 name the plan and its evidence.
- **Every existing payroll result is unchanged,** and the older classes keep their meaning (P-20).
- **Nothing in the plan was deferred to save time;** the deviations are those of «Where this prompt departs», each with its reason.
- **The server** runs this code. A separation was approved there through HR.Core and cancelled, and nothing else moved.
- **The merge** is done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what a separation goes through now, from creation to execution, and who does each step;
- AC-SEP-60's result, and what the migration did to the old records (on the stack and on the demo);
- what the owner will see on the demo, and what the checks left there;
- the deviations in plain words (the two classes, the policies, AC-SEP-60c, the demo only as far as `Approved`, the rollback);
- how long the plan took, how long it waited for CI, and every stop with its cause;
- the PR link;
- the path of the final report.
