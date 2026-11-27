# Task: PLAN-58 — Disbursement governance: one payment path, real four eyes, and holds per employee (PAY-F)

The deliverables:
- **Money leaves by one path only:** posting → a batch and its orders → a funding decision (if needed) → processing by the poster, with a fraud scan and a gate for each employee → one bank file generated from the orders, once, with control totals.
- **Every «four eyes» is two acts by two people in two sessions,** never an id typed into a form.
- **Above the threshold a run needs two different approvers,** and an independent reviewer (`auditor`) reviews the run before it is posted.
- **A user whose token carries two conflicting roles passes no financial act.**
- **One held employee, or one without a service regime, no longer holds back the agency's whole payroll,** and the fraud rules run on real data.
- **The channel's confirmation is recorded by hand by two people,** so a batch can reach «مسدَّدة» (PLAN-64 builds on it).
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P11** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**Why this plan exists** (`docs/PAY_001_GAP_STUDY.md`: G-PAY-08, G-PAY-09 except PLAN-54's part, G-PAY-15, the reviewer's note on conflicting roles, and the manual channel confirmation of G-PAY-16):
- **There are several ways for money to leave:**
  - a partial disbursement accepts an `Approved` run, makes a batch and sends its orders at once, with no review record, no poster, no fraud scan and no gate;
  - the treasury file is a GET that exports every line's whole net, ignores holds, funding and the batch's state, and can be generated again without limit.
- **«Four eyes» is an id in a form.** A hold's release takes «the approver» from the form or the body, and the service only checks that two GUIDs differ. A fraud alert resolved without a second approver makes the resolver the approver. An arrears adjustment takes `approverId` from the request.
- **The threshold is declared and never enforced:** one approval moves a run to `Approved`. The review record is opened and closed by approvers, and `auditor` cannot reach payroll.
- **Roles given in Keycloak directly pass no check,** so a user holding the processor, approver and poster roles passes every policy.
- **One held employee stops the whole batch,** and so does one employee without a service regime for the calculation. The fraud inputs are placeholders: FR-01 and FR-08 flag every correction line.
- **No batch ever reaches «مسدَّدة»:** the ack, settle and reject commands are called by integration tests only.

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder. There is no second clone and there are no locks.
- **The main folder** is `/home/omar/HRManagement` on the owner's Linux notebook, where PLAN-53..57 ran. If the owner opened this session elsewhere, that folder is the main folder: record it. A folder's path is never a reason to stop.
- **PLAN-57 is merged** (Preconditions).

**Complete execution** (owner, 2026-11-12, standing since PLAN-51): the owner wants every plan carried out in full, however long it takes.
- Time is never a reason to skip, defer or allowlist anything.
- Only what the plan itself assigns elsewhere stays out: its «Out of scope».
- Where the plan speaks in general, this prompt makes it concrete, and all of it is in scope:
  - **the one path:** `PaymentBatchIssuer`, the posting that makes the batch and its orders, the funding decision after the posting, `process` and `submit-held`, the partial disbursement removed, and the architecture test;
  - **the bank file:** `PaymentBatchFile`, its builder, its download, its reissue, and the treasury exports removed;
  - **four eyes:** `PayrollDualControlRequest`, its service, its expiry, its inbox and API, and every client-supplied actor id removed;
  - **the approvals and the review:** the double approval above the threshold, `Payroll.AuditReview`, the review's screens and API through the gateway, and the SQL fixture retired;
  - **the conflicting roles:** `PayrollRoleConflictRequirement`, the import guard, and the test persona;
  - **holds per employee:** the gate's per-employee answer, `Held` orders, the service-regime blocker per employee, real fraud inputs, and the held report;
  - **the channel's confirmation:** `ack`, and settlement and rejection by four eyes;
  - **one migration,** `PAY058_DisbursementGovernance`, proved as the migrator;
  - **what PLAN-55..57 handed to this plan** (P-5, P-11);
  - **every new action names its policy,** every new controller declares its org-scope stance (PLAN-52), every new view passes PLAN-67's interface guard, and every new API write validates an antiforgery token — each from its first commit;
  - **AC-PAY-58a..h,** AC-24 and AC-25 rewritten, and the four PostgreSQL integration tests.

