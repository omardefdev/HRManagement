# Task: PLAN-57 — A closed attendance period, corrections that become differences, and a reliable handover to payroll (LVA-C)

The deliverables:
- **An attendance period that really closes:** a calendar month with one versioned summary per employee. Once an employee's month is closed, his days are locked for every writer.
- **A change after the close is a correction,** with a reason and an approver who is not the requester. It makes a new version of the summary, and payroll turns it into a **difference** applied once: in its own month if that month's run is not frozen, otherwise in the next cycle.
- **Absence is counted on working days only.** The bulk entry writes no rest day, holiday or future day. The close covers the unit's whole tree and the employees whose service ended within the month.
- **An attendance record never moves to another employee** when it is edited, and «في إجازة» comes from an approved leave only.
- **Payroll knows before a calculation** which employees' attendance is not closed, and warns or refuses as configured.
- **A written handover contract** (an event and two HTTP reads) that PLAN-59 builds on.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P10** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**Why this plan exists** (`docs/HCM_002_LEAVE_ATTENDANCE_GAP_STUDY.md`: G-LVA-03, G-LVA-10; and the attendance side of G-PAY-19 in `docs/PAY_001_GAP_STUDY.md`):
- **«Closing» a period closes nothing.** `ClosePeriodAndPublishAsync` saves nothing and publishes one event per employee with a random `TransactionId`. It takes only the unit's direct employees, leaves out everyone `Terminated`, and counts absence on every day of the month.
- **Payroll takes the first close only.** Its consumer ignores zero absence and any later version, so a correction after the close never reaches the money.
- **Anyone can write anywhere.** The writes are scattered across a controller and a service. The bulk entry writes every day of the month. The edit screen rebinds the employee, so an edit can silently move a record to someone else. `OnLeave` can be typed by hand, and the leave reconciler treats any `OnLeave` record as the truth.
- **Nothing tells payroll whether attendance is closed** before it calculates.

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder. There is no second clone and there are no locks.
- **The main folder** is `/home/omar/HRManagement` on the owner's Linux notebook, where PLAN-53..56 ran. If the owner opened this session elsewhere, that folder is the main folder: record it. A folder's path is never a reason to stop.
- **PLAN-56 is merged** (Preconditions).

**Complete execution** (owner, 2026-11-12, standing since PLAN-51): the owner wants every plan carried out in full, however long it takes.
- Time is never a reason to skip, defer or allowlist anything.
- Only what the plan itself assigns elsewhere stays out: its «Out of scope».
- **The plan's last line lets the correction API move to LVA-I «if time is short». It does not move:** time is not a reason here, and the correction API is built with the rest.
- Where the plan speaks in general, this prompt makes it concrete, and all of it is in scope:
  - **the period:** `AttendancePeriod`, `AttendancePeriodClosure`, `AttendanceEmployeeSummary`, the calendar, the pure summary calculator, the lock, the close with its rules, and its screens and API;
  - **one writer:** `AttendanceDayWriter`, the controller and its four views, the leave consumers in one save, the reconciler, and the architecture test;
  - **the correction:** the entity, the service, the screens, the API, the separation of duties, and the system corrections from leave events;
  - **the contract:** `IAttendanceSummaryPublished` through the outbox, the old event marked obsolete with no publisher, and the old consumer removed;
  - **payroll's side:** the receipt, the difference rows, the carry to the next cycle, the refund of a negative difference, the gate, its two reads and its panel;
  - **two migrations,** `LVA057_AttendancePeriods` (HR.Core) and `LVA057_AttendanceHandoff` (Payroll), each proved as the migrator;
  - **the three policies.** Every new action names its policy, every new controller declares its org-scope stance (PLAN-52), every new view passes PLAN-67's interface guard, and every new API write validates an antiforgery token — each from its first commit;
  - **AC-LVA-57a..h,** positive and negative, and the PostgreSQL integration tests the plan lists, with the performance target.

**Standing owner instruction on merges** (PLAN-38 to PLAN-56, PLAN-66 and PLAN-67): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-17).
- **Never ask the owner whether to merge:** his instruction is the permission. Asking cost PLAN-51 2 h 09 min.

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Check formatting on the files you changed (see «Lessons»).
4. Commit, using the phase's commit message.
5. Update **`reports/plan57-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan57-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan57-progress.md`. Its first line is:
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
- Write long outputs (inventories, logs, trx files) under `artifacts/plan57/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.
- Build the acceptance images once per code state. Run the unit and integration filters before a stack run, and rerun only the classes that failed.

**Long commands.** A foreground command that outlives the tool's time limit ends the turn. So:
- Run anything that can outlive the limit in the background or detached, with its log under `artifacts/plan57/`, and poll it. That covers the unit suite, the Testcontainers step, an image build, a stack start, an acceptance chain, the local CI steps and the server build.
- **Keep every wait short: poll a background job with calls of about five minutes at most, never one call near the tool's limit.** Each of the last three stops came during one long wait on a job:
  - PLAN-55 stood still **9 h 38 min** on a stack start;
  - PLAN-56 stood still **7 min** and **12 min** on two acceptance jobs that had already finished;
  - each ended only when the owner pressed «Try again».
- **A wait that returns is not the end of anything: the very next action is another wait or check, in the same turn.** A turn ends only with the final reply, `BLOCKED:` or `WAITING-CI:`.
- Run **one heavy job at a time**: never an image build beside a host test run or a browser check (P-2).
- Stop a process **by its id**, never by a pattern: PLAN-53's watch loop killed its own shell with a pattern that matched itself, and a build filled the disk.

**Waiting — for CI and for your own work.**
- **Never end the turn to wait** for CI or for work you started (a review, a build, a stack, a long check). Wait for it in the turn, as PLAN-49 and PLAN-50 did.
- **For CI:** run `gh pr checks <n> --watch --interval 60` in the foreground with a time limit of about five minutes. When it returns because of the limit, run it again, until every check has finished. A call that returns at the limit is not a refusal: run it again.
  - Do not lean on a background watch whose end may never wake you: PLAN-52 stood still 4 h 51 min after its run had finished.
- **Then act at once:**
  - every check green → merge (P-17), with no question to the owner;
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

You are a senior .NET engineer for HR and payroll systems that must be correct, auditable and safe with people's money:
- attendance periods, per-employee summaries with versions, and a lock that every writer obeys;
- a single writer for a table, enforced by an architecture test;
- maker-checker on corrections: a request and a decision by different people, checked on the server;
- event-carried state with absolute values and versions: «the latest version wins», idempotent consumers (an inbox and a unique key), out-of-order delivery, and a row lock against two versions at once;
- the transactional outbox (PLAN-40): the summaries, the close record and the events in one save;
- carrying a difference to the next payroll cycle without touching a frozen run, and refunding a negative one;
- EF Core 10 with PostgreSQL: migrations under forced row security, a data fill as the migrator, filtered unique indexes, optimistic concurrency on `xmin`;
- ASP.NET Core MVC and Web API: named policies, branch scope (PLAN-52), antiforgery on API writes, overposting;
- architecture tests with ceiling lists that only shrink; acceptance tests on Docker Compose; PowerShell (ASCII, 5.1-compatible syntax, run by `pwsh` 7 here) and bash.

You work on the `HRManagement` repository on the owner's Linux notebook (P-2), signed in to GitHub with push rights, and you operate the demo server through `ssh hrm-vps`, as PLAN-36..56 did.

**Where things stand** when this prompt was written (2026-11-25):
- **`main` is `01a8c3f9`:** PLAN-56 merged through PR #56 at 2026-11-25T19:45:49Z.
  - **Its PR's first CI run had one new CodeQL finding:** the older `POST api/payroll/jobs/{jobId}/cancel` had started to cancel the run, so CodeQL read it as a new write without an antiforgery token. The fix (`4402fd4f`) gave it the token check and a mandatory reason.
  - **Before its push, gitleaks read a test literal as a key in an earlier commit** of the branch, and the history was rewritten before the push (Lessons).
  - Its merge note is on the local branch `docs/plan-56-merge-note` of the owner's machine. Read it, and PLAN-56's final report.
- **The demo server runs PLAN-56's last deployed tag** (`/opt/hrm/current-tag`): `e64a0f7e65be` when its progress file was last written (deployed 2026-11-25T17:42:52Z, previous `69570ea4e1f6`), unless its merge note records a redeploy after the CodeQL fix (`4402fd4f227a`). Read it in P0: that is your rollback target.
  - 22 containers; **22 GB free after that deploy.** A build takes about 26 GB at its peak: PLAN-56's second build fell from 29.2 GB to **3.1 GB**, 106 MB above its watchdog's line;
  - **the volume `hrm_seq_data` holds 46.6 GB of logs and grows.** Trimming it is the owner's decision (P-16);
  - the local images of `69570ea4e1f6` and the running tag are on the server; older ones are in the loopback registry only;
  - its last backup is `/opt/hrm/backups/pre-plan56-20261125T112742Z`;
  - PLAN-56's checks left six runs of September 2028, all cancelled, and five reversal requests on the posted run of January 2026, all rejected. The earlier plans' checks left their own records. None of these is to be touched;
  - **the demo cannot calculate a new run:** its 425 pay profiles carry no legal regime, and the service-regime gate refuses once the calculation has begun, leaving the run «قيد الحساب». Its approved run (May 2026) cannot be posted;
  - **port 22 refused this machine for 8 minutes** after the many ssh connections of PLAN-56's server suites and build watch. It reopened by itself.
- **PLAN-56 took 14 h 52 min to its merge** (started 2026-11-25T04:53:25Z), of which 19 min stood still in two stops («Long commands»). Its stacks started on the first try (the prod-like about 14 min), with nothing heavy beside them.
- **The owner's machine** (PLAN-53..56 progress files):
  - Linux 6.14, 15 GB RAM with a 4 GB swap; PowerShell 7.7 at `~/bin/pwsh`, with `~/bin/powershell` linked to it as CI has; .NET SDK 10.0.400;
  - `/` is 233 GB, with 9 to 50 GB free across the four plans. Docker also holds other projects' images, containers and volumes, and the owner's dev containers: never touch them;
  - images are built one service at a time from local base images; **the migrator image through `docker compose build`** (no buildx here, so the job's `docker build` cannot read `COPY --exclude`);
  - **`scripts/ci-local.ps1` cannot run there:** the same `scripts/ci/steps.json` runs natively with `scripts/ci/run-steps.sh --all` in a fresh clone of HEAD, every step not run named, and `scripts/plan035-prepush.ps1` accepts those by name;
  - **its compiler servers go stale** (Razor parse errors on unchanged files): run the local CI steps with `UseSharedCompilation=false`, `UseRazorBuildServer=false`, `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0`, and `dotnet build-server shutdown` after such a failure;
  - **the owner's dev container `hrm-keycloak` restarts about every minute:** Chromium then meets `ERR_NETWORK_CHANGED`, and stalls over HTTP/3. The browser checks launch Chromium without QUIC and probe page by page (`Ux067PageProbe.ProbeEachAsync`). PLAN-56's screens check failed twelve times on one page while a build ran beside it, and passed alone;
  - **its memory under a stack start:** PLAN-55's prod-like stack failed three starts at a load of 54 with the swap full;
  - a full unit run was once aborted by a test host crash; it passed when rerun with `--blame`;
  - port 22 reached the server without the VPN; `kubectl` is absent (`Ac42i` fails locally only); the job's Chromium step uses `--with-deps` (sudo): use the Chromium in `~/.cache/ms-playwright`.
- **The side PRs** about the E2E Playwright install (#40, #42), if still open: never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI today:**
  - a PR run has 15 checks (about an hour; `CI` about 42 min, `E2E` about 13 min). One acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - the required checks on `main` are `CI`, `CodeQL`, `Gitleaks`, `E2E` and `Acceptance`. The last step of `Acceptance` («Every stage job succeeded») fails when the image build or any stage failed;
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - a new acceptance class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs. The `prodlike` job runs the stages `journey`, `main`, `isolated` and `s2s42` on one stack. **The acceptance project runs one class at a time** (`DisableTestParallelization`), in an order that changes from run to run;
  - AC-23, AC-24 and AC-25 are required (lab); `AcIam52BranchScopeTests`, `AcIam53SalaryChangeTests`, `AcPay54LegalFrameworkTests`, `AcPay55BankAccountTests` and `AcPay56RunStateMachineTests` are required (prod-like);
  - **the committed evidence of AC-23..25 is still the «Fail» of the run before PLAN-53** (D-QA-20: nothing of a local run is committed). A narrowed local run that leaves them out copies that evidence in and its guard names them: include them, or read the run by its class table;
  - **the build and test steps live in `scripts/ci/steps.json`;** `CiStepsParityTests` keeps `ci.yml` in line with it;
  - **red on `main` and not this plan's:** `scripts/pol049-validate.ps1` (PLAN-67 reshaped the catalog it reads; the unit tests that hold the same rules pass), and AC-12 is `Blocked` on the lab stack (PLAN-59). Record them; do not chase them.
- **From PLAN-51..56** (their final reports and readiness notes):
  - **policies:** `{Module}.Write` and `{Module}.Approve` come from `Authorization:Policies` through the `RolesPolicy` helper (D-51-2). `HrCore.Write` (`hr-admin`, `hr-core`) is on the attendance actions today. `WriteActionsHaveExplicitPolicyTests` (PLAN-51) refuses a write with no explicit write policy;
  - **branch scope** (PLAN-52): `HrOrgScopeVisibility.IsBranchScoped(user)`, `OrgScopeService.GetAllowedOrgUnitIdsAsync`, `HrCoreOrgScopeKeys`, the stances `[OrgScoped(typeof(T))]`, `[OrgScopeCentralOnly]` and `[OrgScopeNotApplicable("…")]`. `OrgScopeCoverageTests` refuses new rows. The attendance actions answer 403 for a unit outside the caller's scope today;
  - **antiforgery on every new API write:** a `GET …/antiforgery` endpoint per API controller (`MessagingRepairAntiforgery.Issue`; HR.Core's `PoliciesController` and `WorkflowDefinitionsController` have one). For tests, `AcceptanceHarness.CreatePolicyLifecycleClientAsync` carries an HR.Core token and `CreatePayrollClientAsync` a Payroll one; a token from one endpoint of a service is accepted by that service's other API writes;
  - **PLAN-56's run machine:**
    - `PayrollRunTransitions`; `Status`, `ResultsFrozen` and `ValidatedAtCalculate` have `init` accessors;
    - a rejection reopens to `Draft` at `Revision` + 1, and a restart recalculates from a new snapshot;
    - the cancellation frees the period and the inputs (`POST api/payroll/runs/{id}/cancel`, policy `Payroll.RunCancel`);
    - **`PayrollInputRelease` touches the applied markers only** (`DeductionRecord.AppliedInRunId`, `AbsenceDeduction.AppliedInRunId`, `PayrollAdjustment.PayrollRunId`), «the one place to free an absence»;
    - `PayrollNet` accepts a negative deduction total; `ValidateLineIntegrity` checks `NetPay == PayrollNet.Net(line)` and `Σ components == Unclipped`;
  - **PLAN-56's readiness note for this plan:** add `CarryForward`, `OriginalPeriodStart/End` and `SourceVersion` to `AbsenceDeduction` without changing the release. A reopened run picks up its own period's differences. **The attendance gate belongs before `BeginCalculating`:** the service-regime gate refuses after the run moved, which is why a refused calculation stays «قيد الحساب»;
  - `IPayrollCalendar` (Payroll) and `HrLocalTime` / `ICalendarService` (shared) give «today» in Damascus;
  - **a calculation path** passes `PayrollInterimGate.RefusalForRun`. A run in a month an interim v2 version governs stops after 2027-01-31 (PLAN-54 §15).
- **The code today** (read on `01a8c3f9`; the plan's line numbers predate PLAN-51..56, so find code by its content if a line moved):
  - **The record** (`HR.Core.Domain/Entities/DailyAttendanceRecord.cs`): `EmployeeId`, `AttendanceDate`, `Status` (`DailyAttendanceStatus`: Present 0, Absent 1, OnLeave 2, Remote 3), `Notes`. Unique on (employee, date) (`HRCoreDbContext.cs` about `:424`). No source, no lock, no period.
  - **The controller** (`HR.Core.Web/Controllers/OrgBranchControllers.cs`, `DailyAttendanceRecordsController` at about `:77-301`, `[OrgScoped(typeof(DailyAttendanceRecord))]`). Every write is `HrCore.Write`:
    - `Create` (`:136-155`) adds a record as posted;
    - `Edit` (`:177-195`) sets `record.EmployeeId = vm.EmployeeId` (`:188`), so an edit can move a record to another employee;
    - `Delete` (`:200-210`) removes any record;
    - `BulkEntry` (`:224-262`) writes every day of the month, weekends and future days included, for the unit's **direct** `Active` employees only;
    - `ClosePeriod` (`:264-287`) calls the service;
    - `PopulateEmployeesAsync` (`:289-301`) lists the first `LookupMaxPageSize` employees by name. The views use that list; the shared employee picker (`data-hr-picker="employee"`, on `api/lookup/employees`, scoped) is used elsewhere.
  - **The service** (`HR.Core.Infrastructure/Integration/AttendanceIntegrationService.cs`):
    - `UpsertLeaveAttendanceAsync` (`:13-48`) overwrites any record of the leave's days with `OnLeave` and «إجازة معتمدة: <type>», losing the earlier status, and saves;
    - `RemoveLeaveAttendanceAsync` (`:50-73`) deletes **every** `OnLeave` record in the range, a manual one included, and saves;
    - `ClosePeriodAndPublishAsync` (`:75-124`) leaves out every `Terminated` employee (`:81`), takes the unit's direct employees (`:83`), counts statuses on every day (`:98-101`), and publishes `IAttendancePeriodClosed` per employee with `TransactionId = Guid.NewGuid()` (`:104-106`). It saves nothing of its own (`:122`).
  - **The leave consumers** (`HR.Core.Infrastructure/Consumers/LeaveAndAdvanceApprovedConsumer.cs`):
    - `LeaveApprovedConsumer` (`:11-55`) writes the status history (reason `LeaveApproved:<id> Source=…`, idempotent on that marker), **saves** (`:46`), then calls the upsert, which **saves again**;
    - `LeaveCancelledConsumer` (`:57-96`) does the same with the removal.
  - **The reconciler** (`HR.Core.Infrastructure/Services/LeaveScheduleReconciler.cs`) moves employees in and out of `OnLeave` from today's `OnLeave` records of any source. Its own reasons start with `LeaveScheduleReconciler:`.
  - **Payroll's consumer** (`Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs:329-359`, `AttendancePeriodClosedConsumer`): it returns on zero absence. Otherwise, if no row of that period has a reason containing `AttendancePeriodClosed`, it adds an `AbsenceDeduction` with `Amount = 0` and reason `AttendancePeriodClosed:<TransactionId>`. So it takes the first close only. It has no inbox (the leave consumers beside it use `ShouldSkipAsync` and `TryRecordAsync`).
  - **The absence row** (`Payroll.Domain/Entities/AbsenceDeduction.cs`, a `BaseEntity`): `EmployeeId`, `PeriodStart`, `PeriodEnd`, `AbsentDays`, `Amount`, `Reason`, `SourceReference` (unique where not null, `PayrollDbContext.cs` about `:303-309`), `AppliedInRunId`.
  - **The loader** (`PayrollPartitionInputLoader.cs:166-177`) takes absences that are free or applied in the same run and overlap the run's period.
  - **`AbsenceRule`** (`PayrollRules.cs`, order 30) computes an amount from the days when the stored amount is zero. **It skips a row whose amount is not positive (`if (amount <= 0) continue;`) without consuming it,** so such a row stays free for ever. It writes a `Deduction` component of `-amount`, adds to `TotalDeductions`, and consumes the row.
  - **The calculation's start** (`PayrollRunJobHandlers.cs`, `StartPayrollRunJobHandler` from about `:31`):
    - the legal control, then `ValidateCanCalculate` (about `:60`);
    - the resume of an existing job (`BeginCalculating` about `:88`);
    - the interim gate (about `:113`), then **`run.BeginCalculating()` (about `:143`)**;
    - `RestartCalculationCommand` comes back through it;
    - the service-regime gate runs later, in the calculator (`PayrollRunParallelCalculator.cs:49`).
  - **The audit record** (`PayrollAuditRecord`: `PayrollRunId`, `AuditorId`, `ClosedBy`, `Findings` (text), `Status` Open/Closed, `ClosedAt`). **The posting is allowed by any closed record of the run** (`PayrollRunHandlers.cs` about `:200`: `hasClosedAudit`). The acceptance fixture `scripts/acceptance-payroll-audit-fixture.ps1` inserts a closed one.
  - **The contract** `HRManagement.MessageContracts/Events/IAttendancePeriodClosed.cs`: one publisher (the service above), one consumer (the payroll one), no test. `EVENT_PUBLISHER_INVENTORY.md` has its row (SingleSave); `MESSAGE_EVENT_MATRIX.md` has none.
  - **The outbox test to rewrite:** `OutboxAtomicityIntegrationTests.CloseAttendancePeriod_SavesAfterPublishLoop` (about `:464`).
  - **Migrations with a data change under forced row security:** HR.Core's `WF044_WorkflowInstanceIntegrity` and Payroll's `PAY055`/`PAY056_NetFormula` lift `FORCE` around their fill.
  - **The acceptance seed, for agency A** (`AcceptancePayrollSeeder`, `DemoPayrollSeeder`, HR.Core's `DemoDataSeeder`):
    - **posted runs of every month from 2025-08 to 2026-07, and one run per status from 2026-08 to 2027-02;**
    - the `LAW_50_2004` v1 framework effective from 2000-01-01;
    - pay profiles that began 1 to 11 years before 2026-08-01;
    - **years of seeded attendance days.**
  - **The other classes' months** (agency A): AC-IAM-53a the current month; AC-PAY-54 2031 onwards; AC-PAY-55 May and June 2029; AC-PAY-56 January to June 2028.
  - **AC-IAM-52 writes attendance:** `FreeAttendanceDayAsync` picks a random free day 30 to 700 days back for a branch employee and creates a record there (P-12).
  - **Personas** (`testdata/acceptance/personas.json`):
    - agency A: `acc-br1-mgr` and `acc-br2-mgr` (`branch-manager`, `hr-core`, `admin-logistics`); `acc-central-a` and `acc-central-a2` (`hr-core`); `e2e-payroll-processor` and `e2e-payroll-approver`;
    - agency B: `acc-outsider-b` (`hr-core`).
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43). This plan's HR.Core migration sets the source of existing records (P-11): lift `FORCE` inside it, as `WF044` does, and prove it as the migrator.
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role (PLAN-54..56's migration tests are the pattern).
  - **A new PostgreSQL test uses the connection strings of `PostgreSqlFixture`,** whose idle connections close after seconds. Make sure CI's Testcontainers filter picks up your new classes.
  - **The prod-like stack runs `ASPNETCORE_ENVIRONMENT=Acceptance`,** so it does not load `appsettings.Production.json`. A value in a Production file must be mirrored in `docker-compose.acceptance-prodlike.yml`.
  - **Personas:**
    - a persona change needs `scripts/acceptance-reset.ps1` before the first start (the personas' hash is in the seed pack's fingerprint);
    - **never write `personas.json` while an image is building** (PLAN-53 built one image again for it);
    - a new persona also changes `DATA-AUTH`; a user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh`.
  - **The classes of an acceptance group run one at a time in an order that changes from run to run.** Before any payroll calculation on the prod-like stack, call `AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync`. A class never relies on what another class did or did not do, and leaves nothing that changes another class's work (P-12).
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - **Formatting:** before every commit, run `dotnet format whitespace HRManagement.slnx --verify-no-changes --no-restore --include <the files you changed>`. CI runs the full check.
  - **Acceptance tests:** a test posts the URL and the token the page renders (PLAN-44); an API write needs the antiforgery token (PLAN-47, PLAN-53).
  - **CodeQL runs in CI only:**
    - a new API `POST` or `PUT` without `[ValidateAntiForgeryToken]` fails `cs/web/missing-token-validation` (PLAN-53). **So does an existing one that starts to write something new** (PLAN-56's cancel-by-job): give it the token check, and update its clients;
    - a value whose name reads as sensitive (account, password, secret, token, IBAN) that flows into a log, an audit text or a stored field fails `cs/cleartext-storage-of-sensitive-information`, even when it is an opaque key (PLAN-55). Name an identifier by what it is;
    - a log line that writes a value built from the request is `cs/log-forging`;
    - never answer CodeQL with a baseline entry or an ignore attribute.
  - **Gitleaks reads every commit of the branch:** a literal that looks like a key fails the check even if a later commit removes it (PLAN-56 rewrote its history before the push). Never write such a literal, from the first commit; never rewrite history after a push.
  - **`-warnaserror` turns a use of an `[Obsolete]` member into an error** (P-8).
  - **The machine** (P-2): the disk (PLAN-53 filled it once); its memory under a stack start (PLAN-55); stale compiler servers; never delete build outputs while a step uses them; helpers outside the tree; never change `~/.ssh/config` in the middle of a run; after the host sleeps, the Docker VM's clock may lag (PLAN-50).
  - **The session may stand still** (PLAN-51: 7 h 44 min after the pre-push check; PLAN-55: 9 h 38 min; PLAN-56: twice). Keep the progress file's next step exact, and go from the pre-push check straight to the push and the PR, in the same turn.
  - **The server:**
    - **its disk:** check the free space before the build, and watch it during the build (P-16);
    - **its ssh port:** keep connections few. Watch a server build from one process on the server, read its log rarely, and never poll in a tight ssh loop (PLAN-56's 8-minute refusal);
    - run the server build detached (PLAN-44, PLAN-53), and the server suites with nothing heavy beside them (PLAN-45);
    - **a deploy may stop at `keycloak-reconcile` after PostgreSQL is recreated** (PLAN-40, PLAN-54, PLAN-56), leaving the application containers created and not started: run the same deploy again at once;
    - if the server build fails because the provider's resolvers answer SERVFAIL, wait and retry; never change the server's resolver configuration (PLAN-50);
    - the server suites write into the committed `acceptance-evidence/` and `reports/`: restore those files, or move them under `artifacts/plan57/vps/` (PLAN-48);
    - a server check cleans up what it leaves open, and decodes a page before it compares Arabic text (PLAN-49);
    - **Production and the demo keep the password grant closed:** a script that signs in there uses a signed-in session's cookie or a bearer token (PLAN-55);
    - older server checks that fail or flicker from this machine only (PLAN-55 §11, PLAN-56 §11). Record them, rerun them alone, and do not chase them:
      - `AC_POL_47_vps_the_migration…`;
      - the workflow sweeps check, which passes alone just after a sweep cycle;
      - `AC_OPS_39_vps_e_Keycloak…`, on an empty `kcadm` answer;
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
   - `docs/acceptance-plans/PLAN-57-lva-attendance-period-payroll-handoff.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
   - `docs/HCM_002_LEAVE_ATTENDANCE_GAP_STUDY.md` (G-LVA-03, G-LVA-10) and `docs/PAY_001_GAP_STUDY.md` (G-PAY-19).
2. **These reports:**
   - `reports/plan56-final-report.md` (§3, §5, §11, §13, §15) and its merge note;
   - `reports/plan52-final-report.md` (the branch scope of HR.Core) and `reports/plan51-final-report.md` (the explicit policies);
   - `reports/plan56-progress.md`, for the server's disk, the ssh refusal and the two stops.
3. **These documents:**
   - `docs/HCM_002_ATTENDANCE.md`, `docs/RELEASE_PRODUCT_SCOPE.md`, `docs/PILOT_DEPLOYMENT.md`, `docs/EVENT_PUBLISHER_INVENTORY.md`, `docs/MESSAGE_EVENT_MATRIX.md`, `docs/PAYROLL_RULES.md`, `docs/syr/holiday-calendar-policy.json`;
   - the rows D-LVA-3, D-LVA-4 and D-LVA-9..14 of `docs/OWNER_DECISIONS_REGISTER.md`;
   - PLAN-59's plan, for what it will build on this contract.
4. The code named in «The code today».
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **No other plan session is running,** and you are in the main folder (see «How to run»), on `main` or on `docs/plan-56-merge-note`.
2. **`git fetch origin`**, and `origin/main` holds PLAN-56: the merge of `release/plan-56` is in `git log origin/main --merges --oneline -5`. PLAN-37 and PLAN-52 are merged too (the plan's 0.2).
3. **The working tree is clean,** except for these, which may be present:
   - the owner's local edits to `global.json` and `tmp-vendors.html`, if any;
   - git-ignored files (`artifacts/`, `.env`, the local key rings of the dev scripts);
   - untracked prompt files under `docs/acceptance-plans/execution/`, this one included.

   Anything else uncommitted: leave it untouched, record it, and go on if it does not touch this plan's files. `BLOCKED:` only if it does. Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
4. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
   - **If port 22 is blocked** from the owner's network, this precondition does not stop P0. Record it, take the server's read-only picture at the start of P9, and go on with the local phases.
5. **`gh auth status`** shows an account with push rights to `omardefdev/HRManagement` and the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

The owner accepted D-LVA-3 and D-LVA-4 on 2026-10-31 (the wave 1 survey). D-LVA-9..14 are the plan's own defaults, with no other answer on record. They apply as written, since the owner's standing instruction is that plans run to the end with their defaults. If the owner changed one in a message before P0 starts, his message wins; record it.

- **D-LVA-3, attendance in v1:** yes, at a minimum:
  - a daily status from the branch, a locked monthly close, and corrections as differences;
  - device import is outside v1;
  - the payroll gate is `Payroll:Attendance:Gate=Warn`, so an agency that does not use attendance is not stopped.
- **D-LVA-4, who enters and who approves:**
  - entering attendance and asking for a correction: `hr-core` within its scope, including a branch manager for his unit's tree;
  - approving a correction: `hr-core` **not branch-scoped** (central), who is not the requester, checked on the server.
- **D-LVA-9, a working day with no record at the close:** `Attendance:Close:UnrecordedWorkingDays=RequireComplete`. The close is refused with the list of what is missing. `CountAsAbsent` and `Ignore` stay as settings.
- **D-LVA-10, the gate's severity:** `Warn` in every environment. The owner may raise it to `Block`.
- **D-LVA-11, the period:** a calendar month (`Attendance:Periods:Mode=CalendarMonth`). Custom periods that do not overlap stay behind `Custom`.
- **D-LVA-12, an approved or cancelled leave that touches a locked day:** `Attendance:Corrections:LeaveEventsAfterClose=AutoApply`. It becomes a system correction, approved at once, because the leave decision is the authority. `Pending` stays as a setting.
- **D-LVA-13, the calendar until the central calendar exists (LVA-G, wave 2):**
  - `Attendance:Calendar:WeeklyRestDays=Friday,Saturday`;
  - `Attendance:Calendar:Holidays` is a list of dates in configuration, **empty by default**. No list of holidays lives in the code (`forbidHardcodedHolidayList`);
  - the close page warns when no holiday is configured for the period.
- **D-LVA-14, the wage of a late difference day:** the wage of the run that applies the difference (today's `ComputeAbsenceAmount`). The historical wage is PAY-H's (wave 2).

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-57-lva-attendance-period-payroll-handoff` from `origin/main` (plan 0.1), in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-56's merge note from `docs/plan-56-merge-note`. If that branch is gone, rebuild the record from GitHub (the PR, its runs, the merge commit), and say so;<br>&nbsp;&nbsp;– commit this prompt file. If the owner pasted it into the session rather than saving it, write it from the session's text first, as PLAN-54..56 did;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **The machine (Linux): awake, the disk, the memory, Docker, the ports.**<br>• **Keep the machine awake for the session, changing no setting:** `systemd-inhibit --what=sleep:idle --who=plan57 --why="PLAN-57" sleep infinity`, detached. Record its PID in `artifacts/plan57/keepawake.pid`, and stop it **by that PID** at the end. If the machine restarts, start it again and record it.<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• **Remove only this repository's leftovers:** compose projects `hrm-*`, Testcontainers, this plan's images and this repository's build cache. Never prune, stop or remove another project's images, containers or volumes, or the owner's dev containers.<br>• **The disk:** read `df -h /` and `docker system df` before every image build and every stack start.<br>&nbsp;&nbsp;– Build an image only with at least 12 GB free. Below that, prune this repository's build cache and remove this plan's unused images first;<br>&nbsp;&nbsp;– a watch that stops a build near 2 GB free stops it **by its PID**;<br>&nbsp;&nbsp;– if 12 GB cannot be reached without touching other projects: `BLOCKED:`, one line — the owner frees space.<br>• **The memory, before a stack start:**<br>&nbsp;&nbsp;– nothing heavy beside it: no build, no test host, no browser, no idle stack of this repository;<br>&nbsp;&nbsp;– read `uptime` and `free -m` first, and start only when the load is below the number of CPUs;<br>&nbsp;&nbsp;– a start that fails from the load (a health check timing out, a 401 during the identity sync) is retried after the load falls, three times at most, each failure and its cause recorded;<br>&nbsp;&nbsp;– wait for each start in the turn, in short polls («Long commands»).<br>• **The images:** one service at a time from the local base images (`acceptance-up.ps1 -UseLocalBaseImages`, and the helpers PLAN-53..56 kept outside the tree); the migrator through `docker compose build`. This plan changes both HR.Core and Payroll: rebuild both, and the migrator. Never commit a helper.<br>• **One heavy job at a time:** never an image build beside a host test run or a browser check. Close what you opened before a full unit run (a browser, an idle stack).<br>• **After an image build,** check that every service image was rebuilt from this tree. A failed build leaves stale images, and a `-NoBuild` run on them proves nothing.<br>• **Before each stack start,** check the stack's host ports with `ss -ltn` and `docker ps`. A port held by something that is not this repository's: `BLOCKED:` with the port.<br>• **The local CI steps:** `scripts/ci/run-steps.sh --all` natively in a fresh clone of HEAD, with the compiler servers off (see «Where things stand»), naming each step not run and why.<br>• **If this session runs on Windows again,** use PLAN-67's forms of these checks, and record it. |
| P-3 | **The baseline (plan 0.2–0.6),** into `reports/plan57-baseline.md`.<br>• **0.2:** one line per dependency with its merge SHA: PLAN-56 (the guards and the releasing cancellation), PLAN-37 (the prod-like stack with HR.Core and Payroll in production wiring), PLAN-52 (the scope of `DailyAttendanceRecords.Create`).<br>• **0.3–0.4:** `dotnet build HRManagement.slnx -warnaserror`; `dotnet ef migrations has-pending-model-changes` for HR.Core and Payroll, none.<br>• **0.5:** the unit tests filtered on `Attendance`, `Payroll` and `Architecture`, then the integration tests on `Attendance`, `OutboxAtomicity` and `Payroll`. Green, or a list.<br>• **0.6, the data inventory,** read-only, by `scripts/lva057-validate.ps1 -Inventory` (written first, in P0) on a fresh local prod-like stack and on the demo:<br>&nbsp;&nbsp;– the records on Fridays and Saturdays by status;<br>&nbsp;&nbsp;– the manual `OnLeave` records;<br>&nbsp;&nbsp;– the `AbsenceDeductions` whose reason starts with `AttendancePeriodClosed:`.<br>• **The code inventories,** generated by search, never typed, into `artifacts/plan57/inventory.*`, and summarised:<br>&nbsp;&nbsp;– every writer of `DailyAttendanceRecords` (code, seeders, tests, scripts), and every reader of its statuses;<br>&nbsp;&nbsp;– every producer and reader of `AbsenceDeduction`;<br>&nbsp;&nbsp;– every path that starts a payroll calculation (where the gate goes);<br>&nbsp;&nbsp;– every test that counts or reads `PayrollAuditRecords` (the gate adds one; P-9);<br>&nbsp;&nbsp;– every acceptance class that writes attendance or relies on absences, and the months each class uses.<br>• **From this prompt:**<br>&nbsp;&nbsp;– the demo's picture (P-15);<br>&nbsp;&nbsp;– the state of the old event's queue on the demo (P-8);<br>&nbsp;&nbsp;– on the fresh local stack, a payroll run of agency A **calculated in the first month P-12 reserves**, then cancelled through PLAN-56's API, to prove those months can carry the plan's payroll scenarios. |
| P-4 | **Policies, stances and the API's surface.**<br>• **The three policies** (§6), in `HrManagementPolicies` and `HrManagementAuthorizationExtensions`:<br>&nbsp;&nbsp;– `HrCore.AttendanceWrite`: `hr-admin` or `hr-core`, through `RolesPolicy`. The scope is checked in the writer;<br>&nbsp;&nbsp;– `HrCore.AttendanceClose`: the same roles, through `RolesPolicy`. A branch closes its own tree only (§1, rule 3);<br>&nbsp;&nbsp;– `HrCore.AttendanceCorrectionApprove`: those roles **and** not `HrOrgScopeVisibility.IsBranchScoped(user)` (D-LVA-4). It stays a policy of its own because it leaves the branch out.<br>&nbsp;&nbsp;They replace `HrCore.Write` on the five `DailyAttendanceRecords` writes. Every new write names one of them (`WriteActionsHaveExplicitPolicyTests`, whose HR.Core list stays empty).<br>• **The stances:**<br>&nbsp;&nbsp;– `AttendanceCorrectionsController`: `[OrgScoped(typeof(AttendanceCorrection))]`, keyed by its employee in `HrCoreOrgScopeKeys`;<br>&nbsp;&nbsp;– `AttendancePeriodsController`: `[OrgScopeNotApplicable("…")]` with its reason, since the service checks the unit;<br>&nbsp;&nbsp;– `DailyAttendanceRecordsController` keeps its stance; the Payroll reads are covered by their controller's row.<br>• **The API** (HR.Core, behind the gateway's `api/hr-core/`):<br>&nbsp;&nbsp;– `POST api/attendance/periods/{yyyy-MM}/close` (body `orgUnitId`, null for the whole agency) and `GET api/attendance/periods/{yyyy-MM}`;<br>&nbsp;&nbsp;– `POST api/attendance/corrections`, `POST api/attendance/corrections/{id}/approve`, `POST api/attendance/corrections/{id}/reject`;<br>&nbsp;&nbsp;– a `GET …/antiforgery` on each new API controller.<br>&nbsp;&nbsp;Payroll: `GET api/payroll/runs/{id}/attendance-status` and `GET api/payroll/attendance/receipts?periodStart=yyyy-MM-dd` (`Module.Payroll`).<br>• **Every new `POST` carries `[ValidateAntiForgeryToken]`** from its first commit, and so does any existing API write whose behaviour this plan changes (Lessons).<br>• **The HTTP answers:**<br>&nbsp;&nbsp;– a close done → 200 `{ periodId, status, closedEmployees, skippedAlreadyClosed }`;<br>&nbsp;&nbsp;– a close with unrecorded working days under `RequireComplete` → 409 `{ incomplete: [{ employeeId, unrecordedWorkingDays }], total }`, nothing written;<br>&nbsp;&nbsp;– a concurrent close that lost → 409 with the plan's message, nothing partial;<br>&nbsp;&nbsp;– a unit of the caller's agency outside his scope → 403, as the attendance actions answer today;<br>&nbsp;&nbsp;– a unit, record, correction or run of another agency → 404;<br>&nbsp;&nbsp;– a broken correction rule (a reason under 15 characters, the same status, `OnLeave` by hand, a day not locked) → 400;<br>&nbsp;&nbsp;– the requester approving his own correction → 400, as the run's acts answer a separation-of-duties refusal (PLAN-56); a branch-scoped approver → 403 by the policy;<br>&nbsp;&nbsp;– a second pending correction for the same employee and day, or a decided one decided again → 409.<br>&nbsp;&nbsp;Every refusal is an Arabic message, and changes nothing. |
| P-5 | **The period, the summary and the lock (plan §1).**<br>• **The entities:** `AttendancePeriod` (unique on agency and start), `AttendancePeriodClosure`, `AttendanceEmployeeSummary` (one row per period and employee, unique, with `xmin`), and the enums `AttendancePeriodStatus`, `AttendanceRecordSource` (Manual 0, Bulk 1, Leave 2, Correction 3) and `AttendanceCorrectionStatus`.<br>• **`AttendanceSummaryCalculator`:** a pure function of the employee's records, the period, the calendar, the hire date and the end of service. It counts statuses on **working days** only. `UnrecordedWorkingDays` = working days with no record.<br>• **`AttendanceCalendar`** from `AttendanceCalendarOptions` (D-LVA-13): `IsWorkingDay(date)`. «Today» comes from the shared Damascus calendar (`HrLocalTime` / `ICalendarService`), never `DateTime.UtcNow`.<br>• **The employees of a close** (§1, rule 2):<br>&nbsp;&nbsp;– those whose primary unit is in the chosen unit's tree, through `MaterializedPath` as `OrgScopeResolver` does, or the whole agency;<br>&nbsp;&nbsp;– `HireDate <= PeriodEnd`;<br>&nbsp;&nbsp;– not `Terminated`, or terminated within the period. The end of service is the last `EmploymentStatusHistories.ChangedAt` to `Terminated`.<br>• **The rules,** as the plan writes them:<br>&nbsp;&nbsp;– `RequireComplete` refuses the whole close when anyone in scope has a missing working day;<br>&nbsp;&nbsp;– a close is idempotent (`SkippedAlreadyClosed`);<br>&nbsp;&nbsp;– two concurrent closes meet on the summary's unique index, and the loser returns the plan's message with no partial effect;<br>&nbsp;&nbsp;– **the summaries, the closure row and the events are written in one save,** the events staged through the outbox before it (PLAN-40);<br>&nbsp;&nbsp;– the period's status is `Closed` when no eligible employee of the agency lacks a summary, otherwise `PartiallyClosed`. It is for display; the lock is per employee;<br>&nbsp;&nbsp;– **the lock:** day (E, D) is locked when E has a summary in a period that covers D. Nothing writes a locked day but `ApplyApprovedCorrectionAsync`;<br>&nbsp;&nbsp;– there is no reopening; a change after the close is always a correction.<br>• **`AttendanceLockService`** answers one day and a batch (`GetLockedDaysAsync`), each in one query.<br>• **The screens:** `AttendancePeriods/Index` (the periods and their state) and `AttendancePeriods/Close` (the month and the unit). The close page shows what is missing (the first 50 and the total) and the warning when no holiday is configured.<br>• **The old path is removed:** `ClosePeriodAndPublishAsync`, `DailyAttendanceRecordsController.ClosePeriod` (GET and POST) and its view. The old link leads to `AttendancePeriods/Close`.<br>• **The plan's performance target:** a close of 2,000 employees × 31 days under 10 seconds, proved in the integration test. |
| P-6 | **One writer for an attendance day (plan §2).**<br>• **`DailyAttendanceRecord` gains** `Source` (default `Manual`), `SourceReference` (100) and `ReplacedStatus`.<br>• **`AttendanceDayWriter`** (`HR.Core.Infrastructure/Services/Attendance/`) is the only entry point: `CreateManualAsync`, `UpdateManualAsync`, `DeleteManualAsync`, `BulkFillAsync`, `ApplyLeaveAsync`, `RevertLeaveAsync`, `ApplyApprovedCorrectionAsync`. **It never saves;** its caller saves once. The scope check of the employee that PLAN-52 added for a create (find where it lives) moves into the writer, as one check for a create, the bulk entry and a correction. It is never removed.<br>• **The plan's rules:**<br>&nbsp;&nbsp;– a manual or bulk write on a locked day is refused with «اليوم ضمن فترة حضور مغلقة؛ قدّم طلب تصحيح», and nothing changes;<br>&nbsp;&nbsp;– the employee and the date never change after creation;<br>&nbsp;&nbsp;– `OnLeave` is never written by hand or in bulk; it comes from `ApplyLeaveAsync` only, with `LeaveRequestId=<id>`. A `Source=Leave` record is never edited or deleted by hand;<br>&nbsp;&nbsp;– **the bulk entry:** working days only, nothing after today in Damascus, the unit's tree by §1's rule, locked and existing days skipped, and a report of what was written, skipped as locked, and skipped as existing;<br>&nbsp;&nbsp;– a duplicate day → an Arabic validation message, never a 500: checked first, and the unique index's exception caught as a fallback;<br>&nbsp;&nbsp;– `ApplyLeaveAsync` keeps the earlier status in `ReplacedStatus`, or makes a system correction on a locked day (P-7);<br>&nbsp;&nbsp;– `RevertLeaveAsync` touches only the records of that leave's `SourceReference`. It restores `ReplacedStatus` or deletes a record that had none, and goes through a system correction on a locked day. Today's removal deletes every `OnLeave` record in the range, a manual one included: that ends.<br>• **The controller and views:**<br>&nbsp;&nbsp;– `DailyAttendanceRecordsController` calls the writer, and `PopulateEmployeesAsync` with its short list goes;<br>&nbsp;&nbsp;– `Create` and `BulkEntry` use the employee picker (`data-hr-picker="employee"` on `api/lookup/employees`);<br>&nbsp;&nbsp;– **`DailyAttendanceRecordEditViewModel` binds no employee and no date:** `Edit` changes the status and the note, and shows the employee and the date read-only;<br>&nbsp;&nbsp;– `Details` is new (G-LVA-17);<br>&nbsp;&nbsp;– on a locked day the list offers «طلب تصحيح».<br>• **The leave consumers** (HR.Core): the status history, the attendance and any system correction in **one save**.<br>• **`LeaveScheduleReconciler`** reads `Source=Leave` records only. It returns to `Active` only an employee whose last move to `OnLeave` had a reason starting with `LeaveApproved:` or `LeaveScheduleReconciler:` (both written by the code today).<br>• **`AttendanceSingleWriterArchitectureTests`:**<br>&nbsp;&nbsp;– no `DailyAttendanceRecords.Add`, `AddRange`, `Remove`, `RemoveRange` or `Update`, and no `.Status =` on an attendance record, in `src/Services/HRCore/**` outside the writer;<br>&nbsp;&nbsp;– its explicit exception list holds `DemoDataSeeder` only, and only shrinks;<br>&nbsp;&nbsp;– a planted writer is its own negative test. |
| P-7 | **The correction after the close (plan §3).**<br>• **`AttendanceCorrection`**, with a filtered unique index allowing one `Pending` request per employee and day. **`AttendanceCorrectionService`** has `RequestAsync`, `ApproveAsync`, `RejectAsync` and `ApplySystemCorrectionAsync`. **`AttendanceCorrectionsController`** (MVC and API) has its views.<br>• **The rules,** as the plan writes them:<br>&nbsp;&nbsp;– a correction is for a locked day only;<br>&nbsp;&nbsp;– the reason is mandatory and at least 15 characters; the new status differs from the old; a manual correction cannot set `OnLeave`;<br>&nbsp;&nbsp;– the requester holds `HrCore.AttendanceWrite` with the employee in his scope;<br>&nbsp;&nbsp;– the approver holds `HrCore.AttendanceCorrectionApprove` and is not the requester: the server compares the caller's `sub` with `RequestedBy`. A rejection needs a note.<br>• **An approval is atomic,** one save, retried on an `xmin` conflict on the summary:<br>&nbsp;&nbsp;1. the writer applies the day (`Source=Correction`, `SourceReference=AttendanceCorrection:<id>`);<br>&nbsp;&nbsp;2. the summary is computed again by the same calculator, with `Version` + 1 and `LastReason = Correction`;<br>&nbsp;&nbsp;3. `IAttendanceSummaryPublished` is staged with the new version;<br>&nbsp;&nbsp;4. the correction is written `Approved` with its `ResultingVersion`.<br>• A correction that changes no figure (`Present` → `Remote`) still raises the version and publishes it; payroll records it with no row (P-9).<br>• **A system correction from a leave event** (D-LVA-12, `AutoApply`):<br>&nbsp;&nbsp;– it is made and approved in the consumer's own transaction, with no requester and no decider, `Source = LeaveEvent`, and the reason «إجازة معتمدة/ملغاة `<LeaveRequestId>`»;<br>&nbsp;&nbsp;– under `Pending`, it waits for a person;<br>&nbsp;&nbsp;– the same event twice makes one correction.<br>• **Every request, approval and rejection** reaches `EntityAuditLog` through the ordinary save: no `ExecuteUpdate` in this path. |
| P-8 | **The handover contract (plan §4).**<br>• **`IAttendanceSummaryPublished`** with the plan's fields:<br>&nbsp;&nbsp;– `SummaryId`, `Version`, the period, the employee;<br>&nbsp;&nbsp;– the six counts, **absolute** for that version;<br>&nbsp;&nbsp;– `Reason` (`Close`, `Correction`, `LeaveEvent`) and `CorrectionId`;<br>&nbsp;&nbsp;– `TenantId` set explicitly, with the tenant header like HR.Core's other events (`EmployeeIntegrationService` sets it), plus `OccurredAt`, `CorrelationId`, `SourceSystem` and `SchemaVersion` 1.<br>• **Payroll's semantics** (P-9) are the plan's eight rules, and PLAN-59 relies on them: the latest version wins, no version is recorded twice, an older version is ignored, the freeze, the negative difference, the receipt, the gate, and the old event's end.<br>• **The old event ends:**<br>&nbsp;&nbsp;– `IAttendancePeriodClosed` is marked `[Obsolete]`, and nothing publishes it;<br>&nbsp;&nbsp;– `AttendancePeriodClosedConsumer` is deleted, **unless P0 finds messages waiting in its queue on the demo.** Then, as the plan's risk says, it stays one release in a «log and ignore» form. Its one reference to the obsolete contract carries a local pragma with the reason, and `scripts/lva057-validate.ps1` allows exactly that one;<br>&nbsp;&nbsp;– either way, right before the deploy (P9) its queue must be empty: the running release consumes it within seconds. Messages in its error queue are recorded, never replayed into the new model.<br>• **`EVENT_PUBLISHER_INVENTORY.md`** (the new event, SingleSave; the old row ended), **`MESSAGE_EVENT_MATRIX.md`**, and a runbook `docs/runbooks/attendance-period-close.md` (the close, the correction, setting holidays, and draining the old queue before a deploy). |
| P-9 | **Payroll's side: the receipt, the difference and the gate (plan §5).**<br>• **`AttendanceSummaryReceipt`:**<br>&nbsp;&nbsp;– `SummaryId` (unique), the period, the employee;<br>&nbsp;&nbsp;– `LatestVersion`, `LatestAbsentDays`, `RecordedAbsentDays`, `ReceivedAt`, `UpdatedAt`;<br>&nbsp;&nbsp;– `xmin`, and `TenantId` from the message.<br>• **`AbsenceDeduction` gains** `OriginalPeriodStart/End`, `CarryForward` and `SourceVersion`. PLAN-56's release stays as it is: it touches the applied markers only, so a released carried row is free again.<br>• **`AttendanceSummaryPublishedConsumer`** (replacing the old one), in one transaction:<br>&nbsp;&nbsp;– the inbox (`ShouldSkipAsync` / `TryRecordAsync`, as the leave consumers beside it);<br>&nbsp;&nbsp;– the receipt locked `FOR UPDATE`;<br>&nbsp;&nbsp;– the freeze decision;<br>&nbsp;&nbsp;– the difference row, with `SourceReference = "ATT:{SummaryId}:v{Version}"` (unique) and difference = `AbsentDays(v)` − what was recorded for that summary;<br>&nbsp;&nbsp;– the receipt updated.<br>&nbsp;&nbsp;A version older than the latest recorded is ignored. A zero difference writes no row and updates the receipt only. The tenant on the receipt and the row is the message's.<br>• **The row:** `PeriodStart/End = OriginalPeriodStart/End` = the original period, `SourceVersion = v`, and `CarryForward = HasCalculatedRunAsync(period)` at the moment of consumption.<br>• **`PayrollAttendanceFreeze`** (`IsFrozenAsync`, `HasCalculatedRunAsync`) is the one place that reads PLAN-56's guards: a `Regular` run that is neither cancelled nor reversed, whose results are frozen or that is `UnderReview`, `Approved` or `Posted`. An integration test covers every run state.<br>• **The loader:** for a `Regular` run it also takes a carried row of an earlier period that is free or applied in that run (the plan's expression). A carried row is taken by its own period's recalculation if that comes first, otherwise by the first later regular run. A row applied in a run is never changed.<br>• **`AbsenceRule`:**<br>&nbsp;&nbsp;– negative days → a refund: `ComputeAbsenceAmount(context, -days)` as a `Deduction` component of a **positive** amount that lowers `TotalDeductions`, named «استرداد خصم غياب — فترة yyyy-MM»;<br>&nbsp;&nbsp;– a carried row is named «فرق غياب عن فترة yyyy-MM»;<br>&nbsp;&nbsp;– **every row it reads is consumed, a refund included.** Today a row with an amount that is not positive is skipped without being consumed;<br>&nbsp;&nbsp;– PLAN-56's validation holds: `PayrollNet` accepts negative deductions, and the components still add up to the unclipped net.<br>• **What does not change:** rows whose reason starts with `AttendancePeriodClosed:` keep their behaviour and are not migrated; the unpaid-leave rows (`LeaveRequestId=`) are PAY-H's and LVA-D's.<br>• **`PayrollAttendanceGate`** (`Payroll:Attendance:Gate`: `Warn`, `Block`, `Off`):<br>&nbsp;&nbsp;– it runs in `StartPayrollRunJobHandler` after `ValidateCanCalculate` and the interim gate, **before `run.BeginCalculating()`**, so a refusal leaves the run as it was;<br>&nbsp;&nbsp;– it applies to `Regular` runs only, never `Correction` or `FinalSettlement`;<br>&nbsp;&nbsp;– the resume of an existing job does not run it again; a restart does;<br>&nbsp;&nbsp;– «not closed» = an employee in the run's scope with no receipt that overlaps the run's period;<br>&nbsp;&nbsp;– **`Warn`:** the calculation goes on, and a `PayrollAuditRecord` is written with the count and the list in `Findings`. **That record is `Open`, never `Closed`: a closed record is what lets a run be posted** (`hasClosedAudit`);<br>&nbsp;&nbsp;– `Block`: the start is refused with the count, and nothing changes;<br>&nbsp;&nbsp;– `Off`: no check.<br>• **The reads and the panel:** `attendance-status` gives `{ gateMode, employeesInScope, missingAttendance, carryForwardApplied, carryForwardPending }`; `attendance/receipts` gives the receipts with their versions. The run's page (`Details.cshtml`) shows a «الحضور» panel with the mode, the missing employees by name, and the carried differences applied and pending.<br>• **Every regular run now meets the gate,** in every class, and under `Warn` it adds an open audit record. The tests that count audit records keep their meaning, counting closed reviews or the warning by what it is. The posting is unaffected (a closed record still allows it). |
| P-10 | **The configuration.**<br>• **HR.Core** (`HR.Core.Web/appsettings.json`, `.Production.json`, `.Staging.json`): `Attendance:Periods:Mode=CalendarMonth`, `Attendance:Close:UnrecordedWorkingDays=RequireComplete`, `Attendance:Corrections:LeaveEventsAfterClose=AutoApply`, `Attendance:Calendar:WeeklyRestDays=Friday,Saturday`, and `Attendance:Calendar:Holidays` empty.<br>• **Payroll** (`Payroll.Web/appsettings*.json`): `Payroll:Attendance:Gate=Warn`.<br>• **The prod-like stack loads neither Production file:** mirror both groups in `docker-compose.acceptance-prodlike.yml`.<br>• **The holidays of the reserved months** (P-12) go in `docker-compose.acceptance.yml` only, the acceptance layer, as the plan says. Never in a Production or Staging file, and never in code. |
| P-11 | **The migrations: `LVA057_AttendancePeriods` (HR.Core) and `LVA057_AttendanceHandoff` (Payroll).**<br>• **`LVA057_AttendancePeriods`:**<br>&nbsp;&nbsp;– the four tables with `TenantId`, row security through `TenantRlsMigrationHelper`, and the grants;<br>&nbsp;&nbsp;– their unique and filtered indexes (P-5, P-7);<br>&nbsp;&nbsp;– `Source` (default `Manual`), `SourceReference` and `ReplacedStatus` on `DailyAttendanceRecords`;<br>&nbsp;&nbsp;– **the fill:** `Source = Leave` for `Status = OnLeave AND Notes LIKE 'إجازة معتمدة:%'`; every other record stays `Manual`; nothing is deleted. That update reads and writes a tenant table under forced row security: **lift `FORCE` inside the migration, as `WF044` does,** or it changes nothing and says nothing.<br>• **`LVA057_AttendanceHandoff`:** the receipts table (row security, grants), the four columns on `AbsenceDeductions`, and the index on (employee, `CarryForward`, `AppliedInRunId`). No data move.<br>• No byte order mark. `dotnet ef migrations has-pending-model-changes` reports none for either service (`-p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web`, and Payroll's pair).<br>• **`Lva057MigrationPostgreSqlTests`:** as HR.Core's migrator, a non-superuser under forced row security, leave-made `OnLeave` records in two agencies get `Source = Leave`, each in its own agency, and a manual `OnLeave` stays `Manual`. |
| P-12 | **Acceptance placement (PLAN-41's rules).**<br>• **`AcLva57AttendancePeriodTests`** (AC-LVA-57a..h): `prodLike[]`, `required: true`, group `main`; `[RequiredAcceptance]`, `EvidenceRecorder("AC-LVA-57")` and `AcceptanceGate.RequireData`. It does not stop or recreate containers.<br>• **Its steps run in order,** as `AcPay56`'s do.<br>• **Its months** (`testdata/acceptance/lva057-periods.json`). **The plan's default, 2026-01..04, cannot be used:** the acceptance seed posts agency A's runs for every month from 2025-08 to 2026-07, and holds one per status from 2026-08 to 2027-02. The months must be:<br>&nbsp;&nbsp;– **in the past,** since the bulk entry writes no future day;<br>&nbsp;&nbsp;– free of any run of agency A, and of every other class (2028-01..06, 2029-05..06, 2031 on, and the current month are taken);<br>&nbsp;&nbsp;– governed by the seed's v1, which holds from 2000;<br>&nbsp;&nbsp;– so: months of 2025 before August.<br>&nbsp;&nbsp;P0 proves that a run calculates in them. If none does, stop with `BLOCKED:` and the cause; never move a payroll assertion into a month the seed or another class holds.<br>• **Its people:** `acc-br1-mgr`, `acc-br2-mgr`, `acc-central-a`, `acc-central-a2`, `e2e-payroll-processor`, `e2e-payroll-approver`, and `acc-outsider-b`. Their scenarios gain AC-LVA-57 (Lessons: reset).<br>• **Each scenario starts with a recorded, test-only reset of its own month** (the plan's rule; the SQL in `steps.md`):<br>&nbsp;&nbsp;– in HR.Core: the scenario's employees' attendance, summaries, closures and corrections of that month;<br>&nbsp;&nbsp;– in Payroll: the receipts and absence rows of that month;<br>&nbsp;&nbsp;– **its own runs are cancelled through PLAN-56's API, never deleted.**<br>&nbsp;&nbsp;A seeded month has years of random statuses, so the reset is what makes its numbers exact.<br>• **Other test-only preconditions,** recorded: an employee whose service ended within the month, made in HR.Core by SQL; leave events injected by `AcceptanceMessagingHelper` (`PublishLeaveApprovedAsync`, and `PublishAttendanceSummaryAsync` for 57d's duplicate and older version) until LVA-A exists.<br>• **The scenarios** are the plan's table, with the answers of P-4:<br>&nbsp;&nbsp;– **57a:** the close of `br1`'s tree covers a sub-unit's employee and one who left within the month; receipts appear, and E's row has two days. `br2-mgr` closing `br1`'s unit → 403;<br>&nbsp;&nbsp;– **57b:** the bulk entry skips rest days, a configured holiday and future days. Its «no future day» is proved in the current month for one small unit of `br1`, and that month's records are reset afterwards;<br>&nbsp;&nbsp;– **57c, 57d:** need runs in their months, made by the payroll people;<br>&nbsp;&nbsp;– **57d:** M is frozen by validating and submitting its run (PLAN-56's guards). The carried row is consumed by **this class's own** M+1 run, in the same scenario;<br>&nbsp;&nbsp;– **57e:** the fully closed month is closed for the **whole agency** by `acc-central-a`, so the run has no one missing. The `Block` case is proved in the integration test;<br>&nbsp;&nbsp;– **57h:** `acc-outsider-b` naming a unit of agency A → 404, with no effect.<br>• **Isolation** (the classes run one at a time, in an order that changes):<br>&nbsp;&nbsp;– **AC-IAM-52 must not land on a closed day:** its `FreeAttendanceDayAsync` picks a free day 30 to 700 days back, and once this class has closed a month, a create there is refused. Change it to pick a day that no attendance summary of that employee covers. Its meaning, a free day that can be written, is kept;<br>&nbsp;&nbsp;– **this class leaves no free carried absence row in agency A,** since the loader gives one to the first later regular run of any class. Every carried row it makes is consumed by its own M+1 run. Its other rows are dated in its own months of 2025, which only its own runs take;<br>&nbsp;&nbsp;– its runs live in its reserved months, and `ClassifyRecordsAddedSinceTheSeedAsync` comes before each calculation;<br>&nbsp;&nbsp;– it asserts on its own employees and rows by id, never on a total another class could move.<br>• **The evidence of record is the PR run's artifact,** not `acceptance-evidence/AC-LVA-57/` (D-QA-20). Copy what the report needs into `artifacts/plan57/`, then restore the committed evidence. |
| P-13 | **Local testing (owner's rule of 2026-11-06).** Run only the classes this plan touches, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`, and the overlay groups with `-Stage prodLike:<group>`. **The full suite runs once, in CI.**<br>• **prod-like:** `AcLva57AttendancePeriodTests`, `AcIam52BranchScopeTests` (its attendance day), `AcIam50ApproverReachDelegationTests`, `AcIam51ExplicitPolicyTests`, `AcIam53SalaryChangeTests`, `AcPay54LegalFrameworkTests`, `AcPay55BankAccountTests`, `AcPay56RunStateMachineTests` (the gate beside their runs), `AcUx67ReadableInterfaceTests`, `AcHcm25ProductionApprovalTests`, the prod-like journey, and the `isolated` group. `AcUx67` adds to its key pages: the periods' list and close form, the corrections' list, form and details, an attendance record's details and edit, and a run's page with its attendance panel;<br>• **lab:** `Ac16DuplicateMessageTests`, `Ac21PayrollDeterminismTests`, `Ac23PayrollFreezeTests`, `Ac24PartialDisbursementTests`, `Ac25FraudHoldTests`, `AcceptanceV2CatalogTests`, `AcHcm29HireContractTests`, the lab journey;<br>• **leastpriv:** `AcOps38FirstBootTests` and `AcTen48LeastPrivilegeTests` (the two migrations);<br>• **E2E:** in CI only.<br>Then compare the seeded counts of `scripts/acceptance-verify.ps1` by psql. Known local-only failures (`Ac42i` without `kubectl`) are recorded, not chased. |
| P-14 | **Scripts.**<br>• Every new or changed `.ps1` is ASCII-only, 5.1-compatible in syntax, and runs here with `pwsh -NoProfile -File …`.<br>• **`scripts/lva057-validate.ps1`** has `-Inventory` (read-only, P-3) and a `-SelfTest` with planted defects, as `pay056-validate.ps1` does. It checks, at least:<br>&nbsp;&nbsp;– the plan's files exist;<br>&nbsp;&nbsp;– the attendance actions carry the three policies;<br>&nbsp;&nbsp;– nothing publishes `IAttendancePeriodClosed`, and the obsolete contract has no reference but the one P-8 allows;<br>&nbsp;&nbsp;– no writer of `DailyAttendanceRecords` outside `AttendanceDayWriter` and `DemoDataSeeder`;<br>&nbsp;&nbsp;– the two migrations have no byte order mark, and HR.Core's lifts `FORCE` around its fill;<br>&nbsp;&nbsp;– no holiday list in code, and the holidays empty in Production and Staging;<br>&nbsp;&nbsp;– the gate's and the attendance settings mirrored in the prod-like overlay;<br>&nbsp;&nbsp;– every new `POST` with `[ValidateAntiForgeryToken]`;<br>&nbsp;&nbsp;– `AcLva57AttendancePeriodTests` required in `prodLike[]`.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile <ProdLike or Lab> -UseLocalBaseImages`. |
| P-15 | **The demo server facts this plan must respect.**<br>• **It runs Production in demo mode** (`Deployment__Mode=demo`): `hradmin` keeps every policy. `e2e-hr-admin` is a second administrator, and `e2e-payroll-processor` and `e2e-payroll-approver` are the payroll people. Check each exists in the demo realm before relying on it; a missing one is added with `scripts/vps/add-demo-user.sh`, recorded.<br>• **P0's read-only picture:**<br>&nbsp;&nbsp;– the running tag and the free disk, and the size of `hrm_seq_data`;<br>&nbsp;&nbsp;– the inventory of P-3 on the demo's data;<br>&nbsp;&nbsp;– a checksum of every attendance record (id, employee, date, status), never printing a name;<br>&nbsp;&nbsp;– the payroll absence rows by reason prefix and state, and a checksum of the runs and lines (PLAN-56's picture);<br>&nbsp;&nbsp;– the depth of the old event's queue and of its error queue.<br>• **What the owner will see change:**<br>&nbsp;&nbsp;– an «فترات الحضور» list and a close page with what is missing;<br>&nbsp;&nbsp;– a «تصحيحات الحضور» list, a request form from a locked day, and the decision page;<br>&nbsp;&nbsp;– the attendance edit shows the employee and the date read-only, and offers no «في إجازة»;<br>&nbsp;&nbsp;– the bulk entry fills working days only;<br>&nbsp;&nbsp;– a run's page shows its attendance panel; under `Warn`, a calculation adds an open audit record that lists who is missing;<br>&nbsp;&nbsp;– **nothing else moves:** no run, line or amount changes. |
| P-16 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan57-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Before the deploy, read-only:** P-15's picture.<br>• **The disk:** at least 30 GB free before the build.<br>&nbsp;&nbsp;– Remove the server's build cache, the dangling images, and the local images of every release older than the running one. They stay in the loopback registry `127.0.0.1:5050` (undo: `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>`). Record each removal;<br>&nbsp;&nbsp;– **if 30 GB cannot be reached that way,** build from at least 28 GB with a watchdog on the server that stops the build **by its PID** below 3 GB, as PLAN-56 did. Below 28 GB: `BLOCKED:`, one line asking the owner about Seq's volume;<br>&nbsp;&nbsp;– **`hrm_seq_data` is never trimmed or deleted unless the owner allowed it in a message in this session.** If he did, apply exactly what he allowed through Seq's own retention setting, record the sizes before and after, and say that it cannot be undone.<br>• **Build once,** on the final code, detached, watched from one process on the server. Then `scripts/vps/deploy.ps1 -Tag <sha12>` (run it again at once if it stops at `keycloak-reconcile`). The migrations run at start-up. **Right before the deploy, the old event's queue is empty** (P-8).<br>• **The checks' own data** (P9):<br>&nbsp;&nbsp;– one real close, of **one small unit** (at most 20 employees), in a past month that no demo run covers;<br>&nbsp;&nbsp;– its working days are filled by the bulk entry as present;<br>&nbsp;&nbsp;– two corrections whose payroll differences cancel each other.<br>&nbsp;&nbsp;A closed period cannot be reopened, by design: record the month and the unit, and why. No other demo record is written, and no run of the demo is calculated, cancelled or changed.<br>• **Rollback safety:** find out what PLAN-56's tag does with this plan's data, and record it in `OPS_VPS_SINGLE_HOST.md`:<br>&nbsp;&nbsp;– it ignores the new tables and columns;<br>&nbsp;&nbsp;– its old close publishes the old event again, with its old consumer, and writes on locked days;<br>&nbsp;&nbsp;– its `AbsenceRule` skips a refund row, and its loader carries nothing;<br>&nbsp;&nbsp;– whether it can read a row with a value it does not know (PLAN-56 found that names stored as text break the older reader).<br>&nbsp;&nbsp;So no close and no correction while the older release runs.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn:<br>&nbsp;&nbsp;– roll back to the previous tag, or fix forward;<br>&nbsp;&nbsp;– restore the backup only if data was damaged;<br>&nbsp;&nbsp;– prove the demo works, and record the downtime. |
| P-17 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P10, after the local CI steps and the pre-push check are green, in the same turn as the pre-push check (Lessons).<br>2. `gh pr create`, then wait («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, run the unit suite and the format check, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>`, and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-18 | **No regression of PLAN-25, PLAN-36..56, PLAN-66 and PLAN-67.** These stay green:<br>• **every existing payroll result,** unit for unit, for a run whose inputs this plan does not add to: the golden pack tests (`Gov002LegalGoldenPackTests`), `PayrollEnginePipelineTests`, the determinism tests, `PayrollFinancialInvariantsTests`, `Pay029*`, `Pay023FrozenRunIsolationTests`, `Pay004ArrearsTests`, `Pay056*`, AC-21, AC-23, AC-24 and AC-25;<br>• the leave tests (`Leave*`, `Hcm002*`), the inbound event tests, `OutboxAtomicityIntegrationTests` (the attendance case rewritten on the new service with its meaning kept), and AC-16;<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests`;<br>• `AcIam50`..`AcIam53`, `AcPay54`..`AcPay56`, `AcUx67`, `AcWf43`..`AcWf46` with the orphan class, `AcPol47`, both `AcTen48` classes, both `AcPol49` classes, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, `AcHcm29`, and both recruitment journeys;<br>• the architecture tests (`WriteActionsHaveExplicitPolicyTests`, `OrgScopeCoverageTests`, `InterfaceTextGuardTests`, `GuardSelfTests`, `PayrollWriteActionsTests`, `PayProfileWritersTests`, `PayrollReferenceWritersTests`, `Pay054FrameworkWritersTests`), `OutboxPublishOrderingArchitectureTests`, `Rel004CompatibilityTests`, `ProductionWiringMatrixTests`, `CiStepsParityTests`;<br>• the validate scripts `pay056`, `pay055`, `pay054`, `iam053`, `iam052`, `iam051`, `iam050` and `ux067`, `scripts/vps/lint-compose.ps1` (both chains), and `scripts/int037-wiring-check.ps1`. `pol049-validate.ps1` is red on `main` already (Where things stand): record its state before and after. |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-37:** the binding of `hr-contracts-web` in production (G-LVA-18).
- **PLAN-52:** the branch scope of `Rosters` and the rest of HR.Core (G-IAM-04).
- **LVA-A (wave 2):** the atomic approval and cancellation of a leave on PostgreSQL (G-LVA-01). This is why the acceptance injects the leave event.
- **LVA-B/D/E/F (wave 2):** balances, the legal catalogue, governance, overlap and ESS (G-LVA-02, 04–09).
- **LVA-G (wave 2):** a «mission» status in attendance and the central dated calendar (G-LVA-11, 15).
- **LVA-H / PAY-H (wave 2):** shifts, timing, lateness, devices and overtime (G-LVA-14, and the non-attendance part of G-PAY-19).
- **PAY-H (wave 2):** the payroll period with a cut-off for every input, the historical wage of a difference, and returning a cancelled leave's deduction after it was applied (G-PAY-07).
- **PLAN-56, done:** the run's states and the releasing cancellation. **PLAN-59:** the full cycle with production settings to the disbursement. **PLAN-40, done:** the general atomic publishing and its guard. **PLAN-65:** the programme's «Done» documents.

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
- touch the side PRs (#40, #42) or their branches;
- disable host-key checking;
- widen a policy, a scope or a role to make a page or a test work;
- add a row to any ceiling list: they only shrink;
- write an attendance day around `AttendanceDayWriter`, reopen a closed period, or change a row a payroll run applied;
- write the gate's warning as a closed audit record;
- put a list of holidays in code, or in a Production or Staging file;
- trim or delete Seq's volume without the owner's word in this session;
- close attendance on the demo beyond P-16's one small unit, or calculate, cancel or change a demo run;
- touch another project's Docker images, containers or volumes, or the owner's dev containers;
- print a password, or rotate a secret that exists on the server;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** (port 22 at P0 excepted: Preconditions 4).
- **No month of P-12's range lets a payroll run calculate** on a fresh stack (P0).
- **An existing payroll result would change** for a run whose inputs this plan does not add to, and the cause cannot be removed without changing the plan's design.
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning.
- **12 GB free for an image build cannot be reached** without touching other projects, **or a port the tests read is held** by something else (P-2).
- **The server's free space is under 28 GB** after P-16's removals, and the owner has not allowed Seq's retention.
- **Port 22 is still blocked when P9 starts:** one line asking the owner to restore it.
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, keep-awake, preconditions, baseline

1. Write the `Started (UTC)` line (see «Timing»). Start the keep-awake process (P-2). Check the Preconditions.
2. `git switch -c feature/plan-57-lva-attendance-period-payroll-handoff origin/main`. Bring in PLAN-56's merge note, and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan57-baseline.md` (P-3): the dependencies, the build, the migrations, the tests, and `scripts/lva057-validate.ps1 -Inventory`, locally and on the demo.
4. **The demo's picture** (P-15), with the old event's queue. Then **the reserved months** (P-12): a run of agency A calculated in the first of them on a fresh local stack, then cancelled.
5. If port 22 is blocked, record it, and take the demo's picture at the start of P9.

Commit: `chore(lva057): baseline, the attendance data inventory, every writer of an attendance day, the reserved months proved, the demo's picture`.

## P1 — The model, the calendar and the summary (plan §1–3 entities, P-5, P-10, P-11's first migration)

- **The four HR.Core entities and their enums,** the record's three new fields, `AttendanceCalendarOptions` and `AttendanceCalendar`, the pure `AttendanceSummaryCalculator`, the configuration.
- **Migration `LVA057_AttendancePeriods`** with its fill.
- **Tests:**
  - `Lva057AttendanceSummaryCalculatorTests`: working days only, rest days, configured holidays, hire and end of service inside the month, unrecorded days, and records on non-working days counted nowhere;
  - `Lva057MigrationPostgreSqlTests`.

Commit: `feat(lva057): attendance periods, versioned employee summaries and corrections in the model; the calendar from configuration; migration LVA057_AttendancePeriods marks leave-made records`.

## P2 — One writer for an attendance day (plan §2, P-6)

- **`AttendanceDayWriter`** and `AttendanceLockService`; the controller, its four views and the picker; the leave consumers in one save; the reconciler.
- **The three policies** on the attendance actions (P-4).
- **Tests:**
  - `Lva057AttendanceDayWriterTests`: every rule, each refusal changing nothing;
  - `AttendanceSingleWriterArchitectureTests` with its planted writer;
  - overposting on the edit;
  - the leave consumers' single save, and their idempotence;
  - the reconciler's new reading.

Commit: `feat(lva057): one writer for an attendance day; locked days refused; the edit cannot move a record; leave-made days restorable; the bulk entry fills working days only`.

## P3 — The close and the contract (plan §1, §4, P-5, P-8)

- **`AttendancePeriodService`;** the close with its rules, in one save with the closure and the events; the screens and the API.
- **`IAttendanceSummaryPublished`.** The old event obsolete with no publisher, its consumer removed (or kept per P-8), the old close path removed, the inventories, and the runbook.
- **Tests:** the employees of a close (a sub-unit, an end of service inside the month, a hire after it); `RequireComplete` and its two alternatives; idempotence; the period's status; the branch closing outside its tree (403).

Commit: `feat(lva057): the attendance close writes one versioned summary per employee and publishes IAttendanceSummaryPublished in the same save; the old close and its event end`.

## P4 — The correction (plan §3, P-7)

- **`AttendanceCorrectionService`,** the controller (MVC and API), the views, and the system corrections from leave events.
- **Tests:** `Lva057AttendanceCorrectionRulesTests` covering:
  - a correction on an unlocked day, a short reason, the same status, `OnLeave` by hand;
  - the requester approving (400) and a branch-scoped approver (403);
  - the atomic approval and its version;
  - a correction that changes no figure;
  - a leave event on a locked day, approved and cancelled, each once.

Commit: `feat(lva057): a change after the close is a correction with a reason, approved by a central person who is not the requester, and publishes the summary's next version`.

## P5 — Payroll's side (plan §5, P-9, P-11's second migration)

- **The receipt, the four fields, the consumer, `PayrollAttendanceFreeze`,** the loader's carry, the refund in `AbsenceRule`, the gate before `BeginCalculating`, the two reads, and the run's panel.
- **Migration `LVA057_AttendanceHandoff`.**
- **Tests:**
  - `Lva057AbsenceRuleRefundTests`, with `PayrollRunReconciliation` and `PayrollNet`: a refund with no other deduction, and a refund consumed;
  - `Lva057PayrollAttendanceGateTests`: `Warn` (an open record, never a closed one), `Block` (the run unchanged), `Off`, and a correction or final settlement exempt;
  - the consumer's semantics, every rule.

Commit: `feat(lva057): payroll records each attendance summary version as one difference, carries it past a frozen month, refunds a negative one, and checks attendance before a calculation`.

## P6 — The PostgreSQL proofs (the plan's integration tests)

- **`Lva057AttendanceCloseIntegrationTests`:**
  - two concurrent closes;
  - **outbox atomicity:** a failure injected after the event is staged leaves no summary and no outbox message;
  - a correction under an `xmin` conflict;
  - the close of 2,000 employees × 31 days under 10 seconds.
- **`Lva057PayrollAttendanceHandoffIntegrationTests`:**
  - a duplicate, the reverse order, the carry to the next cycle, the negative refund, `Block`;
  - two versions of one summary at once on one receipt;
  - the tenant on the receipt and the row;
  - the freeze in every run state.
- **`OutboxAtomicityIntegrationTests.CloseAttendancePeriod_SavesAfterPublishLoop`** rewritten on the new service.
- **The full integration filter** on PostgreSQL, CI's filter.

Commit: `test(lva057): PostgreSQL proofs of concurrent closes, the close's outbox atomicity, version races on one receipt, the carry past a frozen month and the gate`.

## P7 — Acceptance, then the independent review (plan Acceptance, P-12, P-13, P-14)

1. **The pieces:** `AcLva57AttendancePeriodTests`, `lva057-periods.json`, the holidays in `docker-compose.acceptance.yml`, `AcceptanceMessagingHelper`'s two helpers, `ci-scope.json`, `ac-ownership.json` (`AC-LVA-57`, owner PLAN-57, `provesClaim: true`, its reason), the personas' scenarios, AC-IAM-52's day, `scripts/lva057-validate.ps1`.
2. **Acceptance.**
   1. Check the disk and the memory (P-2). Reset the local stacks (the scenarios). Build the images, one service at a time. Start the prod-like stack, and record the time to ready.
   2. Run `AcLva57AttendancePeriodTests` (AC-LVA-57a..h), positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   3. Then P-13's lists, `scripts/lva057-validate.ps1` (and `-SelfTest`), and the seeded counts by psql. All required classes must pass.
   4. `down -v`. Restore the committed evidence.
3. **The full Testcontainers step on the host,** with CI's filter.
4. **Independent review** (one fresh subagent), before the server, so the server build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - a write of an attendance day around the writer, a locked day written, an edit that moves a record, `OnLeave` typed by hand, a leave's removal that touches another record;
   - a close that misses a sub-unit or a leaver, counts a rest day, writes part of itself, or publishes outside its save;
   - a correction approved by its requester or a branch, or one that changes the day without a new version or the version without the day;
   - a consumer that records a version twice, takes an older version, computes a difference from a stale base, or writes a zero row;
   - a carried row that a frozen run's figures absorb, that two runs both take, or that stays free for another class;
   - a refund that is not consumed, or that breaks the validation;
   - a gate that moves the run before it refuses, or a warning written as a closed audit record;
   - a request of another agency that has an effect;
   - an API write without an antiforgery token, or a name CodeQL reads as sensitive flowing into a log or audit;
   - a migration that fills nothing under forced row security;
   - a test changed to pass rather than to keep its meaning, or a part of the plan deferred, skipped or listed to save time;
   - something PLAN-56's tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(lva057): AC-LVA-57a..h pass on the prod-like stack; review fixes; CI scope and ownership`.

## P8 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to `HRCoreDbContext` or `PayrollDbContext` after yours, remove yours and add them again on top. Rebuild, and rerun the unit, architecture and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you.

Commit (only if the merge needed fixes): `fix(lva057): main merged, conflicts resolved`.

## P9 — The demo server (P-15, P-16)

Record every server change and its undo command **as you make it**.

1. Check SSH and P-16's condition.
2. Take the backup, then **verify** it. Take the read-only picture (P-15), if P0 could not.
3. **Check the server's disk, build and deploy.** Run the build once, detached, on the final code, watched from one process on the server. Check the old event's queue is empty. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`, again at once if it stops at `keycloak-reconcile`. Record the downtime and the time to all healthy.
4. **Then check**, with evidence in `artifacts/plan57/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them. The new checks go in an `AcLva57VpsChecks` class. Each writes realistic Arabic text, keeps its marker in a field the reader does not rely on, and leaves nothing open.
   1. **The migrations:** both recorded; the new tables under forced row security; `Source = Leave` on as many records as the inventory counted, and no attendance record changed otherwise (the checksum); the runs and lines as in the picture.
   2. **One close** (P-16): a small unit's past month, filled by the bulk entry as present → closed. Its summaries are v1, payroll's receipts match their number, and a manual write on one of its days is refused.
   3. **Two corrections:** `hradmin` asks for one day to be absent and `e2e-hr-admin` approves → v2, and a difference row of one day in payroll. Then the reverse correction → v3, and a refund row: the two differences cancel each other. `hradmin` approving his own request → 400.
   4. **The gate:** `attendance-status` of an existing demo run, read-only → `Warn`, with its missing list.
   5. **The screens** open in Arabic under the enforced CSP with no violation (PLAN-67's method, Chromium without QUIC, page by page, never beside a build): the periods' list and close page, the corrections' list and decision page, an attendance record's edit and details, a run's attendance panel. Names, not ids.
   6. **Everyone else is unchanged:** the PLAN-36..56 server checks pass, and `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass. The checks that flicker from this machine are rerun alone.
5. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-16 says, prove the demo works, and record it.

Commit: `test(lva057): demo server with one small attendance close, two corrections netting to zero, and the payroll gate; PLAN-36..56 server checks still green`.

## P10 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/HCM_002_ATTENDANCE.md` rewritten to the model as built;
   - `docs/runbooks/attendance-period-close.md` (new);
   - `docs/RELEASE_PRODUCT_SCOPE.md`: the attendance line matches D-LVA-3;
   - `docs/EVENT_PUBLISHER_INVENTORY.md` and `docs/MESSAGE_EVENT_MATRIX.md`: the new event;
   - `docs/PAYROLL_RULES.md`: the receipt, the difference, the carry, the refund, the gate;
   - the «الحالة» lines of G-LVA-03 and G-LVA-10 in `HCM_002_LEAVE_ATTENDANCE_GAP_STUDY.md`, and the attendance line of G-PAY-19 in `PAY_001_GAP_STUDY.md`, with their evidence;
   - `docs/CI_AND_TESTING.md`: the new class and AC-IAM-52's day; `OPS_VPS_SINGLE_HOST.md`: what the demo users see, the close the checks made, the backup location, the change table, the rollback note;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local gates, before any push:**
   - the full Testcontainers step on the host, with CI's filter (P7, if nothing it builds or tests changed since; otherwise again);
   - the local CI steps (P-2) on HEAD;
   - then `pwsh -NoProfile -File scripts/plan035-prepush.ps1`.

   All must be green, with every step not run named. The report is committed before them, because a commit after a green local run needs another run.
3. **`reports/plan57-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. **Decisions applied and deviations:** the reserved months, the HTTP answers, the old consumer's fate, AC-IAM-52's day, the correction API kept in scope, and anything else. No deviation defers part of the plan.
   4. **The period and the lock:** the close's employees, its rules, its atomicity, the performance measured.
   5. **One writer:** what writes a day now, what was removed, the leave consumers and the reconciler.
   6. **The correction:** its rules, the separation of duties, the system corrections.
   7. **The contract:** the event, its eight rules, what PLAN-59 calls.
   8. **Payroll's side:** the receipt, the difference, the freeze, the carry, the refund, the gate and its warning.
   9. The existing tests that changed, each with the meaning it keeps; the numbers that did not move.
   10. The guards and their own negative tests.
   11. Unit, architecture, integration and acceptance results; the PR run's artifact link as the evidence of record.
   12. **The server:** before and after, changes and their undo, the backup location, the close the checks made, the rollback note.
   13. **Changes for API clients and operators:** the new API, the old close's end, the attendance actions' policies, the runbook.
   14. Items that other plans own (Scope boundaries), and defects for later plans.
   15. **Timing:**
       - total wall-clock;
       - active time, waiting for CI, and every stop with its cause;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `pwsh -NoProfile -File scripts/plan-timing.ps1 -From 25 -To 67`.
   16. **A readiness note** for PLAN-58, PLAN-59 and wave 2:
       - the contract PLAN-59 calls (close, receipts, attendance status), and the gate it may raise to `Block`;
       - the carried rows and the refunds PLAN-58's disbursement meets;
       - what LVA-A changes when the leave event is no longer injected;
       - the historical wage PAY-H owns.
4. **Merge (P-17), by you, in the same turn as the pre-push check:**
   1. Merge the feature branch into a new `release/plan-57` from `main` (`--no-ff`).
   2. Write `reports/plan57-pr.md` (short).
   3. `git push -u origin release/plan-57`
   4. `gh pr create --base main --head release/plan-57 --title "PLAN-57: a closed attendance period, corrections as differences, and a reliable handover to payroll" --body-file reports/plan57-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then:
   - `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`;
   - record the PR link, each check result, the merge commit and the post-merge run in `reports/plan57-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`;
   - commit that note on a local branch `docs/plan-57-merge-note`, as the plans before did, so the next plan brings it in. **Do not open a second PR for it;**
   - stop the keep-awake process by its PID (P-2).

## Definition of done

- **No write of an attendance day outside `AttendanceDayWriter`** (the architecture test green), and the attendance actions carry their new policies (`WriteActionsHaveExplicitPolicyTests` green).
- **`ClosePeriodAndPublishAsync` and the old consumer are gone** (or the consumer stays one release per P-8), and `IAttendancePeriodClosed` has no publisher.
- **The close and the correction write the summary and the event in one save,** proved on PostgreSQL by a failure injected after the event: no summary and no outbox message.
- **Payroll records each version once,** carries a late difference past a frozen month without touching it, refunds a negative one, and warns or refuses before a calculation as configured.
- **Every existing payroll result is unchanged** for a run whose inputs this plan does not add to.
- **Nothing in the plan was deferred to save time,** the correction API included.
- **The tests:** AC-LVA-57a..h = Pass (the PR run's artifact is the evidence of record); `Lva057PayrollAttendanceHandoffIntegrationTests` and the other PostgreSQL tests green; the PLAN-36..56 suites green.
- **The server** runs this code, with one small closed period of the checks and no run or amount changed.
- **The documents** P10 lists are updated, among them the «الحالة» lines of G-LVA-03 and G-LVA-10 and the attendance line of G-PAY-19.
- **The merge** is done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed for HR and payroll users, and what the owner will see on the demo;
- which unit and month the demo checks closed, and why;
- how long the plan took, how long it waited for CI, and every stop with its cause;
- the PR link;
- the path of the final report.