**Standing owner instruction on merges** (PLAN-38 to PLAN-57, PLAN-66 and PLAN-67): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-18).
- **Never ask the owner whether to merge:** his instruction is the permission. Asking cost PLAN-51 2 h 09 min.

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Check formatting on the files you changed (see «Lessons»).
4. Commit, using the phase's commit message.
5. Update **`reports/plan58-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan58-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan58-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `date -u +%Y-%m-%dT%H:%M:%SZ` (on Windows, `(Get-Date).ToUniversalTime().ToString('o')`).
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Record the time spent waiting for CI, and **any time the session stood still** (a stopped turn, the usage limit, the machine asleep or restarted, a full disk, a blocked port, GitHub), each with its cause.
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session in Claude Code at `xhigh`). PLAN-50 stopped once at the usage limit of the owner's plan. Economy is about tokens, never about doing less of the plan:
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only; root-causing separate failures.
- Run **one independent review**, before the server phase, so that the server build carries its fixes. Use a refuter only for findings rated high.
- If the owner switches the session's multi-agent mode on, use it for one read-only verification of the review's fixes, two agents at a time.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan58/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.
- Build the acceptance images once per code state. Run the unit and integration filters before a stack run, and rerun only the classes that failed.

**Long commands.** A foreground command that outlives the tool's time limit ends the turn. So:
- Run anything that can outlive the limit in the background or detached, with its log under `artifacts/plan58/`, and poll it. That covers the unit suite, the Testcontainers step, an image build, a stack start, an acceptance chain, the local CI steps and the server build.
- **Keep every wait short: poll a background job with calls of about five minutes at most, never one call near the tool's limit.** PLAN-55 and PLAN-56 stopped three times during one long wait on a job (9 h 38 min, 7 min, 12 min), each ended only by the owner's «Try again». PLAN-57 polled in short calls and no turn ended on a wait.
- **A wait that returns is not the end of anything: the very next action is another wait or check, in the same turn.** A turn ends only with the final reply, `BLOCKED:` or `WAITING-CI:`.
- Run **one heavy job at a time**: never an image build beside a host test run or a browser check (P-2).
- Stop a process **by its id**, never by a pattern: PLAN-53's watch loop killed its own shell with a pattern that matched itself, and a build filled the disk.

**Waiting — for CI and for your own work.**
- **Never end the turn to wait** for CI or for work you started (a review, a build, a stack, a long check). Wait for it in the turn, as PLAN-49, PLAN-50 and PLAN-57 did.
- **For CI:** run `gh pr checks <n> --watch --interval 60` in the foreground with a time limit of about five minutes. When it returns because of the limit, run it again, until every check has finished. A call that returns at the limit is not a refusal: run it again.
  - Do not lean on a background watch whose end may never wake you: PLAN-52 stood still 4 h 51 min after its run had finished.
- **Then act at once:**
  - every check green → merge (P-18), with no question to the owner;
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

You are a senior .NET engineer for payroll systems that must be correct, auditable and safe with people's money:
- one path for money to leave, enforced by an architecture test, from the posting to the bank file;
- a bank file built from the orders it pays, once per sending, with a control count and total, stored and never regenerated;
- maker-checker as two requests from two sessions: the actor always from the token, never from the client;
- approval thresholds, an independent pre-posting review, and separation of duties between creator, approvers, reviewer, funding decider and poster;
- an authorization requirement that refuses a token carrying two conflicting roles, with its audit line;
- holds per employee: a held order outside the sending and the file, released by four eyes, then sent alone by a supplementary file;
- fraud rules on real inputs;
- the transactional outbox (PLAN-40); EF Core 10 with PostgreSQL: migrations under forced row security, filtered unique indexes, optimistic concurrency on `xmin`;
- ASP.NET Core MVC and Web API behind the YARP gateway: named policies, module filters and their overrides, gateway path rules, antiforgery on API writes;
- architecture tests with ceiling lists that only shrink; acceptance tests on Docker Compose; PowerShell (ASCII, 5.1-compatible syntax, run by `pwsh` 7 here) and bash.

You work on the `HRManagement` repository on the owner's Linux notebook (P-2), signed in to GitHub with push rights, and you operate the demo server through `ssh hrm-vps`, as PLAN-36..57 did.

**Where things stand** when this prompt was written (2026-11-26):
- **`main` is `9b069f65`:** PLAN-57 merged through PR #57 at 2026-11-26T09:33:59Z. Its PR's checks were green on the first run. Its merge note is on the local branch `docs/plan-57-merge-note` of the owner's machine. Read it, and PLAN-57's final report.
- **A side branch of the owner's, `ops/ghcr-release-images`** (one commit, `d6b6880e`, 2026-11-26T09:41Z, not on `main` when this was written):
  - it lets `deploy.yml` build the 17 release images in GitHub Actions and push them to GHCR, without deploying (`action=build`);
  - the deploy and rollback jobs are untouched. It follows the owner's word in PLAN-57: «move to GHCR as a change of its own after the merge»;
  - **never touch it.** If it is on `main` when P10 starts, and `docs/OPS_VPS_SINGLE_HOST.md` documents a deploy path that uses it, you may follow that path (record it). Otherwise build on the server as PLAN-57 did.
- **The demo server runs PLAN-57's tag `4eccb1f269a0`** (`/opt/hrm/current-tag`; deployed 2026-11-26T04:53Z, previous `e64a0f7e65be`): your rollback target.
  - 21 containers healthy; **70 GB free.** PLAN-57's build fell from 78 GB to 50 GB at its lowest (about 28 GB at its peak);
  - **the loopback registry was wiped on the owner's word** («امسح وأكمل», PLAN-57): it holds the running release and `e64a0f7e65be` only (3.6 GB). **A rollback further back is a rebuild from git;**
  - `hrm_seq_data` 47.4 GB, with a retention of 30 days that starts to delete on 2026-11-30;
  - its last backup is `/opt/hrm/backups/pre-plan57-20261126T025930Z`;
  - **PLAN-57's checks closed the attendance of the unit `HR-ATT` (17 employees) for August 2026, by design for good,** with two corrections that cancel each other. The earlier plans' checks left their records (cancelled runs of September 2028, rejected reversal requests, retired and rejected accounts, two `LAW_50_2004_CHK54` versions). None of these is to be touched;
  - **the demo cannot calculate a new run today:** its 425 pay profiles carry no legal regime, and the service-regime gate refuses once the calculation has begun (P-10 changes this);
  - **its realm's people:** `hradmin` (the bootstrap administrator), `e2e-hr-admin`, `e2e-payroll-processor` and `e2e-payroll-approver`, and the other e2e personas (`testdata/e2e/personas.json`, 9). No poster, no second approver and no auditor.
- **PLAN-57 took 13 h 14 min to its merge** (started 2026-11-25T20:19:57Z), of which 14 min stood still: one `BLOCKED:` on the server's disk, answered by the owner. No turn ended on a wait.
  - Its local waits: a solution build 4.5–5.7 min; the full Testcontainers step about 15 min; the 16 acceptance images 22 min and the migrator 20 min; a prod-like stack start about 14.5 min, four times; the prod-like acceptance job 40 min, the lab 21 min, leastpriv 9 min; the local CI steps 43 min.
- **The owner's machine** (PLAN-53..57 progress files):
  - Linux 6.14, 15 GB RAM with a 4 GB swap; PowerShell 7.7 at `~/bin/pwsh`, with `~/bin/powershell` linked to it as CI has; .NET SDK 10.0.400;
  - `/` is 233 GB, with 9 to 50 GB free across the five plans. Docker also holds other projects' images, containers and volumes, and the owner's dev containers: never touch them;
  - images are built one service at a time from local base images; **the migrator image through `docker compose build`** (no buildx here, so the job's `docker build` cannot read `COPY --exclude`);
  - **`scripts/ci-local.ps1` cannot run there:** the same `scripts/ci/steps.json` runs natively with `scripts/ci/run-steps.sh --all` in a fresh clone of HEAD, every step not run named, and `scripts/plan035-prepush.ps1` accepts those by name;
  - **its compiler servers go stale** (Razor parse errors on unchanged files): run the local CI steps with `UseSharedCompilation=false`, `UseRazorBuildServer=false`, `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0`, and `dotnet build-server shutdown` after such a failure;
  - **the owner's dev container `hrm-keycloak` restarts about every minute:** Chromium then meets `ERR_NETWORK_CHANGED`, and stalls over HTTP/3. The browser checks launch Chromium without QUIC, probe page by page (`Ux067PageProbe.ProbeEachAsync`), and never run beside a build;
  - **its memory under a stack start:** PLAN-55's prod-like stack failed three starts at a load of 54 with the swap full;
  - a full unit run was once aborted by a test host crash; it passed when rerun with `--blame`;
  - port 22 reached the server without the VPN, but refused this machine for 8 minutes after many ssh connections (PLAN-56); `kubectl` is absent (`Ac42i` fails locally only); the job's Chromium step uses `--with-deps` (sudo): use the Chromium in `~/.cache/ms-playwright`.
- **The side PRs** about the E2E Playwright install (#40, #42), if still open: never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI today:**
  - a PR run has 15 checks (about an hour). One acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - the required checks on `main` are `CI`, `CodeQL`, `Gitleaks`, `E2E` and `Acceptance`. The last step of `Acceptance` («Every stage job succeeded») fails when the image build or any stage failed;
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - a new acceptance class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs. The `prodlike` job runs the stages `journey`, `main`, `isolated` and `s2s42` on one stack. **The acceptance project runs one class at a time**, in an order that changes from run to run;
  - AC-23, AC-24 and AC-25 are required (lab); `AcIam52BranchScopeTests`, `AcIam53SalaryChangeTests`, `AcPay54LegalFrameworkTests`, `AcPay55BankAccountTests`, `AcPay56RunStateMachineTests` and `AcLva57AttendancePeriodTests` are required (prod-like);
  - **the committed evidence of AC-23..25 is still the «Fail» of the run before PLAN-53** (D-QA-20: nothing of a local run is committed). This plan rewrites AC-24 and AC-25: their evidence of record becomes the PR run's artifact;
  - **the build and test steps live in `scripts/ci/steps.json`;** `CiStepsParityTests` keeps `ci.yml` in line with it;
  - **red on `main` and not this plan's:** `scripts/pol049-validate.ps1` (28 findings, PLAN-67's reshaped catalog), and AC-12 is `Blocked` on the lab stack (PLAN-59). Record them; do not chase them.
- **From PLAN-51..57** (their final reports and readiness notes):
  - **policies:** `Payroll.Process`, `Payroll.Approve`, `Payroll.Post`, `Payroll.Reverse`, `Payroll.RunCancel`, `Payroll.MasterData`, `Payroll.ChangeCancel`, `Payroll.LegalAttest`, `Payroll.Repair` (`HrManagementRoles.cs`), with their rules in `PayrollPolicyRules` and `HrManagementAuthorizationExtensions`. `hr-admin` was dropped from `Payroll.Post` and `Payroll.Reverse` outside demo mode (PLAN-51, D-IAM-4);
  - **the tests of reach:** `PayrollWriteActionsTests` fixes each Payroll write's policy and who reaches it, role by role; `WriteActionsHaveExplicitPolicyTests` refuses a write with no explicit write policy;
  - **module overrides** (PLAN-50, PLAN-51): `[ModulePolicyOverride(policy)]` swaps the module's policy for a named one in `HrModuleAuthorizationFilter`. `ModuleOverridesAreDeclaredTests` refuses an override on a write action itself unless it is an approval policy, and every write under an override names its own policy;
  - **the gateway:** `GatewayPathAuthorizationMiddleware` requires `Module.Payroll` for every mutating payroll path (about `:339-344`). `GatewayApproverReachTests` is the pattern for a role that reaches a few paths only;
  - **antiforgery on every new API write:** a `GET …/antiforgery` endpoint per API controller (`MessagingRepairAntiforgery.Issue`). `AcceptanceHarness.CreatePayrollClientAsync` carries a Payroll token, accepted by every Payroll API write;
  - **PLAN-55's pinning:** a line's destination is `SalaryLine.BankAccountId`, never the profile and never «the current account». `BankAccountPinning.DisbursedLineIdsAsync` is the one definition of «disbursed»; `RetiredAndNotReleasedAsync` is the rule the gate and the file share. A retirement makes an `ACCOUNT-RETIRED:` hold bound to the run. A line with no account is legal, listed by the readiness report, and **«PLAN-58 decides what the batch does with it»**;
  - **PLAN-56's machine:**
    - `PayrollRunTransitions`; a batch that has not left is `Voided`, and its draft orders `Cancelled`. Both enums are **stored by name** (`HasConversion<string>()`). `PaymentBatchTransitions.HasNotLeft` and `PaymentOrderTransitions.HasLeft` draw the line between money that left and money that did not;
    - `PayrollRun.PostedBy/PostedAt`, and `KnownPoster` for older runs;
    - **holds are untouched by a rejection, cancellation, restart or reversal.** «After a reopen a hold of the rejected version still stands; decide there [PLAN-58] whether a reopened run keeps or re-evaluates its holds»;
    - **a run with a funding decision or a partial batch is refused by the cancellation and the reversal** (`PayrollRunLifecycle.HasFundingOrPartialDisbursementAsync`). «When PLAN-58 says what its arrears become, lift that refusal there»;
  - **PLAN-57's handover:**
    - absence rows with negative days are refunds (a deduction component with a positive amount); carried rows are named «فرق غياب عن فترة yyyy-MM»;
    - **the gate's warning is an open audit record that the posting ignores even closed** (`PayrollAttendanceGate.IsWarning`), and the screens do not close it;
    - **«two regular runs of one agency calculating at once both mark a free deduction or adjustment»** (its §14, item 2): it closed this for attendance rows only, with `xmin` on `AbsenceDeduction`. It is this plan's (P-11);
  - `IPayrollCalendar` gives «today» in Damascus; **a calculation path** passes `PayrollInterimGate.RefusalForRun`; PLAN-57's attendance gate runs before `BeginCalculating`.
- **The code today** (read on `9b069f65`; the plan's line numbers predate PLAN-53..57, so find code by its content if a line moved):
  - **The partial disbursement:**
    - `ExecutePartialDisbursementHandler` (`Features/PartialDisbursement/ExecutePartialDisbursementCommand.cs`) accepts an `Approved` or `Posted` run (`:73`), and calls `PaymentOrderCoordinator.EnsureAndSubmitOrdersAsync` (`:144`);
    - `PartialDisbursementService` makes its batch (`:134`);
    - `CreateFundingDecisionCommand` accepts an `Approved` or `Posted` run (`:28`) and takes `ApprovedBy` from the request (`:15`);
    - `PayrollApiController` exposes `POST runs/{id}/funding-decisions` (`:244`) and `POST runs/{id}/partial-disburse` (`:262`).
  - **Who makes a `PaymentBatch`:**
    - the posting (`PayrollRunHandlers.cs` about `:256`);
    - the partial disbursement (above);
    - the seeder (`DemoPayrollSeeder.cs:481`);
    - **`ArrearsSettlementService.Settle`** (`:60`, `:117`), which builds a batch in memory that no product path saves: its only callers are `Pay004ArrearsTests`. Paying arrears in a later cycle is PAY-I's (wave 2).
  - **Who sends orders:** `EnsureAndSubmitOrdersAsync` is called by the partial disbursement and by `ProcessPaymentBatchHandler` (`PayrollRunJobHandlers.cs:481`, the call about `:542`). Its gate `PaymentDisbursementGate.EvaluateAsync` (about `:522`):
    - **blocks the batch** for any active hold of the run or with no run (`:24`);
    - for a retired pinned account (`RetiredAndNotReleasedAsync`, `:46`);
    - for any open critical alert (`:54-65`).
  - **The ack, settle and reject handlers** (`PayrollRunJobHandlers.cs:551`, `:582`, `:617`) are called by integration tests only.
  - **The treasury exports:** `PayrollReportsController.ExportTreasuryFile` (`:255`) and the treasury part of `ExportExternalIntegrationBundle` (`:313`), through `PayrollExternalExportService`.
  - **Client-supplied actors:**
    - `PaymentHoldsController.Release` takes `releaseApprovedBy` or the form's `ReleaseApprovedBy` (`:75-90`), and sets an alert's `ApprovedBy` (`:105`);
    - `PaymentHoldsApiController.Release` takes `ApproverUserId` from the body (`:25-39`);
    - `PaymentHoldService.ReleaseHold` checks only that two GUIDs differ (`:22-26`);
    - `FraudAlertsController.Resolve` reads the form's `ApprovedBy` (`:101-133`);
    - `ArrearsController.Update` takes `approverId` (`:95-102`).
  - **The approvals:**
    - `PayrollRun.Approve` (`PayrollRun.cs:249`) moves to `Approved` at the first approval;
    - `ApprovePayrollRunHandler` (`PayrollRunHandlers.cs:126`);
    - the posting checks the latest approval only (`:183-195`, `PayrollSodGuard.ValidatePayrollRunAction`);
    - `SodMatrixDefaults.DefaultPayrollThreshold` = 50,000,000 (`:9`), with three conflicting pairs (`:165-199`). `SeparationOfDutiesService` has `GetRequiredApprovalCount` (`:29`), `RequiresDualApproval` (`:45`), `ValidateRoleAssignment` (`:109`) and `RolesConflict` (`:136`). Payroll calls none of the first two.
  - **The review record:** `PayrollAuditRecordsController` (`PayrollExtendedControllers.cs:368`):
    - `Create` and `CloseAudit` are `Payroll.Approve`;
    - the closer is not the opener (`AuditorId`);
    - PLAN-57's warning cannot be closed (`PayrollAttendanceGate.IsWarning`);
    - **the posting needs a closed record that is not a warning.** The acceptance classes make one with the test-only `scripts/acceptance-payroll-audit-fixture.ps1` (used by `Ac25FraudHoldTests` and `AcPay56RunStateMachineTests`);
    - `auditor` is in no payroll policy.
  - **Conflicting roles:** Auth.Directory's grant goes through `RoleAssignmentGuard` (`AuthUsersController.cs`). The realm import reads the personas in `scripts/keycloak/render-realm.ps1` (wrapped by `scripts/prepare-keycloak-import.ps1` since PLAN-39; the plan's line numbers in the wrapper predate that). `scripts/sod-report.ps1` lists the same three pairs. **`SecurityAudit.EventTypes` has `BulkExport`, and no `RoleConflict` yet.**
  - **The service-regime gate:**
    - `PayrollRegimeGovernance` refuses the whole run with a count, through `IHrCoreServiceRegimeClient.GetPayrollBlockersAsync` (HR.Core's `ServiceRegimePayrollBlockService`);
    - it is called by the calculator (`PayrollRunParallelCalculator.cs:49`), after the run moved to `Calculating`;
    - `FakeHrCoreServiceRegimeClient` (PLAN-34) can block one employee in tests.
  - **The fraud inputs** (`FraudScanInputBuilder.cs:18-36`): `HasAttendance` and `HasSourceRecord` come from «has an input snapshot» (so every correction line, which has none, is flagged), `HireDate` from the active profile, and `Iban` from the pinned account (PLAN-55).
  - **Holds with no run:** PAY-029's review holds and PLAN-55's `PAY055-ACTIVATION` (`InboundEventHandlers.cs`); today they block every batch.
  - **The enums of money, stored by name:** `PaymentBatchStatus` (… Voided 9), `PaymentBatchKind`, `PaymentOrderStatus` (Draft, Submitted, Acknowledged, Rejected, Settled, PartiallySettled, Cancelled), `ArrearsLedgerStatus` (Pending, PartiallyPaid, …).
  - **Personas** (`testdata/acceptance/personas.json`, 68):
    - agency A: `e2e-payroll-processor`, `e2e-payroll-approver`, `acc-a-payroll-approver-2`, `acc-a-payroll-poster`, `acc-a-payroll-reverser`, `acc-auditor` (`auditor`), `acc-a-directory-checker` (`hr-admin`);
    - agency B: a processor, an approver and `acc-b-auditor`;
    - no persona holds two conflicting payroll roles.
  - **The prod-like stack's months** (agency A): the seed posts every month from 2025-08 to 2026-07 and holds 2026-08 to 2027-02. AC-LVA-57 takes 2025-01..07, AC-PAY-56 2028-01..06, AC-PAY-55 2029-05..06, AC-PAY-54 2031 on, AC-IAM-53a the current month. **No class and no seed uses 2030.** Agency A's runs have about 1,100 lines.
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43). If this plan's migration moves data, lift `FORCE` inside it as `PAY055` does, and prove it as the migrator.
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role (PLAN-54..57's migration tests are the pattern).
  - **A new PostgreSQL test uses the connection strings of `PostgreSqlFixture`,** whose idle connections close after seconds. Make sure CI's Testcontainers filter picks up your new classes.
  - **The prod-like stack runs `ASPNETCORE_ENVIRONMENT=Acceptance`,** so it does not load `appsettings.Production.json`. A value in a Production file must be mirrored in `docker-compose.acceptance-prodlike.yml`.
  - **An enum stored by name breaks an older reader on a value it does not know** (PLAN-56). A new table's enums are stored as integers (PLAN-57, D-P57-3). A new value of an enum already stored by name (an order's `Held`) is something the previous release cannot read: say so in the rollback note (P-17).
  - **Personas:**
    - a persona change needs `scripts/acceptance-reset.ps1` before the first start (the personas' hash is in the seed pack's fingerprint);
    - **never write `personas.json` while an image is building** (PLAN-53 built one image again for it);
    - a new persona also changes `DATA-AUTH` and the tests that read `personas.json`; a user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh`.
  - **The classes of an acceptance group run one at a time in an order that changes from run to run.** Before any payroll calculation on the prod-like stack, call `AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync`. A class never relies on what another class did or did not do, and leaves nothing that changes another class's work (P-13).
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - **Formatting:** before every commit, run `dotnet format whitespace HRManagement.slnx --verify-no-changes --no-restore --include <the files you changed>`. CI runs the full check.
  - **Acceptance tests:** a test posts the URL and the token the page renders (PLAN-44); an API write needs the antiforgery token (PLAN-47, PLAN-53).
  - **CodeQL runs in CI only:**
    - a new API `POST` or `PUT` without `[ValidateAntiForgeryToken]` fails `cs/web/missing-token-validation` (PLAN-53). So does an existing one that starts to write something new (PLAN-56's cancel-by-job): give it the token check, and update its clients;
    - a value whose name reads as sensitive (account, password, secret, token, IBAN) that flows into a log, an audit text or a stored field fails `cs/cleartext-storage-of-sensitive-information`, even when it is an opaque key (PLAN-55). Name an identifier by what it is. **This plan writes bank files and order references: never put an account number in a log, an audit text or an error;**
    - a log line that writes a value built from the request is `cs/log-forging`;
    - never answer CodeQL with a baseline entry or an ignore attribute.
  - **Gitleaks reads every commit of the branch:** a literal that looks like a key fails the check even if a later commit removes it (PLAN-56). Never write such a literal; never rewrite history after a push.
  - **The outbox guard matches a marked method by its name** (PLAN-57's `CloseAsync`): two methods with one name confuse it.
  - **The machine** (P-2): the disk (PLAN-53 filled it once); its memory under a stack start (PLAN-55); stale compiler servers; never delete build outputs while a step uses them; helpers outside the tree; never change `~/.ssh/config` in the middle of a run; after the host sleeps, the Docker VM's clock may lag (PLAN-50).
  - **The session may stand still.** Keep the progress file's next step exact, and go from the pre-push check straight to the push and the PR, in the same turn.
  - **The server:**
    - **measure what holds its disk before asking anything** (PLAN-57 found the registry at 53.7 GB beside Seq). Check the free space before the build, and watch it during the build (P-17);
    - **keep ssh connections few:** watch a server build from one process on the server, read its log rarely, never poll in a tight ssh loop (PLAN-56's 8-minute refusal);
    - run the server build detached (PLAN-44, PLAN-53), and the server suites with nothing heavy beside them (PLAN-45);
    - **a deploy may stop at `keycloak-reconcile` after PostgreSQL is recreated** (PLAN-40, PLAN-54, PLAN-56), leaving the application containers created and not started: run the same deploy again at once;
    - if the server build fails because the provider's resolvers answer SERVFAIL, wait and retry; never change the server's resolver configuration (PLAN-50);
    - the server suites write into the committed `acceptance-evidence/` and `reports/`: restore those files, or move them under `artifacts/plan58/vps/` (PLAN-48);
    - a server check cleans up what it leaves open, and decodes a page before it compares Arabic text (PLAN-49);
    - **Production and the demo keep the password grant closed:** a script that signs in there uses a signed-in session's cookie or a bearer token (PLAN-55);
    - older server checks that fail or flicker from this machine only (PLAN-55..57 §11–12). Record them, rerun them alone, and do not chase them:
      - `AC_POL_47_vps_the_migration…`;
      - the workflow sweeps check, which passes alone just after a sweep cycle;
      - `AC_OPS_39_vps_e_Keycloak…`;
      - the browser tours, on `ERR_NETWORK_CHANGED`.
  - **Before the push, and before any fix pushed to the release branch:**
    - run the unit suite (its tests scan every source file) and the format check. After a test host crash, rerun it with `--blame`;
    - compare the seeded counts of `scripts/acceptance-verify.ps1` on your local stack, by psql;
    - run the full Testcontainers step on the host with CI's filter.
  - **The E2E suite runs in CI only.**
  - **The architecture project's first run after a build** takes about 2 min 25 s, outside the tests (PLAN-51).
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools, or a script that reads and writes UTF-8 without a byte order mark.
  - The acceptance services have no container health check: readiness means `/health/ready`.

## Read first (mandatory)

1. **The specification:**
   - `docs/acceptance-plans/PLAN-58-pay-disbursement-governance.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
   - `docs/PAY_001_GAP_STUDY.md`: G-PAY-08, G-PAY-09, G-PAY-15, G-PAY-16, and the PAY-F row.
2. **These reports:**
   - `reports/plan57-final-report.md` (§3, §12, §14, §16) and its merge note;
   - `reports/plan56-final-report.md` (§15, the note for PLAN-58) and `reports/plan55-final-report.md` (§15);
   - `reports/plan51-final-report.md` (D-IAM-4, the explicit policies) and `reports/plan50-final-report.md` (the module overrides and the gateway's approver reach).
3. **These documents:**
   - `docs/PAY_001_APPROVAL_BYPASS.md`, `docs/EXT_003_PAYMENT_ORDER_BINDING.md`, `docs/FRD_001_FRAUD.md`, `docs/PAY_004_ARREARS.md`, `docs/SOD_MATRIX.md`, `docs/PAYROLL_RULES.md`;
   - the rows D-PAY-2..5, D-PAY-9, D-PAY-19 and D-PAY-20 of `docs/OWNER_DECISIONS_REGISTER.md`.
4. The code named in «The code today».
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **No other plan session is running,** and you are in the main folder (see «How to run»), on `main` or on `docs/plan-57-merge-note`.
2. **`git fetch origin`**, and `origin/main` holds PLAN-57: the merge of `release/plan-57` is in `git log origin/main --merges --oneline -5`.
3. **The working tree is clean,** except for these, which may be present:
   - the owner's local edits to `global.json` and `tmp-vendors.html`, if any;
   - git-ignored files (`artifacts/`, `.env`, the local key rings of the dev scripts);
   - untracked prompt files under `docs/acceptance-plans/execution/`, this one included.

   Anything else uncommitted: leave it untouched, record it, and go on if it does not touch this plan's files. `BLOCKED:` only if it does. Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
4. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
   - **If port 22 is blocked** from the owner's network, this precondition does not stop P0. Record it, take the server's read-only picture at the start of P10, and go on with the local phases.
5. **`gh auth status`** shows an account with push rights to `omardefdev/HRManagement` and the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

The owner accepted the plan's defaults on 2026-10-31 (the wave 1 survey; the rows of `OWNER_DECISIONS_REGISTER.md`). If the owner changed one in a message before P0 starts, his message wins; record it.

- **D-PAY-2, disbursement before the posting:** no. A funding decision is taken on a `Pending` batch after the posting only, and `process` is the poster's. AC-24 changes and is rewritten.
- **D-PAY-3, who reviews before the posting:**
  - the holder of `auditor` (`Payroll:Audit:ReviewerRoles`, default `auditor`), who is not the run's creator, approver or poster;
  - `Payroll:Audit:Required=true`. An agency may turn it off by its own decision, setting it `false` with a line in the run's log;
  - `auditor` reaches the payroll review screens, to read and to close, and nothing else.
- **D-PAY-4, the scope of a hold and of the service-regime blocker:** the employee (`Payroll:Blockers:Mode=HoldEmployee`). His line is calculated and his order held, and the others are paid. `BlockRun` (today's behaviour) stays an agency setting.
- **D-PAY-5, the double approval:** `Payroll:Approval:DualApprovalThreshold` = 50,000,000 (`SodMatrixDefaults.cs:9`). Above it, two different holders of `Payroll.Approve`. A chain of named roles (finance manager, then the disbursing officer) is wave 2 (WF).
- **D-PAY-19, reissuing the bank file:** refused, except on a request with a reason approved by a holder of `Payroll.Approve` who is not the requester. The new file carries a new sequence number and voids the one before.
- **D-PAY-20, a token with two conflicting roles:** refused in every financial act (`Payroll:Sod:RejectConflictingRoleHolders=true`), by the pairs of `SodMatrixDefaults.cs:165-199`.
  - A user who holds both processor and approver neither processes nor approves until his roles are corrected;
  - **`hr-admin` counts as holding the three roles wherever it passes the payroll policies** (the plan's §5). Outside demo mode that is `Payroll.Process` and `Payroll.Approve`; **on the demo, where `hradmin` keeps every payroll policy, it is refused in every financial act** (P-16).

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-58-pay-disbursement-governance` from `origin/main` (plan 0.1), in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-57's merge note from `docs/plan-57-merge-note`. If that branch is gone, rebuild the record from GitHub (the PR, its runs, the merge commit), and say so;<br>&nbsp;&nbsp;– commit this prompt file. If the owner pasted it into the session rather than saving it, write it from the session's text first, as PLAN-54..57 did;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **The machine (Linux): awake, the disk, the memory, Docker, the ports.**<br>• **Keep the machine awake for the session, changing no setting:** `systemd-inhibit --what=sleep:idle --who=plan58 --why="PLAN-58" sleep infinity`, detached. Record its PID in `artifacts/plan58/keepawake.pid`, and stop it **by that PID** at the end. If the machine restarts, start it again and record it.<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• **Remove only this repository's leftovers:** compose projects `hrm-*`, Testcontainers, this plan's images and this repository's build cache. Never prune, stop or remove another project's images, containers or volumes, or the owner's dev containers.<br>• **The disk:** read `df -h /` and `docker system df` before every image build and every stack start.<br>&nbsp;&nbsp;– Build an image only with at least 12 GB free. Below that, prune this repository's build cache and remove this plan's unused images first;<br>&nbsp;&nbsp;– a watch that stops a build near 2 GB free stops it **by its PID**;<br>&nbsp;&nbsp;– if 12 GB cannot be reached without touching other projects: `BLOCKED:`, one line — the owner frees space.<br>• **The memory, before a stack start:**<br>&nbsp;&nbsp;– nothing heavy beside it: no build, no test host, no browser, no idle stack of this repository;<br>&nbsp;&nbsp;– read `uptime` and `free -m` first, and start only when the load is below the number of CPUs;<br>&nbsp;&nbsp;– a start that fails from the load (a health check timing out, a 401 during the identity sync) is retried after the load falls, three times at most, each failure and its cause recorded;<br>&nbsp;&nbsp;– wait for each start in the turn, in short polls («Long commands»).<br>• **The images:** one service at a time from the local base images (`acceptance-up.ps1 -UseLocalBaseImages`, and the helpers PLAN-53..57 kept outside the tree); the migrator through `docker compose build`. This plan changes Payroll, the gateway's shared code, Auth.Directory's guard and the realm: rebuild every image the change reaches, and the migrator. Never commit a helper.<br>• **One heavy job at a time:** never an image build beside a host test run or a browser check. Close what you opened before a full unit run (a browser, an idle stack).<br>• **After an image build,** check that every service image was rebuilt from this tree. A failed build leaves stale images, and a `-NoBuild` run on them proves nothing.<br>• **Before each stack start,** check the stack's host ports with `ss -ltn` and `docker ps`. A port held by something that is not this repository's: `BLOCKED:` with the port.<br>• **The local CI steps:** `scripts/ci/run-steps.sh --all` natively in a fresh clone of HEAD, with the compiler servers off (see «Where things stand»), naming each step not run and why.<br>• **If this session runs on Windows again,** use PLAN-67's forms of these checks, and record it. |
| P-3 | **The baseline (plan 0.2–0.5),** into `reports/plan58-baseline.md`.<br>• **0.2:** `dotnet build HRManagement.slnx -warnaserror`; the unit tests filtered on `Pay001`, `Pay004`, `Frd001`, `Ext003` and `SeparationOfDuties` (the plan's), then `Pay055`, `Pay056` and `Lva057`. Green, or a list.<br>• **The inventories,** generated by search, never typed, into `artifacts/plan58/inventory.*`, and summarised:<br>&nbsp;&nbsp;– **0.3, the money paths:** every `new PaymentBatch`, every call of `EnsureAndSubmitOrdersAsync`, and every point that exports a payment file (`ExportTreasuryFile`, `ExportExternalIntegrationBundle`, `PayrollExportJobService`). Each is deleted or routed to the one path;<br>&nbsp;&nbsp;– **0.4, actors from the client:** every parameter or form field in Payroll that carries an «approver», «releaser» or «decider» id from the client;<br>&nbsp;&nbsp;– **0.5, the personas:** the real roles of `personas.json`, and any conflicting combination (none expected but the test persona this plan adds).<br>• **From this prompt:**<br>&nbsp;&nbsp;– every acceptance and E2E class that approves or posts a payroll run, with its run's total against the threshold on its stack. On the prod-like stack, agency A's runs (about 1,100 lines) are far above 50,000,000, so each such class meets the double approval;<br>&nbsp;&nbsp;– every user of `scripts/acceptance-payroll-audit-fixture.ps1`;<br>&nbsp;&nbsp;– every test or table that says `hr-admin` reaches a payroll financial act (`PayrollWriteActionsTests`, `AcIam51ExplicitPolicyTests`, the persona reach tables): D-PAY-20 changes them;<br>&nbsp;&nbsp;– every reader of a set of order, batch or arrears statuses (this plan adds `Held` and voids arrears);<br>&nbsp;&nbsp;– every hold with no run, and what makes it;<br>&nbsp;&nbsp;– the demo's picture (P-16). |
| P-4 | **Policies, stances and the API's surface.**<br>• **The policies:**<br>&nbsp;&nbsp;– a funding decision → `Payroll.Approve`;<br>&nbsp;&nbsp;– `process`, `submit-held` and `ack` → `Payroll.Post`;<br>&nbsp;&nbsp;– a file's download → `Payroll.Post` or `Payroll.Approve`;<br>&nbsp;&nbsp;– each four-eyes kind: its request and its decision carry the policy the plan or the act requires (a hold release, a critical alert, an arrears adjustment, a reissue, a settlement, a rejection), and the decider holds the kind's decision policy;<br>&nbsp;&nbsp;– the review record → the new `Payroll.AuditReview` (roles from `Payroll:Audit:ReviewerRoles`);<br>&nbsp;&nbsp;– the inbox and the held report → `Module.Payroll`, filtered to what the caller may decide.<br>&nbsp;&nbsp;`PayrollWriteActionsTests` gains each new write, with who reaches it role by role, conflicts included.<br>• **The stances:** actions added to Payroll's existing controllers are covered by their rows. A new controller (the inbox, the files, the channel confirmation, if you make them their own) declares `[OrgScopeCentralOnly]`: a batch belongs to the whole agency.<br>• **The API:**<br>&nbsp;&nbsp;– `POST api/payroll/payment-batches/{id}/funding-decisions` (replacing the run's), `…/process` (exists), `…/submit-held`, `…/ack`, `…/settlement-requests`, `…/rejection-requests`;<br>&nbsp;&nbsp;– `GET api/payroll/payment-batches/{id}/held` and `GET …/files/{fileId}`;<br>&nbsp;&nbsp;– `GET` and `POST api/payroll/dual-control` (the requests and their decisions);<br>&nbsp;&nbsp;– `POST api/payroll/runs/{id}/audit-records` and `POST api/payroll/audit-records/{id}/close`;<br>&nbsp;&nbsp;– a `GET …/antiforgery` on each new API controller.<br>&nbsp;&nbsp;**Every new `POST` carries `[ValidateAntiForgeryToken]`,** and so does any existing API write whose behaviour this plan changes (`process`, the funding decision, the hold release).<br>• **`partial-disburse` and the treasury exports are removed** and answer 404. Their absence is listed for clients.<br>• **The HTTP answers:**<br>&nbsp;&nbsp;– a broken rule or a state the act cannot leave → 400, as the payroll acts answer today. This covers a funding decision on an `Approved` run, the requester deciding his own request, the first approver approving again, a settled amount that does not match, and a release of `REGIME-BLOCKER` while HR.Core still blocks;<br>&nbsp;&nbsp;– a policy refusal, the processor running `process`, a conflicting role holder, or the approver closing a review → 403. The conflict's message names the pair;<br>&nbsp;&nbsp;– a second pending request of one kind and target, two concurrent decisions or processings (`xmin` and the unique indexes), or a file asked for twice for one sending → 409, or the same file again, never a second one and never a 500;<br>&nbsp;&nbsp;– another agency's batch, run, request or file → 404.<br>&nbsp;&nbsp;Every refusal is an Arabic message, and changes nothing. |
| P-5 | **One payment path after the posting (plan §1).**<br>• **`PaymentBatchIssuer`** (`Payroll.Application/Services/`) is the only maker of a `PaymentBatch` and its orders, with an explicit source (`PayrollRunPosted` here; PLAN-64 adds its final settlement through the same interface).<br>&nbsp;&nbsp;– **The posting calls it in its own transaction:** a `Pending` batch, and one `Draft` order per line to the line's pinned account (PLAN-55). A line with no account gets a `Held` order with the reason `NO-ACCOUNT`: that is PLAN-55's open question answered;<br>&nbsp;&nbsp;– **the seeder** creates its batches at construction through one named way, which the architecture test allows there only, as PLAN-56 did for a run's status;<br>&nbsp;&nbsp;– **`ArrearsSettlementService.Settle`** builds a batch in memory for its tests: bring it under the same rule without changing what `Pay004ArrearsTests` mean. Paying arrears in a later cycle stays PAY-I's.<br>• **The funding decision** (`CreateFundingDecisionCommand`), `Payroll.Approve`, the decider from the token:<br>&nbsp;&nbsp;– on a `Pending` batch of a `Posted` run only;<br>&nbsp;&nbsp;– it spreads the orders' amounts by the priority rules of the run's pinned framework version (PLAN-54), and records the rest in `ArrearsLedgerEntry` as today.<br>• **`ExecutePartialDisbursementCommand` and `PartialDisbursementService.ExecuteAsync` are deleted,** with the `partial-disburse` endpoint. A partial disbursement is a funding decision, then `process`. `PartialDisbursementOrchestratorTests` moves to the new path, its meaning kept.<br>• **`ProcessPaymentBatchHandler`:**<br>&nbsp;&nbsp;– the executor is not the creator, any approver, or the funding decider;<br>&nbsp;&nbsp;– the fraud scan, then the gate per employee (P-10): held employees' orders `Held`, the others `Submitted`, then the file (P-6), in one request;<br>&nbsp;&nbsp;– it calls `RetiredAndNotReleasedAsync` (PLAN-55), so a retired account is never paid.<br>• **`submit-held`** sends the orders whose hold was released after the batch was sent, with a supplementary file. A held employee is paid after his release with no new batch.<br>• **The plan's rules:**<br>&nbsp;&nbsp;– no order is `Submitted` for a run that is not `Posted`, and none is sent without a fraud scan and a gate in the same request;<br>&nbsp;&nbsp;– **unheld orders + held orders + arrears = the run's `TotalNetPay`,** checked at every transition of the batch.<br>• **PLAN-56's refusal is lifted for a funding decision on a batch that has not left:** a reversal then voids the batch, cancels its orders, and closes the run's open arrears rows with the reversal as their reason, since nothing of them was paid. A batch that has left stays refused (D-PAY-16).<br>&nbsp;&nbsp;Partial batches made by the removed path before this plan keep PLAN-56's refusal; the inventory says whether any exists.<br>• **A reopened run's holds** (PLAN-56's question): a hold bound to a run stays until its four-eyes release. The re-calculated run meets it in its gate, per employee. Record it.<br>• **`Pay058SinglePaymentPathArchitectureTests`** (extending `Pay001ApprovalBypassTests`):<br>&nbsp;&nbsp;– no `new PaymentBatch` outside the issuer and the named seed construction;<br>&nbsp;&nbsp;– no call of the issuer but from `PostPayrollRunHandler`;<br>&nbsp;&nbsp;– no `EnsureAndSubmitOrdersAsync` outside `ProcessPaymentBatchHandler` and `submit-held`;<br>&nbsp;&nbsp;– each with a planted negative. |
| P-6 | **A bank file from the orders, once, with control totals (plan §2).**<br>• **`PaymentBatchFile`:** `PaymentBatchId`, `Sequence`, `Kind` (Initial, HeldRelease, Reissue, stored as an integer), `ControlCount`, `ControlTotal`, `Sha256`, `Content` (bytea), `GeneratedBy`, `GeneratedAt`, `SupersededById`, `ReissueReason`, `ReissueApprovedBy`. Unique on (batch, sequence).<br>• **`PaymentBatchFileBuilder`** (`Payroll.Infrastructure/Integration/`) builds today's CSV (D-PAY-9) from the orders `Submitted` **in this sending only,** to their pinned accounts, with a closing line of the count and the total.<br>• **Generated inside `process` and `submit-held`, one file per sending, in the same transaction.** A second request for the same sending gives the same file, never a second one.<br>• **The download** (`GET …/files/{fileId}` and a button on the batch's page) reads what is stored and never generates. It writes `SecurityAudit` as `BulkExport`. No account number reaches a log, an audit text or an error.<br>• **A reissue** is a four-eyes request of kind `BankFileReissue` (D-PAY-19): a new sequence, the previous file `SupersededById`.<br>• **`ExportTreasuryFile` and the treasury part of `ExportExternalIntegrationBundle` are removed:** no other path exports a payment file. |
| P-7 | **Four eyes as two requests from two sessions (plan §3), and the channel's confirmation (plan §7).**<br>• **`PayrollDualControlRequest`:**<br>&nbsp;&nbsp;– `Kind` (HoldRelease, CriticalAlertResolve, ArrearsAdjust, BankFileReissue, BatchSettle, BatchReject), stored as an integer;<br>&nbsp;&nbsp;– `TargetId`, `PayloadJson` (the reason, and the amount for arrears and settlements);<br>&nbsp;&nbsp;– `RequestedBy`, `RequestedAt`, `Status` (Pending, Approved, Rejected, Expired), `DecidedBy`, `DecidedAt`, `DecisionNotes`;<br>&nbsp;&nbsp;– one pending request per kind and target (a filtered unique index), and `ConfigureOptimisticConcurrency`.<br>• **`PayrollDualControlService`:** `RequestAsync` and `DecideAsync`.<br>&nbsp;&nbsp;– The actor of both comes from the token only;<br>&nbsp;&nbsp;– the decider is not the requester, holds the kind's policy, and holds no conflicting pair (P-9);<br>&nbsp;&nbsp;– the effect (the release, the resolution, the arrears entry, the reissue, the settlement, the rejection) happens inside `DecideAsync`, in the same transaction.<br>• **Expiry:** `Payroll:DualControl:ExpiryHours` (default 72). An expired request is `Expired`, with no effect.<br>• **Removed from the client:** `ReleasePaymentHoldCommand.ApproverUserId`, the form's field, the arrears' `approverId`, and the alert's `ApprovedBy`. No actor id comes from the client anywhere in Payroll (0.4).<br>• **The inbox:** `Views/DualControl/Inbox.cshtml` and the API — «طلبات بانتظار قرارك» for the policy's holders, without what the user asked for himself.<br>• **The plan's rules:**<br>&nbsp;&nbsp;– resolving an alert that is not critical stays one act; a critical one needs a request and a decision;<br>&nbsp;&nbsp;– releasing a `REGIME-BLOCKER` hold also needs HR.Core's client to answer «not blocked» for that employee at the moment of the decision.<br>• **The channel's confirmation** (plan §7):<br>&nbsp;&nbsp;– `POST …/ack` is one act with the channel's reference (`Payroll.Post`);<br>&nbsp;&nbsp;– a settlement and a rejection are four-eyes requests (`BatchSettle`, `BatchReject`) with the bank statement's reference and the settled amount;<br>&nbsp;&nbsp;– `SettlePaymentBatchHandler` and `RejectPaymentBatchHandler` are called from `DecideAsync` only, and move the batch's sent orders together;<br>&nbsp;&nbsp;– **a settled amount that is not the sum of the sent orders is refused,** and the batch stays as it was. Per-order partial settlement, importing the channel's notices and statements, and matching by reference are PAY-I's (wave 2). |
| P-8 | **The double approval above the threshold, and an independent review before the posting (plan §4).**<br>• **`PayrollRun.Approve`** takes the required count from `sod.GetRequiredApprovalCount("PayrollRun", TotalNetPay)`, with the threshold from `Payroll:Approval:DualApprovalThreshold`. The run stays `UnderReview` until the count is met by different approvers, and shows «اعتماد 1 من 2». A rejection by either reopens it (PLAN-56). `PayrollRunTransitions` keeps its guard: the first approval is not a transition.<br>• **`ApprovePayrollRunHandler`:** the second approver is neither the first nor the creator. The poster is none of the approvers: `PayrollSodGuard` checks every approval, not the latest.<br>• **`Payroll.AuditReview`** (`HrManagementPolicies`, `HrManagementAuthorizationExtensions`), its roles from `Payroll:Audit:ReviewerRoles`.<br>• **`PayrollAuditRecordsController`:**<br>&nbsp;&nbsp;– it opens and closes with `Payroll.AuditReview`. The closer is not the run's creator, approver or poster, and is still not the opener;<br>&nbsp;&nbsp;– PLAN-57's warning stays unclosable and is never the review;<br>&nbsp;&nbsp;– **`[ModulePolicyOverride(Payroll.AuditReview)]` goes on the controller, and each write names `Payroll.AuditReview` itself:** `ModuleOverridesAreDeclaredTests` allows an override on a write action only for an approval policy.<br>• **The gateway** lets `Payroll.AuditReview` through to `/ui/payroll/PayrollAuditRecords/*`, `GET /api/payroll/runs/{id}`, and the two audit-record API paths only, as `GatewayApproverReachTests` does for approvers. A test proves the reach and its limits.<br>• **Two reviewers per posting:** the opener and the closer differ, and agency A has one auditor (`acc-auditor`). Add `acc-a-auditor-2` (`auditor`, agency A) for the scenarios (P-13). The SQL fixture is deleted once AC-25 and AC-PAY-56 use the API. |
| P-9 | **No token with two conflicting roles reaches a financial act (plan §5).**<br>• **`PayrollRoleConflictRequirement` and its handler** (`Payroll.Web/Authorization/`), added to `Payroll.Process`, `Payroll.Approve`, `Payroll.Post`, `Payroll.Reverse` and `Payroll.AuditReview` in Payroll:<br>&nbsp;&nbsp;– a token with a pair that `SeparationOfDutiesService.RolesConflict` names → 403 with the pair in the message;<br>&nbsp;&nbsp;– a `SecurityAudit` line of a new type `RoleConflict`, with no secret and no token in it;<br>&nbsp;&nbsp;– `Payroll:Sod:RejectConflictingRoleHolders` (default `true`). Turned off by a documented decision only, and then every pass of a conflicting holder still writes its line.<br>• **`hr-admin`** counts as holding the three roles wherever it passes the payroll policies (D-PAY-20). Every test that said it reaches those acts changes, its meaning moving to «reaches the policy, refused by the conflict».<br>• **The import guard** goes where the personas are read now (`scripts/keycloak/render-realm.ps1`, wrapped by `prepare-keycloak-import.ps1`): a persona with a conflicting pair is refused unless it carries `"sodConflictFixture": true`. It has its own negative test.<br>• **The test persona `acc-a-payroll-multirole`** (`payroll`, `payroll-processor`, `payroll-approver`, `payroll-poster`, `sodConflictFixture: true`, agency A) in `personas.json` and `AcceptancePersonaCatalog.cs`. Update whatever counts the personas (the tests that read `personas.json`, `DATA-AUTH`). |
| P-10 | **Holds and the service-regime blocker per employee, and real fraud inputs (plan §6).**<br>• **`PaymentDisbursementGate`** answers with `HeldEmployees` (the employee, the reason, the reference) instead of a block.<br>&nbsp;&nbsp;– An active hold of this run or with no run, an open critical alert of the employee, and a retired pinned account (PLAN-55) each hold that employee's order only;<br>&nbsp;&nbsp;– a critical alert with no employee still stops the batch, as today.<br>• **`PaymentOrderStatus.Held` and `PaymentOrder.HoldReason`:** a held order is outside the sending and the file. `HasLeft` and PLAN-55's «disbursed» do not count it.<br>• **A hold with no run** (PAY-029's review holds, `PAY055-ACTIVATION`) affects that employee's orders in every batch, never the other employees'.<br>• **The service-regime blocker** (`PayrollRegimeGovernance`, `Payroll:Blockers:Mode`):<br>&nbsp;&nbsp;– in `HoldEmployee`, everyone is calculated, and each blocked employee gets a `PaymentHold` of type `REGIME-BLOCKER` with his name and the reason HR.Core gives;<br>&nbsp;&nbsp;– the approval screen lists the held people by name, so approving the run includes knowing them;<br>&nbsp;&nbsp;– **in `BlockRun`, the refusal comes before `BeginCalculating`,** so the run is left as it was. Today's gate refuses after the run moved, which leaves it «قيد الحساب» (PLAN-56 §13, PLAN-57's attendance gate shows the place).<br>• **`FraudScanInputBuilder`:**<br>&nbsp;&nbsp;– FR-01: `HasAttendance` from PLAN-57's receipts of the period, `null` when there is none, so no alert (the attendance gate handles a missing receipt);<br>&nbsp;&nbsp;– FR-08: a correction line carries its source by `SourceRunId`, so `HasSourceRecord = true`;<br>&nbsp;&nbsp;– FR-04: the pinned account (PLAN-55);<br>&nbsp;&nbsp;– FR-05: `ExpectedNetPay` = the employee's net in the last posted regular run;<br>&nbsp;&nbsp;– FR-07: `HireDate` = the earliest `EffectiveFrom` of the employee's profiles.<br>• **The held report:** `GET …/payment-batches/{id}/held` and a card on the batch's page — the held people by name, their reasons, and their pending requests.<br>• **The plan's rules:**<br>&nbsp;&nbsp;– a release goes through P-7, then `submit-held` sends the order with a supplementary file;<br>&nbsp;&nbsp;– **`NO-ACCOUNT` and `ACCOUNT-RETIRED` need no release request:** verifying the employee's account is already two people's act (PLAN-55). On its activation, the account is pinned on the line and the unsent order (`SalaryLine.BankAccountId`, `PaymentOrder.BeneficiaryIban`), recorded in `EntityAuditLog`, and the hold lifted; then `submit-held`. This is the plan's one deliberate exception to D-PAY-15's pinning, for a held order only: say so in the code and the report. |
| P-11 | **PLAN-57's handover: two runs marking one free input.** «Two regular runs of one agency calculating at once both mark a free deduction or adjustment». PLAN-57 closed it for attendance rows with `xmin` on `AbsenceDeduction`.<br>• Give `DeductionRecord` and `PayrollAdjustment` the same guard. The second run's save of a package that marks an input already taken then fails as a conflict, and its partition is retried or the job fails, as an input-fingerprint conflict does today (PLAN-56's restart).<br>• A PostgreSQL test proves it with two runs of two months that both see one free deduction.<br>• **Every amount of a run with no such race is unchanged** (P-19). |
| P-12 | **The migration `PAY058_DisbursementGovernance`.**<br>• The tables `PaymentBatchFiles` and `PayrollDualControlRequests`, with `TenantId`, row security through `TenantRlsMigrationHelper` and the grants, as `PAY055`; their unique and filtered indexes.<br>• `PaymentOrders.HoldReason`; the new `Held` value (stored by name, like the others of that column); the concurrency of P-11; the run's approvals as the double approval needs them; the new arrears state of P-5 if you add one.<br>• **New tables store their enums as integers** (PLAN-57). A new value in a column stored by name is a rollback matter (P-17).<br>• No data move is expected. If one appears, lift `FORCE` as `PAY055` does and prove it as the migrator.<br>• No byte order mark; `dotnet ef migrations has-pending-model-changes -p src/Services/Payroll/Payroll.Infrastructure -s src/Services/Payroll/Payroll.Web` reports none.<br>• **`Pay058MigrationPostgreSqlTests`:** as the migrator, a non-superuser under forced row security, the new tables keep each agency's rows to itself. |
| P-13 | **Acceptance placement (PLAN-41's rules).**<br>• **`AcPay58DisbursementGovernanceTests`** (AC-PAY-58a..h): `prodLike[]`, `required: true`, group `main`; `[RequiredAcceptance]` and `EvidenceRecorder("AC-PAY-58")`. It does not stop or recreate containers. Its steps run in order, as `AcPay56`'s do.<br>• **Its months:** months of **2030**, which no class and no seed uses, governed by the seed's v1. A precondition shows that no run of agency A covers them. A rerun on the same stack stops there with a clear message, and needs a fresh stack. `ClassifyRecordsAddedSinceTheSeedAsync` comes before each calculation.<br>• **Its people** (agency A): `e2e-payroll-processor`, `e2e-payroll-approver`, `acc-a-payroll-approver-2`, `acc-a-payroll-poster`, `acc-auditor`, the new `acc-a-auditor-2` and `acc-a-payroll-multirole`. Their scenarios gain AC-PAY-58 (Lessons: reset).<br>• **Every posting goes through the review by API, by two auditors.** No SQL fixture remains.<br>• **The scenarios** are the plan's table, with the answers of P-4:<br>&nbsp;&nbsp;– **58b:** the release is asked by `e2e-payroll-approver` and decided by `acc-a-payroll-approver-2`, each in his own session;<br>&nbsp;&nbsp;– **58d:** one auditor opens, the other closes; the approver closing → 403; an auditor posting to `PayrollRuns/Approve` → 403;<br>&nbsp;&nbsp;– **58f:** the employee's account is registered and verified by PLAN-55's two people;<br>&nbsp;&nbsp;– **58g:** the shared pinned account is made by PLAN-55's flow for two employees, then retired at the scenario's end;<br>&nbsp;&nbsp;– **58h:** the poster acks, asks for the settlement with the statement's reference, and the approver approves.<br>• **AC-24 and AC-25 are rewritten** on the new behaviour, their meaning kept: a partial disbursement is a funding decision after the posting; a fraud hold holds its employee's order, and the others are sent. Their evidence of record becomes the PR run's artifact.<br>• **The other classes that approve or post a run** (P-3's list: AC-PAY-56, AC-PAY-55, AC-IAM-53, Ac12, Ac23, …) meet the double approval and the review: each adds the second approval and the two auditors where its run needs them, its meaning kept.<br>• **Isolation** (the classes run one at a time, in an order that changes): the class leaves nothing in agency A that another class's run or batch would meet:<br>&nbsp;&nbsp;– no active hold, with or without a run;<br>&nbsp;&nbsp;– no open critical alert;<br>&nbsp;&nbsp;– no shared or unverified account;<br>&nbsp;&nbsp;– no pending four-eyes request;<br>&nbsp;&nbsp;– no free input with an amount.<br>&nbsp;&nbsp;It asserts on its own runs, batches and employees by id.<br>• **`ac-ownership.json`:** `AC-PAY-58` (owner PLAN-58, `provesClaim: true`, its reason), and AC-24 and AC-25's owner notes.<br>• **The evidence of record is the PR run's artifact** (D-QA-20). Copy what the report needs into `artifacts/plan58/`, then restore the committed evidence. |
| P-14 | **Local testing (owner's rule of 2026-11-06).** Run only the classes this plan touches, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`, and the overlay groups with `-Stage prodLike:<group>`. **The full suite runs once, in CI.**<br>• **prod-like:** `AcPay58DisbursementGovernanceTests`, `AcPay56RunStateMachineTests`, `AcPay55BankAccountTests`, `AcPay54LegalFrameworkTests`, `AcIam53SalaryChangeTests`, `AcLva57AttendancePeriodTests`, `AcIam50ApproverReachDelegationTests`, `AcIam51ExplicitPolicyTests`, `AcIam52BranchScopeTests`, `AcUx67ReadableInterfaceTests`, `AcHcm25ProductionApprovalTests`, the prod-like journey, and the `isolated` group. `AcUx67` adds to its key pages: the inbox, a batch's page with its files and held card, the review records' list and form, and a run's page with «اعتماد 1 من 2»;<br>• **lab:** `Ac12PayrollSodTests`, `Ac21PayrollDeterminismTests`, `Ac23PayrollFreezeTests`, `Ac24PartialDisbursementTests`, `Ac25FraudHoldTests`, `AcceptanceV2CatalogTests`, `AcHcm29HireContractTests`, the lab journey;<br>• **leastpriv:** `AcOps38FirstBootTests` and `AcTen48LeastPrivilegeTests` (the migration and the realm);<br>• **E2E:** in CI only.<br>Then compare the seeded counts of `scripts/acceptance-verify.ps1` by psql. Known local-only failures (`Ac42i` without `kubectl`) are recorded, not chased. |
| P-15 | **Scripts.**<br>• Every new or changed `.ps1` is ASCII-only, 5.1-compatible in syntax, and runs here with `pwsh -NoProfile -File …`.<br>• **`scripts/pay058-validate.ps1`** has a `-SelfTest` with planted defects, as `pay056-validate.ps1` does. It checks, at least:<br>&nbsp;&nbsp;– no `new PaymentBatch` outside the issuer and the named seed construction, and no `EnsureAndSubmitOrdersAsync` outside its two callers;<br>&nbsp;&nbsp;– no `partial-disburse`, `ExportTreasuryFile` or treasury bundle left;<br>&nbsp;&nbsp;– no actor id read from a form, a query or a body in Payroll;<br>&nbsp;&nbsp;– the conflict requirement on the five policies; no persona with a conflicting pair but the fixture;<br>&nbsp;&nbsp;– no `acceptance-payroll-audit-fixture.ps1` and no use of it;<br>&nbsp;&nbsp;– the migration without a byte order mark;<br>&nbsp;&nbsp;– the settings mirrored in the prod-like overlay;<br>&nbsp;&nbsp;– every new `POST` with `[ValidateAntiForgeryToken]`;<br>&nbsp;&nbsp;– `AcPay58DisbursementGovernanceTests` required in `prodLike[]`.<br>• **The settings** (`Payroll:Audit:*`, `Payroll:Blockers:Mode`, `Payroll:Approval:DualApprovalThreshold`, `Payroll:Sod:RejectConflictingRoleHolders`, `Payroll:DualControl:ExpiryHours`) go in Payroll's appsettings with their defaults, and are mirrored in `docker-compose.acceptance-prodlike.yml`.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile <ProdLike or Lab> -UseLocalBaseImages`. |
| P-16 | **The demo server facts this plan must respect.**<br>• **It runs Production in demo mode** (`Deployment__Mode=demo`). Its realm has `hradmin`, `e2e-hr-admin`, `e2e-payroll-processor` and `e2e-payroll-approver`: no poster, no second approver, no auditor. **The checks add no user,** so nothing on the demo is posted, funded, processed or settled. Record what the demo would need for a posting: a poster, a second approver above the threshold, and two auditors.<br>• **D-PAY-20 on the demo:** `hradmin` keeps every payroll policy in demo mode, so the conflict requirement refuses it in every financial act. The payroll people act instead.<br>• **D-PAY-4 on the demo:** a calculation now succeeds and holds every one of its 425 employees (`REGIME-BLOCKER`, none has a legal regime). The checks calculate nothing. Record it, with the data task it points to (classifying the demo's regimes).<br>• **P0's read-only picture:**<br>&nbsp;&nbsp;– the running tag and the free disk;<br>&nbsp;&nbsp;– the runs, batches, orders, holds, alerts, funding decisions, arrears rows and review records, by status;<br>&nbsp;&nbsp;– a checksum of the runs, lines, batches and orders, never printing an amount or a number of an account.<br>• **What the owner will see change:**<br>&nbsp;&nbsp;– «طلبات بانتظار قرارك»;<br>&nbsp;&nbsp;– a batch's page with its files, its held card and its confirmation acts;<br>&nbsp;&nbsp;– the review records for the auditor;<br>&nbsp;&nbsp;– «اعتماد 1 من 2» on a run above the threshold;<br>&nbsp;&nbsp;– the hold release and the alert resolution as requests;<br>&nbsp;&nbsp;– `hradmin` refused in payroll's financial acts, with the conflict named;<br>&nbsp;&nbsp;– the treasury file and the partial disbursement gone;<br>&nbsp;&nbsp;– **nothing else moves:** no run, line, batch, order or amount changes. |
| P-17 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan58-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Before the deploy, read-only:** P-16's picture.<br>• **The disk:** at least 30 GB free before a build on the server (70 GB after PLAN-57). Watch it during the build from one process on the server, and stop the build **by its PID** below 3 GB. Remove only the local images of releases older than the running one and the previous one; the registry keeps two releases since PLAN-57. Never trim Seq's volume.<br>• **Build** once, on the final code, detached (or through GHCR if `main` documents that path by then: «Where things stand»). Then `scripts/vps/deploy.ps1 -Tag <sha12>` (run it again at once if it stops at `keycloak-reconcile`). The migration runs at start-up.<br>• **The checks' own data** (P10): one four-eyes request on an existing demo record (an arrears entry), made by one payroll person and **rejected** by the other, so that nothing changes. No demo run is calculated, approved, posted, funded, processed, cancelled or reversed.<br>• **Rollback safety:** find out what PLAN-57's tag does with this plan's data, and record it in `OPS_VPS_SINGLE_HOST.md`:<br>&nbsp;&nbsp;– it ignores the new tables;<br>&nbsp;&nbsp;– **it cannot read an order stored as `Held`** (a name it does not know);<br>&nbsp;&nbsp;– it brings back the partial disbursement, the treasury file and the client-supplied approvers.<br>&nbsp;&nbsp;So no disbursement act while the older release runs, and once a `Held` order exists, the way back is a fix forward. The registry holds only two releases: a rollback further back is a rebuild from git.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn:<br>&nbsp;&nbsp;– roll back to the previous tag, or fix forward;<br>&nbsp;&nbsp;– restore the backup only if data was damaged;<br>&nbsp;&nbsp;– prove the demo works, and record the downtime. |
| P-18 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P11, after the local CI steps and the pre-push check are green, in the same turn as the pre-push check (Lessons).<br>2. `gh pr create`, then wait («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, run the unit suite and the format check, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>`, and record it.<br>5. `main` is strict: if another PR merges first (the owner's GHCR branch, for one), merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-19 | **No regression of PLAN-25, PLAN-36..57, PLAN-66 and PLAN-67.** These stay green:<br>• **every existing payroll result, unit for unit:** the golden pack tests (`Gov002LegalGoldenPackTests`), `PayrollEnginePipelineTests`, the determinism tests, `PayrollFinancialInvariantsTests`, `Pay029*`, `Pay023FrozenRunIsolationTests`, `Pay004ArrearsTests`, `Pay056*`, `Lva057*`, AC-21 and AC-23;<br>• `Frd001*`, `Ext003*` (the treasury pack now comes from the bank file), `Pay055*` and the SoD tests, their meaning kept where this plan changes them;<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests`;<br>• `AcIam50`..`AcIam53`, `AcPay54`..`AcPay56`, `AcLva57`, `AcUx67`, `AcWf43`..`AcWf46` with the orphan class, `AcPol47`, both `AcTen48` classes, both `AcPol49` classes, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, `AcHcm29`, and both recruitment journeys;<br>• the architecture tests (`PayrollWriteActionsTests`, `WriteActionsHaveExplicitPolicyTests`, `ModuleOverridesAreDeclaredTests`, `GatewayApproverReachTests`, `OrgScopeCoverageTests`, `InterfaceTextGuardTests`, `GuardSelfTests`, `PayProfileWritersTests`, `PayrollReferenceWritersTests`, `Pay054FrameworkWritersTests`), `OutboxPublishOrderingArchitectureTests`, `Rel004CompatibilityTests`, `ProductionWiringMatrixTests`, `CiStepsParityTests`;<br>• the validate scripts `lva057`, `pay056`, `pay055`, `pay054`, `iam053`, `iam052`, `iam051`, `iam050` and `ux067`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. `pol049-validate.ps1` is red on `main` already: record its state before and after. |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-54, done:** the separation of duties of the legal framework.
- **PLAN-56, done:** the reversal, its approval and the voided batch. **PLAN-40:** the atomicity of `Save→Publish` in the processing.
- **Wave 2 (PAY-I):** importing the channel's notices and the bank's statements, matching by reference, per-order partial settlement, paying arrears in the next cycle, and recovering money already settled. This plan's §7 is the manual record by two people.
- **Wave 2 (PAY-J):** FR-02 (the civil register), FR-03 (the national number's fingerprint), FR-06 (the approver's volume), and the proof-of-life task.
- **Wave 2 (WF):** an approval chain of named roles through the workflow engine.
- **Wave 2 (IAM-G):** separating `admin` from financial acts entirely, a report of conflicting users in Keycloak, and syncing roles. PLAN-51 already dropped `hr-admin` from `Payroll.Post` and `Payroll.Reverse` outside demo mode.
- **PLAN-64:** the end-of-service calculation and its payment, which goes through this plan's path.
- **PLAN-59:** the full cycle with production settings, raising the attendance gate, and the run's scope against hire dates.

**Never commit:**
- the owner's local edits (`global.json`, `tmp-vendors.html`) — check `git diff --cached --name-only` before every commit;
- `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- another plan's untracked prompt file, or a helper of the local image builds;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- a screenshot or any other image, or a bank file;
- any secret, token or password (templates keep their placeholders, `__SVC_SECRET__` or `${…}`), or a literal gitleaks would read as one.

**Never:**
- push to `main` or force-push, or rewrite a branch's history after it was pushed;
- merge with a pending or red check;
- touch the side PRs (#40, #42), the owner's branch `ops/ghcr-release-images`, or their branches;
- disable host-key checking;
- widen a policy, a scope or a role to make a page or a test work, or turn off `RejectConflictingRoleHolders` or `Payroll:Audit:Required` to get green;
- add a row to any ceiling list: they only shrink;
- take an actor's id from a form, a query or a body;
- let money leave by any path but the posting, then `process` or `submit-held`, or generate a bank file outside them;
- write an account number into a log, an audit text, an error or a URL;
- release a hold or resolve a critical alert by one person;
- add users to the demo's realm, or post, fund, process or settle anything there;
- touch another project's Docker images, containers or volumes, or the owner's dev containers;
- print a password, or rotate a secret that exists on the server;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** (port 22 at P0 excepted: Preconditions 4).
- **An existing payroll result would change,** and the cause cannot be removed without changing the plan's design.
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning.
- **12 GB free for an image build cannot be reached** without touching other projects, **or a port the tests read is held** by something else (P-2).
- **The server's free space is under 30 GB** after P-17's removals.
- **Port 22 is still blocked when P10 starts:** one line asking the owner to restore it.
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, keep-awake, preconditions, baseline

1. Write the `Started (UTC)` line (see «Timing»). Start the keep-awake process (P-2). Check the Preconditions.
2. `git switch -c feature/plan-58-pay-disbursement-governance origin/main`. Bring in PLAN-57's merge note, and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan58-baseline.md` (P-3), and the demo's picture (P-16).
4. If port 22 is blocked, record it, and take the demo's picture at the start of P10.

Commit: `chore(pay058): baseline, every money path, every actor taken from the client, the personas' roles, the classes that approve or post, the demo's picture`.

## P1 — One payment path (plan §1, P-5)

- **`PaymentBatchIssuer`;** the posting's batch and orders; the funding decision after the posting; the partial disbursement removed; `process` and `submit-held`; the seeder and `ArrearsSettlementService`; PLAN-56's refusal lifted for an unsent funded batch.
- **`Pay058SinglePaymentPathArchitectureTests`** with its planted defects.
- **Tests:**
  - the posting's one transaction;
  - `NO-ACCOUNT`;
  - a funding decision on an `Approved` run → 400;
  - the processor → 403;
  - the sum rule at every transition;
  - a reversal of a funded, unsent batch.

Commit: `feat(pay058): money leaves by one path — the posting makes the batch and its orders, a funding decision comes after it, and only process or submit-held sends; the partial disbursement is removed`.

## P2 — The bank file (plan §2, P-6)

- **`PaymentBatchFile`, its builder, its download and its reissue;** the treasury exports removed.
- **Tests:** a file per sending with its control count and total; the same sending asked twice gives one file; the supplementary file; the reissue's sequence and supersession; the download reads and writes `BulkExport`; no account number in a log.

Commit: `feat(pay058): one bank file per sending, built from the sent orders with control totals, stored and never regenerated; the treasury exports are removed`.

## P3 — Four eyes, and the channel's confirmation (plan §3, §7, P-7)

- **`PayrollDualControlRequest`, its service, its expiry, its inbox and API;** every client-supplied actor removed; `ack`, and settlement and rejection as requests.
- **Tests:**
  - every kind: requested, decided by another, refused to its requester, expired;
  - the `REGIME-BLOCKER` release while HR.Core still blocks;
  - a non-critical alert as one act;
  - a settled amount that does not match → 400 and nothing changed;
  - an actor id in a body ignored.

Commit: `feat(pay058): every four-eyes act is a request and a decision by two people from their own sessions; the channel's confirmation is recorded by hand by two people`.

## P4 — The double approval, the independent review, the gateway (plan §4, P-8)

- **The approval count above the threshold;** the handler's separation of duties; `Payroll.AuditReview`; the review records' controller with its override; the gateway's paths; the review API.
- **Tests:**
  - «1 of 2», then `Approved`; the first approver again → 400;
  - the poster against every approval;
  - the reviewer's rules;
  - the gateway's reach and its limits;
  - `ModuleOverridesAreDeclaredTests`.

Commit: `feat(pay058): a run above the threshold needs two approvers, and an auditor who made none of it reviews it before the posting, through the gateway`.

## P5 — Conflicting roles (plan §5, P-9)

- **`PayrollRoleConflictRequirement`** on the five policies, the `RoleConflict` audit, the import guard, and the persona.
- **Tests:**
  - each pair → 403 with the pair named, and its audit line;
  - `hr-admin`;
  - the setting off: a pass still audited;
  - the import guard and its fixture flag.

Commit: `feat(pay058): a token that carries two conflicting payroll roles is refused in every financial act and audited; the realm import refuses such a persona`.

## P6 — Holds per employee, the regime blocker, real fraud inputs (plan §6, P-10, P-11)

- **The gate's per-employee answer, `Held` orders, holds with no run, the regime blocker in both modes,** the fraud inputs, the held report, the account's re-pinning for a held order, and P-11's guard.
- **Tests:**
  - one held employee, the others sent;
  - a release, then `submit-held` with its file;
  - `REGIME-BLOCKER` named;
  - `BlockRun` leaving the run as it was;
  - FR-01, FR-04, FR-05, FR-07 and FR-08 on real inputs, and a correction raising neither FR-01 nor FR-08;
  - `NO-ACCOUNT` lifted by a verification.

Commit: `feat(pay058): a hold, a critical alert or a missing service regime holds that employee's order only; the fraud rules read real data`.

## P7 — The PostgreSQL proofs (the plan's integration tests)

- **`Pay058DisbursementPostgreSqlTests`:**
  - two concurrent `process` calls of one batch → one succeeds, and one file (the unique index and `xmin`);
  - two concurrent decisions of one request → one succeeds;
  - a failure injected after the orders leaves no orphan batch;
  - one employee blocked by `FakeHrCoreServiceRegimeClient` → calculated, his order `Held` as `REGIME-BLOCKER` by name, the others paid. A release before the client answers «not blocked» → refused;
  - P-11's race;
  - `Pay058MigrationPostgreSqlTests`.
- **The full integration filter** on PostgreSQL, CI's filter.

Commit: `test(pay058): PostgreSQL proofs of one file per sending, one decision per request, an atomic posting, the regime blocker per employee, and one run per free input`.

## P8 — Acceptance, then the independent review (plan Acceptance, P-13, P-14, P-15)

1. **The pieces:** `AcPay58DisbursementGovernanceTests`; AC-24 and AC-25 rewritten; the other classes' second approvals and reviews; the two personas; the fixture deleted; `ci-scope.json`, `ac-ownership.json`, `scripts/pay058-validate.ps1`.
2. **Acceptance.**
   1. Check the disk and the memory (P-2). Reset the local stacks (the personas). Build the images, one service at a time. Start the prod-like stack, and record the time to ready.
   2. Run `AcPay58DisbursementGovernanceTests` (AC-PAY-58a..h), positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   3. Then P-14's lists, `scripts/pay058-validate.ps1` (and `-SelfTest`), and the seeded counts by psql. All required classes must pass.
   4. `down -v`. Restore the committed evidence.
3. **The full Testcontainers step on the host,** with CI's filter.
4. **Independent review** (one fresh subagent), before the server, so the server build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - a way for money to leave outside the one path, or a file generated outside it, or twice for one sending;
   - an order sent without a fraud scan and a gate in the same request, or a sum that no longer equals the run's net;
   - an actor taken from the client; a four-eyes act done by one person or decided by its requester;
   - an approval count met by one person twice; a poster who approved; a reviewer who made, approved or posts the run, or who opened what he closes;
   - a conflicting role holder who passes a financial act;
   - a held employee who holds back the others, or a held order inside a file;
   - a re-pinned account outside the held-order exception;
   - an account number in a log, an audit text, an error or a URL;
   - an API write without an antiforgery token, or a name CodeQL reads as sensitive flowing into a log or audit;
   - a test changed to pass rather than to keep its meaning, or a part of the plan deferred, skipped or listed to save time;
   - something PLAN-57's tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(pay058): AC-PAY-58a..h, AC-24 and AC-25 pass; the other classes meet the double approval and the review; review fixes; CI scope and ownership`.

## P9 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved (the owner's GHCR branch, for one), merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to `PayrollDbContext` after yours, remove yours and add it again on top. Rebuild, and rerun the unit, architecture and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you, and for P10's build.

Commit (only if the merge needed fixes): `fix(pay058): main merged, conflicts resolved`.

## P10 — The demo server (P-16, P-17)

Record every server change and its undo command **as you make it**.

1. Check SSH and P-17's condition.
2. Take the backup, then **verify** it. Take the read-only picture (P-16), if P0 could not.
3. **Check the server's disk, build and deploy** (P-17). Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`, again at once if it stops at `keycloak-reconcile`. Record the downtime and the time to all healthy.
4. **Then check**, with evidence in `artifacts/plan58/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them. The new checks go in an `AcPay58VpsChecks` class. Each writes realistic Arabic text, keeps its marker in a field the reader does not rely on, and leaves nothing open.
   1. **The migration:** recorded; the new tables under forced row security; the runs, lines, batches and orders as in the picture.
   2. **The one path:** `partial-disburse` and the treasury exports answer 404.
   3. **The conflict:** `hradmin` on a payroll financial act → 403 naming the pair, a `RoleConflict` line in `SecurityAudit`, nothing changed.
   4. **Four eyes:** `e2e-payroll-processor` asks for an arrears adjustment on a demo entry. The inbox shows it to `e2e-payroll-approver` and not to the requester. The requester's own decision → 400. `e2e-payroll-approver` rejects it with a note → `Rejected`, and the entry is as it was.
   5. **The screens** open in Arabic under the enforced CSP with no violation (PLAN-67's method, Chromium without QUIC, page by page, never beside a build): the inbox, a batch's page with its files and held card, the review records' list, a run's page, and the holds' and alerts' pages without an approver field. Names, not ids.
   6. **Everyone else is unchanged:** the PLAN-36..57 server checks pass, and `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass. The checks that flicker from this machine are rerun alone.
5. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-17 says, prove the demo works, and record it.

Commit: `test(pay058): demo server with the one path, the conflict refusal and a four-eyes request refused; PLAN-36..57 server checks still green`.

## P11 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/PAY_001_APPROVAL_BYPASS.md` and `docs/EXT_003_PAYMENT_ORDER_BINDING.md`: the one path (D-PAY-2);
   - `docs/FRD_001_FRAUD.md`: four eyes, and the rules that really run;
   - `docs/PAY_004_ARREARS.md`: holds per employee, arrears on a reversal;
   - `docs/SOD_MATRIX.md`: the double approval, the reviewer, the funding decider, the conflicting holder;
   - `docs/PAYROLL_RULES.md`: the batch, the file, the confirmation;
   - the «الحالة» lines of G-PAY-08, G-PAY-09 and G-PAY-15 in `docs/PAY_001_GAP_STUDY.md`, with what is left for wave 2;
   - `docs/CI_AND_TESTING.md`: the new class, AC-24 and AC-25, the fixture's end; `OPS_VPS_SINGLE_HOST.md`: what the demo users see, the backup location, the change table, the rollback note;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local gates, before any push:**
   - the full Testcontainers step on the host, with CI's filter (P8, if nothing it builds or tests changed since; otherwise again);
   - the local CI steps (P-2) on HEAD;
   - then `pwsh -NoProfile -File scripts/plan035-prepush.ps1`.

   All must be green, with every step not run named. The report is committed before them, because a commit after a green local run needs another run.
3. **`reports/plan58-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. **Decisions applied and deviations:** the second auditor, the lifted refusal, the re-pinning exception, `ArrearsSettlementService`, the regime refusal before `BeginCalculating`, P-11, the demo's limits, and anything else. No deviation defers part of the plan.
   4. **The one path:** what makes a batch, what sends, what was removed.
   5. **The bank file:** its shape, its controls, its reissue.
   6. **Four eyes:** every kind, its policies, its effect; the channel's confirmation.
   7. **The approvals and the review:** the count, the reviewer, the gateway.
   8. **Conflicting roles:** the requirement, `hr-admin`, the import guard.
   9. **Holds per employee:** the gate, the regime blocker, the fraud inputs, the held report.
   10. The existing tests that changed, each with the meaning it keeps; the numbers that did not move.
   11. The guards and their own negative tests.
   12. Unit, architecture, integration and acceptance results; the PR run's artifact link as the evidence of record.
   13. **The server:** before and after, changes and their undo, the backup location, the rollback note, and what the demo needs for a posting.
   14. **Changes for API clients and operators:** the new API, the removed endpoints and exports, the settings, the new persona and the import guard.
   15. Items that other plans own (Scope boundaries), and defects for later plans.
   16. **Timing:**
       - total wall-clock;
       - active time, waiting for CI, and every stop with its cause;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `pwsh -NoProfile -File scripts/plan-timing.ps1 -From 25 -To 67`.
   17. **A readiness note** for PLAN-59, PLAN-64 and wave 2:
       - for PLAN-59: the full cycle through the one path, the gate settings, the people a posting needs;
       - for PLAN-64: the issuer's interface for a final settlement, and `Settled` as «paid»;
       - for PAY-I: what the manual confirmation leaves to automate.
4. **Merge (P-18), by you, in the same turn as the pre-push check:**
   1. Merge the feature branch into a new `release/plan-58` from `main` (`--no-ff`).
   2. Write `reports/plan58-pr.md` (short).
   3. `git push -u origin release/plan-58`
   4. `gh pr create --base main --head release/plan-58 --title "PLAN-58: disbursement governance — one payment path, real four eyes, and holds per employee" --body-file reports/plan58-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then:
   - `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`;
   - record the PR link, each check result, the merge commit and the post-merge run in `reports/plan58-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`;
   - commit that note on a local branch `docs/plan-58-merge-note`, as the plans before did, so the next plan brings it in. **Do not open a second PR for it;**
   - stop the keep-awake process by its PID (P-2).

## Definition of done

- **No path makes money or a bank file leave but the posting, then `process` or `submit-held`,** and the architecture test enforces it.
- **Every «four eyes» is two requests from two users,** and no actor id comes from the client anywhere in Payroll.
- **The double approval above the threshold, and the independent review by `auditor`, work through the gateway.**
- **A holder of two conflicting roles is refused in every financial act.**
- **A held or blocked employee does not stop the batch,** and is paid after his release with a supplementary file.
- **The channel's confirmation is recorded by two people,** so a batch reaches `Settled`.
- **Every existing payroll result is unchanged.**
- **Nothing in the plan was deferred to save time.**
- **The tests:** AC-PAY-58a..h, AC-24 and AC-25 = Pass (the PR run's artifact is the evidence of record); the PostgreSQL integration tests green; the PLAN-36..57 suites green.
- **The server** runs this code, with nothing paid, posted or changed by the checks.
- **The documents** P11 lists are updated, among them the «الحالة» lines of G-PAY-08, G-PAY-09 and G-PAY-15, with what is left for wave 2.
- **The merge** is done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed for payroll users, and what the owner will see on the demo (`hradmin` refused in payroll's financial acts, and why);
- what the demo would need before a run can be posted there;
- how long the plan took, how long it waited for CI, and every stop with its cause;
- the PR link;
- the path of the final report.
