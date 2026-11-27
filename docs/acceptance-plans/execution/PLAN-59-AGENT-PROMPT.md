# Task: PLAN-59 — A full payroll cycle proved with production settings (PAY-D)

The deliverables:
- **One monthly payroll cycle proved end to end on the production-like profile, by HTTP alone, with a different person for each role:**
  - its inputs: the legal framework in force (PLAN-54), verified bank accounts (PLAN-55), a salary change decided by two people (PLAN-53, under D-53-1: P-4), and an attendance month closed for the whole agency that feeds the absence deduction (PLAN-57);
  - then calculate → validate → submit → reject → recalculate with an approved adjustment → two approvals → an independent review by two auditors → the posting → processing with one employee held → the bank file → his account verified → the supplementary file → the payslips → the bank's confirmation recorded by two people (`Settled`).
- **AC-12 proves separation of duties for the right reason,** and is required.
- **The fourteen payroll integration tests are green,** and the regime gate's blocked path and the handler's own separation-of-duties rules have tests of their own.
- **No payroll acceptance script writes SQL:** runs are cancelled through PLAN-56's API, and AC-21..23 take a month of their own.
- **A monthly-cycle runbook** for operators, and the pilot's two success indicators tied to the evidence.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P10** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**Why this plan exists** (`docs/PAY_001_GAP_STUDY.md`: G-PAY-05, and the PAY-D row):
- **No document proves one complete payroll cycle.** AC-12 has been `Blocked` since PLAN-41: it submits for review a run it never calculated, and it expects the wrong answer from the approver's posting. The pilot's two success indicators («دورة رواتب واحدة مكتملة» and «إغلاق فترة حضور واحدة مرتبطة بالرواتب», `docs/PILOT_DEPLOYMENT.md`) have no evidence.
- **PLAN-53..58 built the pieces, and each proved its own:** the salary change, the legal framework, the bank account, the run's state machine, the attendance handover, and one payment path with four eyes. Nobody has driven one month through all of them, by HTTP only, with one person per role.
- **Two acceptance scripts still cancel runs and release inputs by SQL** (AC-21 and AC-22).
- **The regime gate's blocked path has no test of its own** (PLAN-34 left it here). Since PLAN-58's conflict guard, no real persona reaches the handler's own separation-of-duties rules, so nothing proves them either.

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder. There is no second clone and there are no locks.
- **The main folder** is `/home/omar/HRManagement` on the owner's Linux notebook, where PLAN-53..58 ran. If the owner opened this session elsewhere, that folder is the main folder: record it. A folder's path is never a reason to stop.
- **PLAN-58 is merged** (Preconditions).

**Complete execution** (owner, 2026-11-12, standing since PLAN-51): the owner wants every plan carried out in full, however long it takes.
- Time is never a reason to skip, defer or allowlist anything.
- Only what the plan itself assigns elsewhere stays out: its «Out of scope».
- Where the plan speaks in general, this prompt makes it concrete, and all of it is in scope:
  - **the integration tests:** the fourteen of G-PAY-05 green, the four on PostgreSQL included; `Pay059RegimeBlockerTests` (the refusal of `BlockRun` names the blocked employees, and a host with no client refuses); `Pay059PayrollSodHandlerTests`;
  - **AC-12** rewritten on a calculated, validated run, and required on the prod-like profile;
  - **the scripts:** both SQL-free on the cancel API; AC-21, AC-22 and AC-23 with a month of their own, `[RequiredAcceptance]`, run on the prod-like profile (AC-21 and AC-23 required in CI; AC-22 proved locally: P-11);
  - **the reads an SQL-free cycle needs** (P-9);
  - **AC-PAY-59** and its driver: three consecutive executions on three consecutive months locally, the PR run's execution in CI, and AC-PAY-59b;
  - **the runbook** and the documents of the plan's §5;
  - **what earlier plans handed here:** the run's scope against hire dates and the attendance gate's severity (P-5);
  - **every new read names its policy and its org-scope stance,** and every new or changed API write validates an antiforgery token — each from its first commit.

**Where this prompt departs from the plan's text** (each with its reason in the decisions, and in the final report):
- **Step 3's date.** D-53-1 (the owner, 2026-10-31) refuses a salary change dated before the current month. D-PAY-22's month has ended. So Z's change is dated the first day of next month, decided by two people, and cancelled at the end; the cycle's run pays Z the salary in force in its month (P-4).
- **The cycle runs on a stack of its own in CI** (P-11): it gives every employee of agency A a verified account, and AC-PAY-55 and AC-PAY-58 need employees with none.
- **The posting's review rule and message are PLAN-58's:** the review is opened by `auditor` and closed by `a-auditor-2`, and a posting before it answers «يجب أن يفتح مدقق سجل مراجعة للتشغيل ويغلقه مدقق آخر قبل الترحيل.» (the plan's older text no longer exists).
- **AC-22 stays out of CI:** it kills Payroll (QA-G's drill). It is proved on the local prod-like stack.
- **AC-12, AC-21 and AC-22 take their months by rules of their own** (P-7, P-8); AC-12 keeps D-PAY-22.

**Standing owner instruction on merges** (PLAN-38 to PLAN-58, PLAN-66 and PLAN-67): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-17).
- **Never ask the owner whether to merge:** his instruction is the permission. Asking cost PLAN-51 2 h 09 min.

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Check formatting on the files you changed (see «Lessons»).
4. Commit, using the phase's commit message.
5. Update **`reports/plan59-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan59-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan59-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `date -u +%Y-%m-%dT%H:%M:%SZ` (on Windows, `(Get-Date).ToUniversalTime().ToString('o')`).
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, each acceptance run (each of AC-PAY-59's three executions apart), the GHCR build and the deploy, and every GitHub Actions run.
- Record the time spent waiting for CI, and **any time the session stood still** (a stopped turn, the usage limit, the machine asleep or restarted, a full disk, a blocked port, GitHub), each with its cause.
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session in Claude Code at `xhigh`). PLAN-50 stopped once at the usage limit of the owner's plan. Economy is about tokens, never about doing less of the plan:
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only; root-causing separate failures.
- Run **one independent review**, before the push and the server phase, so that the server's images carry its fixes. Use a refuter only for findings rated high.
- If the owner switches the session's multi-agent mode on, use it for one read-only verification of the review's fixes, two agents at a time.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan59/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.
- Build the acceptance images once per code state. Run the unit and integration filters before a stack run, and rerun only the classes that failed.

**Long commands.** A foreground command that outlives the tool's time limit ends the turn. So:
- Run anything that can outlive the limit in the background or detached, with its log under `artifacts/plan59/`, and poll it. That covers the unit suite, the Testcontainers step, an image build, a stack start, each acceptance run (AC-PAY-59 takes minutes per execution), the local CI steps, the GHCR build and the server suites.
- **Keep every wait short: poll a background job with calls of about five minutes at most, never one call near the tool's limit.** PLAN-55 and PLAN-56 stopped three times during one long wait on a job (9 h 38 min, 7 min, 12 min), each ended only by the owner's «Try again». PLAN-57 and PLAN-58 polled in short calls and no turn ended on a wait.
- **A wait that returns is not the end of anything: the very next action is another wait or check, in the same turn.** A turn ends only with the final reply, `BLOCKED:` or `WAITING-CI:`.
- Run **one heavy job at a time**: never an image build beside a host test run or a browser check (P-2).
- Stop a process **by its id**, never by a pattern: PLAN-53's watch loop killed its own shell with a pattern that matched itself, and a build filled the disk.

**Waiting — for CI and for your own work.**
- **Never end the turn to wait** for CI or for work you started (a review, a build, a stack, a long check). Wait for it in the turn, as PLAN-49, PLAN-50, PLAN-57 and PLAN-58 did.
- **For CI:** run `gh pr checks <n> --watch --interval 60` in the foreground with a time limit of about five minutes. When it returns because of the limit, run it again, until every check has finished. A call that returns at the limit is not a refusal: run it again.
  - **For the GHCR build** (P9): `gh run watch <id> --exit-status` blocks until the run ends; give it the same limit of about five minutes and run it again, or poll `gh run view <id> --json status,conclusion`.
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

You are a senior .NET engineer for payroll systems that must be correct, auditable and safe with people's money:
- an end-to-end acceptance proof driven by HTTP alone, one person per role, where every check is a read of the product's own API;
- the monthly cycle: legal framework, master data, attendance close, calculation, validation, rejection and reopening, approval thresholds, independent review, posting, payment batch, bank file with control totals, holds, supplementary file, payslips, bank confirmation;
- maker-checker and separation of duties, at the handler and at the policy;
- acceptance data that never collides across test classes: reserved months, isolation, and clean-up through the product's own API;
- EF Core 10 with PostgreSQL; ASP.NET Core MVC and Web API behind the YARP gateway: named policies, org-scope stances, antiforgery on API writes;
- acceptance tests on Docker Compose and in CI stage jobs; PowerShell (ASCII, 5.1-compatible syntax, run by `pwsh` 7 here) and bash; the GHCR release path.

You work on the `HRManagement` repository on the owner's Linux notebook (P-2), signed in to GitHub with push rights, and you operate the demo server through `ssh hrm-vps`, as PLAN-36..58 did.

**Where things stand** when this prompt was written (2026-11-27):
- **`main` is `708d2004`:** PLAN-58 merged through PR #59 at 2026-11-27T04:03:30Z. Before the merge its release branch took these fixes:
  - `bc4703ce`: a nullable warning that is an error in Release only;
  - `64dfffff` and `4a6f21ae`: the prod-like stage's job limits by QA-066's rule, and the older server checks meeting `hradmin`'s conflict refusal;
  - `11fd674c` and `274861a6`: AC-IAM-52a picks an attendance day whose list fits one page, because AC-LVA-57 fills whole months.
  Its merge note is on the local branch `docs/plan-58-merge-note` of the owner's machine. Read it, and PLAN-58's final report.
- **The owner's GHCR change is on `main`** (PR #58, merged 2026-11-26T18:38:30Z): the 17 release images are built in GitHub Actions and pulled by tag; **nothing is built on the server any more** (`docs/OPS_VPS_SINGLE_HOST.md`, «صور الإصدارات على GHCR»). The branch `ops/ghcr-release-images` and the worktree `/home/omar/HRManagement-ghcr`, if still there, are the owner's: never touch them.
- **The demo server runs PLAN-58's tag `25be64cf9fa8`** (deployed 2026-11-26T20:31Z, previous `0dd3564560b6`): your rollback target, on GHCR.
  - every container healthy, and 54 GB free, after PLAN-58's checks. GHCR keeps every tag pushed to it;
  - its last backup is `/opt/hrm/backups/pre-plan58-20261126T201439Z`;
  - **what the earlier checks left, not to be touched:**
    - three cancelled runs of 2028-09 from PLAN-56's checks; one calculation held the 425 employees `REGIME-BLOCKER` on one of them (shown `Active`, holding nothing);
    - versions under `LAW_50_2004_CHK54`, approved and never published;
    - rejected requests, among them PLAN-58's four-eyes request on an arrears entry;
    - PLAN-57's closed August 2026 of the unit `HR-ATT`;
  - **it cannot run a cycle:**
    - it has no poster, no second approver (its runs are above 50,000,000) and no auditor;
    - a calculation holds all 425 of its employees `REGIME-BLOCKER`, since no service regime is classified there;
    - `hradmin` is refused in payroll's financial acts, with the conflict named (D-PAY-20).
    This plan adds no user there and posts nothing (P-15);
  - **its realm's people:** `hradmin`, `e2e-hr-admin`, `e2e-payroll-processor`, `e2e-payroll-approver` and the other e2e personas (`testdata/e2e/personas.json`).
- **PLAN-58 took 16 h 13 min to its merge** (started 2026-11-26T11:50:02Z). Of that, 3 min stood still: one `BLOCKED:` at its server phase (the demo ran the owner's unmerged GHCR release), answered «ادمجه واكمل». No `WAITING-CI`, and no turn ended on a wait.
  - **Its stretches:**
    - P0–P9 6 h 43 min; the GHCR build 11 min; the deploy 5 min 49 s; its own server checks 11 min;
    - **the older plans' server checks 2 h 49 min:** 78 checks, of which 72 passed at once and the rest were adapted or rerun alone;
    - five CI runs on the PR, the last one green: the prod-like stage at its 35-minute limit; an architecture rule; green; AC-IAM-52a's collision with AC-LVA-57's months; and the head with its fix.
  - **Its local waits:** a solution build 4 min 41 s; the full unit suite 8 min 7 s; the Testcontainers step 18 min; 16 service images one at a time 25 min; a prod-like stack 16 min to ready; the migrator image 21 min; the local CI steps 15 + 47 min.
- **The owner's machine** (PLAN-53..58 progress files):
  - Linux 6.14, 15 GB RAM with a 4 GB swap; PowerShell 7.7 at `~/bin/pwsh`, with `~/bin/powershell` linked to it as CI has; .NET SDK 10.0.400;
  - `/` is 233 GB, with 9 to 50 GB free across the last six plans. Docker also holds other projects' images, containers and volumes, and the owner's dev containers: never touch them;
  - images are built one service at a time from local base images; **the migrator image through `docker compose build`** (no buildx here, so the job's `docker build` cannot read `COPY --exclude`);
  - **`scripts/ci-local.ps1` cannot run there:** the same `scripts/ci/steps.json` runs natively with `scripts/ci/run-steps.sh --all` in a fresh clone of HEAD, every step not run named, and `scripts/plan035-prepush.ps1` accepts those by name;
  - **its compiler servers go stale** (Razor parse errors on unchanged files): run the local CI steps with `UseSharedCompilation=false`, `UseRazorBuildServer=false`, `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0`, and `dotnet build-server shutdown` after such a failure;
  - **the owner's dev container `hrm-keycloak` restarts about every minute:** Chromium then meets `ERR_NETWORK_CHANGED`, and stalls over HTTP/3. The browser checks launch Chromium without QUIC, probe page by page (`Ux067PageProbe.ProbeEachAsync`), and never run beside a build;
  - **its memory under a stack start:** PLAN-55's prod-like stack failed three starts at a load of 54 with the swap full;
  - a full unit run was once aborted by a test host crash; it passed when rerun with `--blame`;
  - port 22 reached the server without the VPN, but refused this machine for 8 minutes after many ssh connections (PLAN-56); `kubectl` is absent (`Ac42i` fails locally only); the job's Chromium step uses `--with-deps` (sudo): use the Chromium in `~/.cache/ms-playwright`.
- **The side PRs** about the E2E Playwright install (#40, #42) and Dependabot's PRs, if still open: never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI today:**
  - a PR run has 15 checks (about an hour). One acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - the required checks on `main` are `CI`, `CodeQL`, `Gitleaks`, `E2E` and `Acceptance`. The last step of `Acceptance` («Every stage job succeeded») fails when the image build or any stage failed;
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - **the acceptance stage jobs** are the `stageJobs[]` of `tests/acceptance/ci-scope.json`, mirrored by the `acceptance-stage` matrix of `.github/workflows/ci.yml`, each with a time limit (QA-066's rule: about 1.5 times the measured duration):
    - `lab-main` 35 and `lab-rest` 30;
    - `prodlike` 55: the groups `journey`, `main`, `isolated` and `s2s42` on one stack;
    - `prodlike-final` 60: it waits for `lab-main`, `lab-rest` and `prodlike`, and reads their folders;
    - `prodlike-policy` 25, `leastpriv` 20 and `meta` 60.
    Each job starts its own stack. **The acceptance project runs one class at a time,** in an order that changes from run to run;
  - **the payroll classes today:**
    - `Ac12PayrollSodTests`: lab main, not required, `Blocked`;
    - `Ac21PayrollDeterminismTests`: lab main, not required;
    - `Ac23PayrollFreezeTests`, `Ac24PartialDisbursementTests` and `Ac25FraudHoldTests`: lab main, required;
    - `AcIam53SalaryChangeTests`, `AcPay54`..`AcPay56`, `AcPay58DisbursementGovernanceTests` and `AcLva57AttendancePeriodTests`: prod-like main, required;
    - `Ac22PayrollResumeAfterKillTests` is in `excluded[]` («Deliberate chaos: kills Payroll in the drill (ops-drill)», QA-G);
  - **D-QA-20:** the evidence of record is the PR run's artifact `acceptance-evidence`. Nothing of a local run is committed;
  - **the build and test steps live in `scripts/ci/steps.json`;** `CiStepsParityTests` keeps `ci.yml` in line with it;
  - **red on `main` and not this plan's:** `scripts/pol049-validate.ps1` (28 findings, PLAN-67's reshaped catalog). Record it; do not chase it.
- **From PLAN-51..58** (their final reports and readiness notes):
  - **policies:** `Payroll.Process`, `.Approve`, `.Post`, `.Reverse`, `.RunCancel`, `.MasterData`, `.ChangeCancel`, `.LegalAttest`, `.Repair`, `.AuditReview` (`HrManagementRoles.cs`), with their rules in `PayrollPolicyRules` and `HrManagementAuthorizationExtensions`;
  - **the tests of reach:** `PayrollWriteActionsTests` fixes each Payroll write's policy and who reaches it, role by role; `WriteActionsHaveExplicitPolicyTests` refuses a write with no explicit write policy; `OrgScopeCoverageTests` needs a stance on every controller;
  - **antiforgery on every API write:** a `GET …/antiforgery` endpoint per API controller. `AcceptanceHarness.CreatePayrollClientAsync` carries a Payroll token, accepted by every Payroll API write;
  - **PLAN-55's pinning:** a line's destination is `SalaryLine.BankAccountId`, pinned at the calculation's snapshot from the account in force. A line with no account is legal;
  - **PLAN-56's machine:** a rejection gives `Draft` at the next `Revision`, with a `PayrollRunRevisions` row; a cancellation frees the period and the inputs; holds are untouched by a rejection, a cancellation, a restart or a reversal;
  - **PLAN-57's handover** (its §7 and §16): close a month for the whole agency (`POST api/attendance/periods/{yyyy-MM}/close` with no unit, as a central user; 409 lists who lacks a working day), read `GET api/payroll/attendance/receipts?periodStart=` until it holds one receipt per summary, calculate, then read `attendance-status`. The gate's warning is an open audit record that the posting ignores;
  - **PLAN-58's one path** (its §4–§9 and §17):
    - the posting makes one `Disbursement` batch and one order per positive line through `PaymentBatchIssuer`; an order with no pinned account is `Held` `NO-ACCOUNT`;
    - only the poster's `process` and `submit-held` send. Each runs the fraud scan and the gate per employee, and writes one bank file (`PaymentBatchFile`: `Initial`, `HeldRelease`, `Reissue`; CSV lines `PaymentOrderId,Amount` and `CONTROL,<count>,<total>,<batch>,<sequence>,<kind>`);
    - verifying an employee's new account re-pins his unsent `Draft` and `Held` orders, and lifts `NO-ACCOUNT`;
    - four eyes are requests and decisions (`PayrollDualControlRequest`). `ack` is the poster's own act; a settlement and a rejection are requests the approver decides;
    - above `Payroll:Approval:DualApprovalThreshold` (50,000,000), two approvers of the run's present version;
    - the posting needs a closed review of the present version, opened by one `auditor` and closed by another, neither of them an approver or the poster;
    - a token with two conflicting payroll roles is refused (403 naming the pair);
    - `Payroll:Blockers:Mode=HoldEmployee` holds each blocked employee's order;
    - **the people a posting needs:** a processor, one or two approvers, two auditors and a poster.
- **The code today** (read on `708d2004`; the plan was written on 2026-10-30, before PLAN-53..58, so find code by its content where a line moved):
  - **AC-12** (`src/Tests/HRManagement.AcceptanceTests/Ac12PayrollSodTests.cs`, 106 lines):
    - its month is fixed (2027-06, `:26`);
    - it creates a run and submits it for review without calculating or validating it, so `PayrollRun.SubmitForReview` answers 400 («يجب إكمال الحساب والتحقق من الإجماليات قبل الإرسال للمراجعة») and `AcceptanceGate.RequireData` stops it `Blocked`;
    - it expects 400 with «ترحيل» from the approver's posting, where the policy answers 403: the approver holds no poster role;
    - it has no `[RequiredAcceptance]`. PLAN-58 added the second approval and the review through `AcceptancePayrollReview`.
  - **The handler's separation of duties** (`Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs`):
    - `ApprovePayrollRunHandler` (about `:144`): the creator → `PayrollApprovalMessages.CreatorCannotApprove` («لا يمكن لمنشئ التشغيل اعتماد نفس التشغيل.»); a reviewer of the present version → `ReviewerCannotApprove`; the required count from `SeparationOfDutiesService.GetRequiredApprovalCount`;
    - `PostPayrollRunHandler` (about `:194`): first «the poster is none of the approvers» (`PayrollPostingMessages.PosterIsApprover`, «لا يمكن لنفس المستخدم اعتماد التشغيل ثم ترحيله.»), then `PayrollSodGuard.ValidatePayrollRunAction`, then the review: `PosterIsReviewer`, `ReviewerIsApprover` and **`ReviewRequired`** («يجب أن يفتح مدقق سجل مراجعة للتشغيل ويغلقه مدقق آخر قبل الترحيل.»);
    - since PLAN-58 no persona reaches these from the API: the creator holds no approval role, the approver no poster role, and a holder of both is refused by the conflict requirement.
  - **The service-regime gate:**
    - `PayrollRegimeGovernance` (`Payroll.Application/Services/`) has `ClientMissing` («تعذر التحقق من أنظمة الخدمة — عميل HR.Core غير مهيأ.»), `DecideAsync` and `HoldsFor`. **`BlockRunRefusal(int count)` names a count only;**
    - `StartPayrollRunJobHandler.DecideRegimeAsync` (`PayrollRunJobHandlers.cs`, about `:48-62`) reads `ServiceRegimes:BlocksFinalPayroll` (default `true`) and `Payroll:Blockers:Mode` (default `HoldEmployee`). It runs before `BeginCalculating` (about `:176-193`): a refusal leaves the run as it was;
    - `FakeHrCoreServiceRegimeClient` (`HRManagement.TestSupport/Integration/`) blocks the ids in `BlockedEmployeeIds`. `PayrollIntegrationTestSetup.AddPayrollConsumerServices` registers it with `TryAddScoped`, beside an empty configuration;
    - `Pay058HeldEmployeeTests` proves `HoldEmployee` (the blocked employee held by name, the others paid) and `BlockRun` (refused before the run moves; it asserts `BlockRunRefusal(1)`). `Syr007RegimeGovernanceTests` holds both modes in unit tests;
    - display names: `IEmployeeLookup.GetEmployeeDisplayNamesAsync` (`Payroll.Application/Abstractions/IPayrollEventPublisher.cs`).
  - **The fourteen integration tests of G-PAY-05** (`docs/PAY_001_GAP_STUDY.md`, about `:196-199`):
    - on SQLite (10): `PayrollRunLifecycleIntegrationTests.PayrollRun_Draft_Calculate_Post_Lifecycle`, `Perf001PayrollBatchTests` ×4, `Pay003ResumablePayrollRunTests` ×4, and `EndToEndReleaseWorkflowSmokeIntegrationTests`;
    - on PostgreSQL with `RUN_TESTCONTAINERS=true` (4): `Pay021`, `Pay022.Pause_Resume…`, `Pay023` and `Perf001…PostgreSql`;
    - PLAN-34 fixed their hosts; PLAN-58's report has the payroll integration tests on SQLite and the full Testcontainers step green. 0.3 measures them again.
  - **The scripts:**
    - `scripts/acceptance-payroll-determinism.ps1` (199 lines): `Invoke-PayrollSql` (`:69`), `Cancel-PayrollRunInDb` (`:77-90`: four `UPDATE`s that free deductions, absences and adjustments and set the status to 8), `Reset-ReservedPeriod` (`:92-119`), and its month fixed at 2027-01 (`:122-123`);
    - `scripts/acceptance-payroll-resume-chaos.ps1` (299 lines): `Invoke-PsqlJson` (`:60`, reads), `Invoke-PayrollSql` (`:66`), `Reset-ReservedPeriod` (`:72-92`), `Cancel-TestRun` (`:95-104`), and its month fixed at 2027-03 (`:140-141`);
    - `scripts/acceptance-payroll-audit-fixture.ps1` was deleted by PLAN-58.
  - **The cancellation and the rejection** (`PayrollRunLifecycleApiController`): `POST api/payroll/runs/{id}/cancel` (`Payroll.RunCancel` = a processor or an approver; a reason) frees the period and the inputs; `POST runs/{id}/reject` (`Payroll.Approve`; a note); both take the antiforgery token of `GET api/payroll/run-lifecycle/antiforgery`.
  - **AC-21..23:**
    - AC-21 and AC-22 call the two scripts above;
    - AC-23 (`[RequiredAcceptance]`, 200 lines) takes the months 13 and 14 ahead of the current one. On the prod-like stack that is AC-PAY-56's 2028-01 today. It cancels its salary change request and leaves its two runs.
  - **The salary change** (`docs/PAYROLL_SALARY_CHANGE.md` §5; D-53-1):
    - `PayProfileChangeRules.CheckRequestedDate` refuses a date that is not the first day of a month, or that **is before the current month** (`PayProfileChangeMessages.BeforeCurrentMonth`, «تاريخ السريان لا يسبق الشهر الجاري»), in Damascus dates through `IPayrollCalendar`;
    - the approval gives `Applied` when the date is today or earlier, and `Scheduled` otherwise. `FindBlockingRunAsync` refuses a month whose run covering the employee is frozen, approved or posted, or is a regular run being calculated or validated (409);
    - a `PendingApproval` or `Scheduled` request is cancelled by its requester or by a holder of `Payroll.Approve`, with a reason;
    - **AC-IAM-53a** proves that a run of the current month takes an applied change (its base component the new salary for the whole month), and it leaves that run frozen.
  - **G-PAY-11 (wave 2, PAY-E), which the cycle meets:** a regular run takes the **active** pay profiles only (`StartPayrollRunJobHandler`: `EmployeePayProfiles.Where(p => p.IsActive)`), and `BaseSalaryRule` pays the whole salary when the employee's working days in the period are zero (`PayrollRules.cs:15-21`; `EmployeePayProfileRules.CountEmployeeWorkingDays` gives zero for a version that starts after the period). So a run of a past month, calculated after a later change was applied, pays the new salary in full.
  - **The attendance gate** (`PayrollAttendanceGate`):
    - its scope is every active pay profile; «missing» is an employee with no receipt overlapping the period; `Warn` by default (D-LVA-10);
    - `GET api/payroll/runs/{id}/attendance-status` answers `employeesInScope` and `missingAttendance` (ids, in order). **For a month nobody closed, that list is the run's whole scope:** that is how the cycle reads its employees without SQL.
  - **HR.Core's attendance** (`AttendancePeriodsController`, `OrgBranchControllers`):
    - `POST api/attendance/periods/{yyyy-MM}/close` (`HrCore.AttendanceClose`: `hr-core` or `admin`; antiforgery from `GET api/attendance/periods/antiforgery`; no unit = the whole agency, for a central user). It answers 200 `{ periodId, status, closedEmployees, skippedAlreadyClosed }`, 400 (unreadable or not ended), 403, 404, or 409 with the employees who lack a working day;
    - `GET api/attendance/periods/{yyyy-MM}`: `status`, `closedEmployees`, `remainingEmployees`, and each summary with its `absentDays`;
    - the bulk entry is a screen, `BulkEntry(orgUnitId, month, defaultStatus)` (`HrCore.AttendanceWrite`): the unit's tree, working days only, nothing after today, never «في إجازة», skipping days that have a record. One day is the attendance create screen;
    - payroll's side: `GET api/payroll/attendance/receipts?periodStart=yyyy-MM-dd`.
  - **The prod-like stack's data:**
    - the seed's hire dates are all on or before 2025-08-01 (`AcceptanceSeedCatalog.cs` about `:359`: `ReferenceDate` 2026-08-01 less at least a year). Only `Active`, `Probation` and `OnLeave` employees get a pay profile (`IsPayable`). HR.Core's acceptance seed writes no attendance;
    - the payroll seed posts a regular run of every month from 2025-08 to 2026-07 (`DemoPayrollSeeder.SeedPayrollRunsAsync`). `AcceptancePayrollSeeder.SeedScenarioRunsAsync` holds one run per status from 2026-08 to 2027-02, all `Correction` or `FinalSettlement`, so outside the live-period index (`IX_PayrollRuns_LivePeriod`: regular runs neither reversed nor cancelled);
    - a second live regular run of a period → 400 «يوجد تشغيل رواتب حي لنفس الفترة والنوع مسبقاً.»;
    - the seed's law-50 version 1 is published from 2000-01-01 (`PayrollRegimeSeed`);
    - no acceptance seed gives an employee a bank account;
    - **so on a fresh prod-like stack in November 2026, D-PAY-22's months are 2026-08, 2026-09 and 2026-10;**
    - **the months in use** in agency A: AC-LVA-57 2025-01..07 (`testdata/acceptance/lva057-periods.json`); AC-IAM-53a the current month (it leaves a frozen regular run there); AC-PAY-56 2028-01..06; AC-PAY-55 2029-05..06; AC-PAY-58 2030; AC-PAY-54 2031 on; on the lab stack, AC-24 2027-04 and AC-25 2027-08;
    - agency A's runs have about 1,100 lines and a total of about 276,000,000: two approvers.
  - **The API the cycle calls** (Payroll's, unless marked):
    - **runs:** `POST api/payroll/runs` (201 `{ id }`); `POST runs/{id}/calculate`, `validate`, `submit-for-review`, `approve`, `post` (**the posting answers 200 with no body**); `GET runs/{id}` (`PayrollRunReadApiController`: the run and its lines with gross, deductions, net and adjustments — **no revision, no approvals, no batch**); `GET runs/{id}/provenance`, `attendance-status`, `bank-readiness`, `freeze-status`; `GET payslips/{salaryLineId}`;
    - **the review:** `POST runs/{id}/audit-records` and `POST audit-records/{id}/close`. `AcceptancePayrollReview.ReviewAsync` has `auditor` open and `a-auditor-2` close; `CompleteApprovalAsync` adds `a-payroll-approver-2` when two approvals are required;
    - **batches:** `GET api/payroll/payment-batches/{id}` (its status, and **its orders counted by status**), `…/held`, `…/files`, `…/files/{fileId}`; `POST …/process`, `…/submit-held`, `…/ack`, `…/settlement-requests`; `GET dual-control/{id}` and `POST dual-control/{id}/decide`;
    - **frameworks:** `GET api/payroll/frameworks?code=`, `POST …/import`, `…/{id}/approve`, `legal-attest`, `publish`, `activate` (AC-PAY-54a's flow);
    - **accounts:** `GET` and `POST api/payroll/employees/{employeeId}/bank-accounts`; `…/{id}/verify` and `…/{id}/retire` (`Payroll.Approve`); antiforgery from `GET /api/payroll/bank-accounts/antiforgery`;
    - **salary changes:** `api/payroll/pay-profile-changes` (submit, `{id}`, `approve`, `reject`, `cancel`);
    - **adjustments:** screens only — `PayrollAdjustmentsController.Create` (`Payroll.Process`) enters one pending, and another person decides it (PLAN-54 §4);
    - **missing for an SQL-free cycle:** the posting's batch, the batch's orders by employee, and the run's revision and approvals (P-9).
  - **Acceptance helpers:**
    - `AcceptancePayrollAccounts` (in `AcceptancePayrollReview.cs`): `Iban(employee, mark)` (a test IBAN with computed check digits), `GiveVerifiedAccountAsync` (the processor registers, the approver verifies), and `PickWithoutAccountAsync` (psql, employees with **no account at all**: AC-PAY-55 and AC-PAY-58 take theirs this way);
    - `AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync` (psql), `AcceptanceGate.RequireData`;
    - `EvidenceRecorder`, with the eight elements: `metadata.json`, `steps.md`, `http/`, `before.json`, `after.json`, `audit.json`, `db-checks.json` and `timing.json`, then `result.json`.
  - **Personas** (`testdata/acceptance/personas.json`, 70), agency A: `payroll-processor` (`e2e-payroll-processor`), `payroll-approver` (`e2e-payroll-approver`), `a-payroll-approver-2`, `a-payroll-poster`, `a-payroll-reverser`, `auditor` (`acc-auditor`), `a-auditor-2`, `a-payroll-multirole` (the one `sodConflictFixture`), `central-a` (`hr-core`) and `br1-mgr`.
  - **The documents:**
    - `docs/PILOT_DEPLOYMENT.md` «مؤشرات النجاح» is about `:31-34` (the plan's `:20-21` moved);
    - `docs/PAYROLL_RUN_RESUME.md` already documents PLAN-56's cancel (about `:35`);
    - `docs/runbooks/` holds `attendance-period-close.md` and four others, none for the payroll cycle;
    - G-PAY-05 in `docs/PAY_001_GAP_STUDY.md` (about `:178`) has no «الحالة» line yet;
    - `docs/CI_AND_TESTING.md` (about `:38`) names AC-12.
  - **STR-002:** the interim legal pack's exceptions expire on 2027-01-31, and the dashboard warns from 2026-12-02. A version that cites them stops calculating after that date (`PayrollInterimGate`) until the owner renews them.
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43). This plan expects no migration. If one appears, lift `FORCE` inside it as `PAY055` does, and prove it as the migrator.
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role.
  - **A new PostgreSQL test uses the connection strings of `PostgreSqlFixture`,** whose idle connections close after seconds. Make sure CI's Testcontainers filter picks up your new classes.
  - **The prod-like stack runs `ASPNETCORE_ENVIRONMENT=Acceptance`,** so it does not load `appsettings.Production.json`. A value in a Production file must be mirrored in `docker-compose.acceptance-prodlike.yml`.
  - **Personas:**
    - a persona change needs `scripts/acceptance-reset.ps1` before the first start (the personas' hash is in the seed pack's fingerprint);
    - **never write `personas.json` while an image is building** (PLAN-53 built one image again for it);
    - a new persona also changes `DATA-AUTH` and the tests that read `personas.json`; a user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh`.
  - **The classes of an acceptance group run one at a time in an order that changes from run to run.**
    - Before any payroll calculation on a shared prod-like stack, call `AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync`;
    - a class never relies on what another class did or did not do, and leaves nothing that changes another class's work;
    - **random or fixed test data can meet another class's months** (PLAN-58: AC-IAM-52a's random day fell in a month AC-LVA-57 fills): take payroll months from the registry (P-8).
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - **Formatting:** before every commit, run `dotnet format whitespace HRManagement.slnx --verify-no-changes --no-restore --include <the files you changed>`. CI runs the full check.
  - **Acceptance tests:** a test posts the URL and the token the page renders (PLAN-44); an API write needs the antiforgery token (PLAN-47, PLAN-53).
  - **CI builds Release, where a nullable warning is an error** (PLAN-58's `bc4703ce`, CS8629): run `dotnet build HRManagement.slnx -c Release -warnaserror` before the push.
  - **The stage jobs' limits follow QA-066's rule** (PLAN-58's first PR run hit the prod-like stage's 35 minutes): a class added to a group adds its time; measure and set the limit.
  - **CodeQL runs in CI only:**
    - a new API `POST` or `PUT` without `[ValidateAntiForgeryToken]` fails `cs/web/missing-token-validation` (PLAN-53). So does an existing one that starts to write something new (PLAN-56): give it the token check, and update its clients;
    - a value whose name reads as sensitive (account, password, secret, token, IBAN) that flows into a log, an audit text or a stored field fails `cs/cleartext-storage-of-sensitive-information`, even when it is an opaque key (PLAN-55). Name an identifier by what it is. **The new orders read carries no account number, and no field named like one;**
    - a log line that writes a value built from the request is `cs/log-forging`;
    - never answer CodeQL with a baseline entry or an ignore attribute.
  - **Gitleaks reads every commit of the branch:** a literal that looks like a key fails the check even if a later commit removes it (PLAN-56). Never write such a literal (the test IBANs are computed, never written out); never rewrite history after a push.
  - **The outbox guard matches a marked method by its name** (PLAN-57's `CloseAsync`): two methods with one name confuse it.
  - **The machine** (P-2): the disk (PLAN-53 filled it once); its memory under a stack start (PLAN-55); stale compiler servers; never delete build outputs while a step uses them; helpers outside the tree; never change `~/.ssh/config` in the middle of a run; after the host sleeps, the Docker VM's clock may lag (PLAN-50).
  - **The session may stand still.** Keep the progress file's next step exact, and go from the pre-push check straight to the push and the PR, in the same turn.
  - **The server:**
    - **the GHCR path** (PLAN-58): images are built in GitHub Actions from a pushed branch, so the push comes before the server phase; nothing is built on the server; a rollback pulls an earlier tag from GHCR;
    - **keep ssh connections few:** never poll in a tight ssh loop (PLAN-56's 8-minute refusal);
    - run the server suites with nothing heavy beside them (PLAN-45);
    - **a deploy may stop at `keycloak-reconcile` after PostgreSQL is recreated** (PLAN-40, PLAN-54, PLAN-56), leaving the application containers created and not started: run the same deploy again at once;
    - the server suites write into the committed `acceptance-evidence/` and `reports/`: restore those files, or move them under `artifacts/plan59/vps/` (PLAN-48);
    - a server check cleans up what it leaves open, and decodes a page before it compares Arabic text (PLAN-49);
    - **Production and the demo keep the password grant closed:** a script that signs in there uses a signed-in session's cookie or a bearer token (PLAN-55);
    - **when a plan changes who may act, the older server checks written for the old actor are adapted, their meaning kept** (PLAN-58's `64dfffff` and `4a6f21ae` for `hradmin`);
    - in a psql concatenation, `||` with a `NULL` gives `NULL`: use `coalesce` (PLAN-58's PAY-54 check);
    - older server checks that fail or flicker from this machine only (PLAN-55..58). Record them, rerun them alone, and do not chase them:
      - `AC_POL_47_vps_the_migration…` (it reads a file lost with the formatted Windows machine);
      - `AC_WF_46` (it reads a 5,000-line tail of a log that grows fast under the checks): it passes alone just after a sweep cycle;
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
  - In this prompt's tables, `\|` stands for `|`.

## Read first (mandatory)

1. **The specification:**
   - `docs/acceptance-plans/PLAN-59-pay-full-cycle-production-proof.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
   - `docs/PAY_001_GAP_STUDY.md`: G-PAY-05 and the PAY-D row; G-PAY-11 (why P-4 avoids a later change in a past month).
2. **These reports:**
   - `reports/plan58-final-report.md` (§3, §13, §15, §17) and its merge note;
   - `reports/plan57-final-report.md` (§7, §14, §16);
   - `reports/plan56-final-report.md` (§15 and its note for PLAN-59);
   - `reports/plan53-final-report.md` (§3: the month rule).
3. **These documents:**
   - `docs/PAYROLL_SALARY_CHANGE.md` (§5, the dates; its state table), `docs/HCM_002_ATTENDANCE.md` and `docs/runbooks/attendance-period-close.md` (the runbook's model), `docs/PAYROLL_RUN_RESUME.md`, `docs/PILOT_DEPLOYMENT.md`;
   - the rows D-PAY-3..5, D-PAY-20, D-PAY-21, D-PAY-22, D-53-1 and D-LVA-10 of `docs/OWNER_DECISIONS_REGISTER.md`.
4. The code named in «The code today».
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md` (the GHCR path).

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **No other plan session is running,** and you are in the main folder (see «How to run»), on `main` or on `docs/plan-58-merge-note`.
2. **`git fetch origin`**, and `origin/main` holds PLAN-58: the merge of `release/plan-58` is in `git log origin/main --merges --oneline -5`.
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

- **D-PAY-21, «a complete cycle» in wave 1:** posted; its batch sent by a bank file whose control totals match; the bank's confirmation recorded by two people with the statement's reference (PLAN-58 item 7); its payslips equal to its orders. Importing the channel's notice and the bank's statement is wave 2 (PAY-I). «Settled» is recorded only with a bank reference and two people.
- **D-PAY-22, the cycle's month in acceptance:** the first ended calendar month with no live regular run in agency A, from `ACCEPTANCE_PAYROLL_CYCLE_FROM` (default: 12 months before the execution). It skips the months of `testdata/acceptance/lva057-periods.json` and any month with a closed attendance summary in agency A. When the months run out, a clear message asks for `scripts/acceptance-reset.ps1`. Three executions in a row give three consecutive months. P-10 makes it an API-only picker.
- **D-PAY-3, D-PAY-4, D-PAY-5:** as in PLAN-58 — the review by `auditor`, opened by one and closed by another; holds per employee (`HoldEmployee`); two different approvers above 50,000,000.
- **D-LVA-10, the attendance gate:** `Warn` in every environment; the owner raises it to `Block`. P-5 records what PLAN-57 and PLAN-58 asked this plan to decide.
- **D-53-1, the salary change's date:** the first day of a month, not before the current month (refused at the request), and not in a month whose run covering the employee is frozen, approved or posted. It binds step 3 (P-4).
- **D-PAY-20, a token with two conflicting roles:** refused in every financial act. AC-12 proves it with `a-payroll-multirole`.
- **D-QA-20:** the evidence of record is the PR run's artifact.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-59-pay-full-cycle-production-proof` from `origin/main` (plan 0.1), in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-58's merge note from `docs/plan-58-merge-note`. If that branch is gone, rebuild the record from GitHub (PR #59, its runs, the merge commit `708d2004`), and say so;<br>&nbsp;&nbsp;– commit this prompt file. If the owner pasted it into the session rather than saving it, write it from the session's text first, as PLAN-54..58 did;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **The machine (Linux): awake, the disk, the memory, Docker, the ports.**<br>• **Keep the machine awake for the session, changing no setting:** `systemd-inhibit --what=sleep:idle --who=plan59 --why="PLAN-59" sleep infinity`, detached. Record its PID in `artifacts/plan59/keepawake.pid`, and stop it **by that PID** at the end. If the machine restarts, start it again and record it.<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• **Remove only this repository's leftovers:** compose projects `hrm-*`, Testcontainers, this plan's images and this repository's build cache. Never prune, stop or remove another project's images, containers or volumes, or the owner's dev containers.<br>• **The disk:** read `df -h /` and `docker system df` before every image build and every stack start.<br>&nbsp;&nbsp;– Build an image only with at least 12 GB free. Below that, prune this repository's build cache and remove this plan's unused images first;<br>&nbsp;&nbsp;– a watch that stops a build near 2 GB free stops it **by its PID**;<br>&nbsp;&nbsp;– if 12 GB cannot be reached without touching other projects: `BLOCKED:`, one line — the owner frees space.<br>• **The memory, before a stack start:**<br>&nbsp;&nbsp;– nothing heavy beside it: no build, no test host, no browser, no idle stack of this repository;<br>&nbsp;&nbsp;– read `uptime` and `free -m` first, and start only when the load is below the number of CPUs;<br>&nbsp;&nbsp;– a start that fails from the load (a health check timing out, a 401 during the identity sync) is retried after the load falls, three times at most, each failure and its cause recorded;<br>&nbsp;&nbsp;– wait for each start in the turn, in short polls («Long commands»).<br>• **The images:** one service at a time from the local base images (`acceptance-up.ps1 -UseLocalBaseImages`, and the helpers PLAN-53..58 kept outside the tree). This plan changes Payroll (the regime refusal, the reads) and the tests: build every service image once from this tree, then rebuild only the images a fix reaches. The migrator only if a migration is added (none is expected). Never commit a helper.<br>• **One heavy job at a time:** never an image build beside a host test run or a browser check. Close what you opened before a full unit run (a browser, an idle stack).<br>• **After an image build,** check that every service image was rebuilt from this tree. A failed build leaves stale images, and a `-NoBuild` run on them proves nothing.<br>• **Before each stack start,** check the stack's host ports with `ss -ltn` and `docker ps`. A port held by something that is not this repository's: `BLOCKED:` with the port.<br>• **The local CI steps:** `scripts/ci/run-steps.sh --all` natively in a fresh clone of HEAD, with the compiler servers off (see «Where things stand»), naming each step not run and why.<br>• **If this session runs on Windows again,** use PLAN-67's forms of these checks, and record it. |
| P-3 | **The baseline (plan 0.2–0.4),** into `reports/plan59-baseline.md`.<br>• **0.2:** `dotnet build HRManagement.slnx -warnaserror`, and every endpoint and screen the cycle calls, grouped by the plan that made it, each plan with its merge commit (`git log --merges --oneline origin/main`):<br>&nbsp;&nbsp;– PLAN-53: the salary change's request, decision and cancellation;<br>&nbsp;&nbsp;– PLAN-54: the framework's read, import, approval, attestation and publication; the adjustment's entry and decision;<br>&nbsp;&nbsp;– PLAN-55: the account's registration, verification and retirement;<br>&nbsp;&nbsp;– PLAN-56: the cancellation and the rejection;<br>&nbsp;&nbsp;– PLAN-57: the bulk entry, a day's entry, the close, the period's read, the receipts and `attendance-status`;<br>&nbsp;&nbsp;– PLAN-58: the review records, the batch, its files, its acts and the dual-control decision.<br>• **0.3:** the plan's filter twice, without and with `RUN_TESTCONTAINERS=true`: `dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Payroll\|FullyQualifiedName~Pay0\|FullyQualifiedName~Perf001\|FullyQualifiedName~EndToEndReleaseWorkflow"`. Record the fourteen by name with their result, and whatever is red with its cause.<br>• **0.4:** the plan's inventory with `pwsh`: `Select-String -Path scripts/acceptance-payroll-*.ps1 -Pattern 'UPDATE\|INSERT\|DELETE\|Invoke-PayrollSql'`.<br>• **From this prompt,** generated by search into `artifacts/plan59/inventory.*`, never typed, and summarised:<br>&nbsp;&nbsp;– **the reads:** each check of AC-PAY-59 (P-10) with the API read that answers it. A check with no read goes to P-9;<br>&nbsp;&nbsp;– **the months:** every acceptance class that creates a payroll run or a salary change in agency A, its stack and its months (P-8's registry);<br>&nbsp;&nbsp;– **the pickers:** every class or helper that picks employees by «no bank account», «no request» or id order (P-11);<br>&nbsp;&nbsp;– every test that asserts `BlockRunRefusal` or its text (P-6);<br>&nbsp;&nbsp;– the demo's picture (P-15). |
| P-4 | **Step 3 under D-53-1, and G-PAY-11** (plan §4 step 3).<br>• **The conflict:** D-53-1 (the owner, 2026-10-31) refuses, at the request, a date before the current month (`CheckRequestedDate`). D-PAY-22's month has ended, since the bulk entry writes no future day and a close refuses a month not ended. No month satisfies both in one execution: a change dated the first day of a past month is a retroactive change (PAY-G, wave 2).<br>• **And a change applied before the month's calculation would be paid in full in that past month** (G-PAY-11, PAY-E: the run takes the active version, and zero working days pay the whole salary). The cycle would then prove an overpayment.<br>• **So step 3 is,** in its place before the calculation:<br>&nbsp;&nbsp;– `payroll-processor` asks for a raise of Z dated **the first day of the month after the current one** (Damascus), inside the grade's bounds, with a reason and a legal reference;<br>&nbsp;&nbsp;– his own approval is refused; `payroll-approver` approves it → `Scheduled` (the date is after today);<br>&nbsp;&nbsp;– assert: requester ≠ decider, `Scheduled` with its date, Z's profile in force unchanged;<br>&nbsp;&nbsp;– steps 5 and 14 then assert **Z's line in M at the salary of his version in force in M** (his payslip's base component): a change dated after M reaches no part of M;<br>&nbsp;&nbsp;– in `finally`, whatever the result, the requester cancels it with a reason → `Cancelled`, so nothing applies later.<br>• **The proof that a run takes an applied change in its own month is AC-IAM-53a's** (the current month, the whole month at the new salary). The report says so, and says that the plan's «Z's line at the new salary» in a past month needs a retroactive change (PAY-G).<br>• **Do not relax D-53-1, and do not fix G-PAY-11 here:** G-PAY-11 is in the plan's «Out of scope» (PAY-E). The runbook tells operators what follows: run each month before a later salary change takes effect (P-14). |
| P-5 | **The run's scope and the attendance gate** (PLAN-57 §14 items 1 and 3, PLAN-58's handover).<br>• **Decided, not changed:** the run's scope stays «every active pay profile» (who the run gives a line to), and the gate's scope stays that same set. Excluding a profile outside the period is G-PAY-11's (PAY-E), out of this plan.<br>• **The gate stays `Warn` in every environment** (D-LVA-10: the owner raises it). Under `Block`, a month in which someone with an active profile is out of HR.Core's service (hired after it, for one) is refused whole.<br>• **What the cycle relies on:** on the cycle's own stack every payable employee was hired before every month from 2025-08, so a whole-agency close leaves nobody missing. The cycle asserts `missingAttendance = []` itself (the plan's rule 2).<br>• **The prod-like seed's profile dates** (PLAN-57 item 3) are not changed: changing them changes every seeded payroll figure, and the cycle's months come after every seeded hire. Record it for PLAN-65.<br>• **The runbook** says when `Block` is safe for an agency (nobody with an active profile out of service in the month), how to see it before raising it (the run's `attendance-status` before the close against the close's employees), and the setting per environment (`Payroll__Attendance__Gate=Block`).<br>• **PLAN-56's question** (should the regime gate refuse before the run moves?) was answered by PLAN-58: before `BeginCalculating`. Record it. |
| P-6 | **The fourteen, and the two new classes** (plan §1).<br>• 0.3's list goes green. A red in Payroll is fixed here; a red in another service is recorded in the baseline with its owner plan, as the plan says.<br>• **`BlockRun`'s refusal names the blocked employees:**<br>&nbsp;&nbsp;– `BlockRunRefusal` takes the blocked employees and gives the first ten by display name (`IEmployeeLookup` when registered, else the employee's number or id), then «و{n} آخرون», with HR.Core's reason codes. No national number;<br>&nbsp;&nbsp;– it still refuses before the run moves;<br>&nbsp;&nbsp;– `Pay058HeldEmployeeTests` and `Syr007RegimeGovernanceTests` follow the new text, their meaning kept.<br>• **`Pay059RegimeBlockerTests`** (`src/Tests/HRManagement.IntegrationTests/`, PLAN-34's host as it is):<br>&nbsp;&nbsp;– `Payroll:Blockers:Mode=BlockRun` with the fake blocking one employee → refused, the message names him, the run as it was (no job, no pinned framework);<br>&nbsp;&nbsp;– a host with no `IHrCoreServiceRegimeClient` and `ServiceRegimes:BlocksFinalPayroll` unset (default `true`) → refused with `ClientMissing`, the run as it was; with `false` → calculated;<br>&nbsp;&nbsp;– `HoldEmployee` (the default) → not refused: proved by `Pay058HeldEmployeeTests`. Name that test in the class's summary; do not duplicate it.<br>• **`Pay059PayrollSodHandlerTests`,** through the handlers (the rules no persona reaches since PLAN-58):<br>&nbsp;&nbsp;– the creator approves → `CreatorCannotApprove`, nothing written;<br>&nbsp;&nbsp;– an approver posts a run that has no review → `PosterIsApprover` and not `ReviewRequired` (the separation of duties comes first), no batch, the run still `Approved`;<br>&nbsp;&nbsp;– an approver posts after a closed review → `PosterIsApprover` again.<br>• The four PostgreSQL tests run with `RUN_TESTCONTAINERS=true` and count as green. Check that CI's Testcontainers filter picks them up. |
| P-7 | **AC-12** (plan §2), on PLAN-58's rules.<br>• `Ac12PayrollSodTests`: `[RequiredAcceptance]`, `EvidenceRecorder("AC-12")`, on the prod-like stack's main group (P-11). Its month is D-PAY-22's, through P-10's picker.<br>• `ClassifyRecordsAddedSinceTheSeedAsync` before its calculation: it shares the main stack with classes that add employment records.<br>• **The steps** (the plan's table, as PLAN-58 left the product):<br>&nbsp;&nbsp;– `payroll-processor` creates, calculates and validates → 200, `ResultsFrozen=true`;<br>&nbsp;&nbsp;– `payroll-processor` approves → 403;<br>&nbsp;&nbsp;– `a-payroll-multirole` approves → 403, the conflict's pair named;<br>&nbsp;&nbsp;– `payroll-processor` submits; `payroll-approver` approves, and `a-payroll-approver-2` when the approval's answer says two are required → `Approved`;<br>&nbsp;&nbsp;– `payroll-approver` posts → 403 (no poster role);<br>&nbsp;&nbsp;– `a-payroll-poster` posts before the review → 400 with `PayrollPostingMessages.ReviewRequired`'s text;<br>&nbsp;&nbsp;– `auditor` opens the review and `a-auditor-2` closes it → 200;<br>&nbsp;&nbsp;– `a-payroll-poster` posts → 200, `Posted`.<br>• **In `finally`:** a run not posted is cancelled through `POST …/cancel` with a reason, so its month stays free.<br>• The handler's own messages are `Pay059PayrollSodHandlerTests`' (P-6). |
| P-8 | **The scripts without SQL, AC-21..23, and the months** (plan §3).<br>• **The scripts:** `Cancel-PayrollRunInDb`, `Cancel-TestRun` and `Reset-ReservedPeriod` become `POST api/payroll/runs/{id}/cancel` with a reason, as the persona the class passes, with the antiforgery header and its cookie from `GET api/payroll/run-lifecycle/antiforgery`. `Invoke-PayrollSql` is deleted. Reads may stay (`Invoke-PsqlJson`). The plan's `Select-String` finds nothing.<br>• Each script takes its month from its class, as a parameter; none fixes one.<br>• **The months' registry, `testdata/acceptance/payroll-months.json`** (PLAN-57's file is its model). It lists every class's payroll months in agency A, by stack: the seed's, AC-LVA-57's (whose own file stays, named there), AC-IAM-53a's current month, AC-PAY-54..58's, AC-12's (the main stack) and AC-PAY-59's (the cycle's own stack), both by D-PAY-22, the lab's AC-24 and AC-25, and these:<br>&nbsp;&nbsp;– **AC-21:** a month of 2024-01..2024-06. **AC-22:** a month of 2024-07..2024-12. Both are in the past, governed by the seed's version from 2000, with no run and no attendance;<br>&nbsp;&nbsp;– **AC-23:** two consecutive months from the current month + 2 on (its salary change cannot name a month before the current one), skipping every month the registry gives another class and every month with a live regular run.<br>&nbsp;&nbsp;A unit test proves that the registry's absolute months overlap nowhere on one stack, and that PLAN-57's file is inside AC-LVA-57's entry.<br>• **One picker** (`AcceptancePayrollMonths` in the acceptance project): the first month of the class's window with no live regular run of agency A. It is found by creating the run (201) or meeting the live-period refusal (400 with its text) — no SQL. A class that finds none stops with `AcceptanceGate.RequireData`, asking for `scripts/acceptance-reset.ps1`.<br>• **Each class cancels, through the API, in `finally`, what it does not keep:** AC-21 both runs, AC-22 its run, AC-23 its two runs (after its assertions) and its request.<br>• `[RequiredAcceptance]` on AC-21 and AC-22 (AC-23 has it). `ClassifyRecordsAddedSinceTheSeedAsync` in each class before its first calculation.<br>• **The guard over every acceptance script is PLAN-65's** (`AcceptanceScriptSqlWriteTests`). This plan removes payroll's SQL only, so that guard passes with no exception for it. |
| P-9 | **The reads an SQL-free cycle needs** (plan §4: «every check is an API read»).<br>• **`GET api/payroll/runs/{id}`** gains `revision`, `approvalsGiven` and `approvalsRequired` (of the present version; the count from `SeparationOfDutiesService` with the agency's threshold, as the approval counts), and `paymentBatchId` (the posting's `Disbursement` batch; `null` before the posting).<br>• **`GET api/payroll/payment-batches/{id}/orders`:** each order's id, employee, status, amount, hold reason, whether an account is pinned, and the sequence of the file that carried it.<br>&nbsp;&nbsp;– **Never an account number,** and no field named like one;<br>&nbsp;&nbsp;– `Module.Payroll` and central-only, as the batch's read; another agency's batch → 404.<br>• **Anything else P-3's read inventory finds missing** gets the smallest read of the same kind. No new write and no migration. If a migration turns out necessary: forced row security, proved as the migrator, and a rollback note.<br>• **Each read:** its stance (`OrgScopeCoverageTests`); its tests (the shape, another agency's 404, no account number in the body); listed for API clients; the auditor's gateway reach unchanged (`GatewayAuditorReachTests`). |
| P-10 | **AC-PAY-59** (plan §4).<br>• **`AcPay59FullCycleTests`** (`[RequiredAcceptance]`, `EvidenceRecorder("AC-PAY-59")`) and **`PayrollCycleDriver`**, in the acceptance project:<br>&nbsp;&nbsp;– each step is one HTTP call or one screen post (the URL and the token the page renders), captured with `CaptureHttp`, and each check an API read written by `WriteDbChecks`;<br>&nbsp;&nbsp;– **no SQL: no psql helper, no `QueryAcceptancePostgres*`, no `ClassifyRecordsAddedSinceTheSeedAsync`;**<br>&nbsp;&nbsp;– a bulk step (the accounts, the payslips) captures its first and last calls and writes the counts. `GiveVerifiedAccountAsync` captures every call: give it a switch, or loop in the driver.<br>• **The month (D-PAY-22),** from `ACCEPTANCE_PAYROLL_CYCLE_FROM` (`yyyy-MM`; default 12 months before today, Damascus) up to the last ended month:<br>&nbsp;&nbsp;– skip a month of `lva057-periods.json`, or one the registry gives another class;<br>&nbsp;&nbsp;– skip a month that `GET api/attendance/periods/{M}` shows closed for anyone in agency A (`closedEmployees` above 0);<br>&nbsp;&nbsp;– then create the regular run of M: 201 makes it the cycle's run; the live-period refusal moves on to the next month;<br>&nbsp;&nbsp;– no month left → `RequireData`, asking for `scripts/acceptance-reset.ps1`.<br>• **Its employees:** the run's scope from `attendance-status` before the close («The code today»). Y, X and Z are three different employees of it, chosen in a fixed order; Z has no open salary change request.<br>• **The steps** (the plan's table, with P-4's step 3 and PLAN-58's people):<br>&nbsp;&nbsp;1. `payroll-processor` reads the framework in force for `LAW_50_2004` in M (`GET api/payroll/frameworks?code=`). None in force: AC-PAY-54a's import of `law50-interim.json` from the first day of M, approved by `payroll-approver`, attested by `a-payroll-approver-2`, and published.<br>&nbsp;&nbsp;2. Accounts, the processor registering and the approver verifying (`AcceptancePayrollAccounts.Iban` with this class's mark): everyone in scope without an account in force. For X: his account in force, if an earlier execution left one, retired by `payroll-approver`; then a new one registered and not verified.<br>&nbsp;&nbsp;3. P-4.<br>&nbsp;&nbsp;4. `central-a`: Y's first two working days of M «غائب» (the create screen, one day each); then the bulk entry «حاضر» from agency A's root unit; then `POST api/attendance/periods/{M}/close` with no unit → 200. Wait until `GET api/payroll/attendance/receipts?periodStart=` holds as many receipts as the close's `closedEmployees`.<br>&nbsp;&nbsp;5. `payroll-processor` calculates and validates: `missingAttendance = []`; Y's line carries two days' absence deduction; Z's line at his salary in force; `provenance` names step 1's framework; `bank-readiness` lists X alone.<br>&nbsp;&nbsp;6. `payroll-processor` submits, and `payroll-approver` rejects with a note → `Draft`, `revision` 2.<br>&nbsp;&nbsp;7. `payroll-processor` enters +25,000 for Y on the adjustments screen, and `payroll-approver` approves it. Then calculate, validate (it passes with a non-zero adjustment) and submit. Y's net = gross − deductions + 25,000.<br>&nbsp;&nbsp;8. `payroll-approver` approves, and `a-payroll-approver-2` when `approvalsRequired` is 2 → `Approved`.<br>&nbsp;&nbsp;9. `auditor` opens the review, and `a-auditor-2` closes it.<br>&nbsp;&nbsp;10. `a-payroll-poster` posts → `Posted`, `TotalNetPay` as approved, `paymentBatchId` a `Pending` batch, X's order `Held` `NO-ACCOUNT`, and everyone else's `Draft`. The batch's held report names X alone (no `REGIME-BLOCKER`, no fraud hold).<br>&nbsp;&nbsp;11. `a-payroll-poster` processes → the others `Submitted`; one `Initial` file whose `ControlCount` and `ControlTotal` are theirs; X's order not in it. **AC-PAY-59b:** a second `process` makes no second file (409, or the same file), and X's order is still not sent.<br>&nbsp;&nbsp;12. `payroll-approver` verifies X's account → `NO-ACCOUNT` lifted, the account pinned on X's order (the orders read) and line (`bank-readiness` no longer lists him).<br>&nbsp;&nbsp;13. `a-payroll-poster` sends `submit-held` → a `HeldRelease` file with X's order alone.<br>&nbsp;&nbsp;14. `payroll-processor`: each line's payslip net equals its employee's order, and Σ orders = `TotalNetPay`.<br>&nbsp;&nbsp;15. `a-payroll-poster` acks with the channel's reference, then asks for the settlement with the statement's reference and the sum; `payroll-approver` approves → the batch and its orders `Settled` (D-PAY-21).<br>• **The rules:** a failure before step 10 cancels the run in `finally` through `CancelRun`; P-4's change is cancelled in `finally` whatever happens; the gate stays `Warn`, and the test asserts `missingAttendance = []` itself; nothing in the acceptance data changes before the test, except what the test does by HTTP.<br>• **AC-PAY-59a's three executions** run on one stack, one after the other, and take three consecutive months. Each execution's evidence folder holds the eight elements, and is copied to `artifacts/plan59/ac-pay-59-<n>/`.<br>• If the persona catalog requires each persona to list the scenarios that use it (PLAN-58 added AC-PAY-58 so), add AC-PAY-59 and AC-12's new uses. A persona change needs `scripts/acceptance-reset.ps1` (Lessons). |
| P-11 | **Acceptance placement** (PLAN-41's rules, QA-066's limits).<br>• **`AcPay59FullCycleTests` goes into a new group `cycle` of `prodLike[]`, `required: true`, with a stage job of its own, `prodlike-cycle`:**<br>&nbsp;&nbsp;– in `stageJobs[]` and `order[]`, and in the `acceptance-stage` matrix of `ci.yml` with its limit; `prodlike-final` waits for it; whatever test ties the matrix to `stageJobs[]` stays green;<br>&nbsp;&nbsp;– **why:** the cycle gives every employee of agency A a verified account, and `PickWithoutAccountAsync` (AC-PAY-55, AC-PAY-58) needs employees with none. A fresh stack of its own also makes D-PAY-22 and `missingAttendance = []` deterministic.<br>• **`Ac12PayrollSodTests`, `Ac21PayrollDeterminismTests` and `Ac23PayrollFreezeTests` move from `lab[]` main to `prodLike[]` main, `required: true`.**<br>• **`Ac22PayrollResumeAfterKillTests` stays in `excluded[]`** with its reason: it kills Payroll on a shared stack (QA-G's drill). It is proved on the local prod-like stack (P-12), and the report says so.<br>• **The limits by QA-066's rule:** measure the new job and the `prodlike` job on the PR's first run, and set each limit to about 1.5 times the measured duration. The new job's first limit comes from the local measurement.<br>• **`ac-ownership.json`:** `AC-PAY-59` (PLAN-59, `provesClaim: true`, its reason); `AC-12` (PLAN-59, `provesClaim: true`); `AC-21` and `AC-23` (PLAN-59); `AC-22` (QA-G, with «proved on the prod-like stack by PLAN-59, outside CI»).<br>• **Isolation:** AC-12, AC-21 and AC-23 leave nothing in agency A that another class's run or batch would meet — no active hold, open critical alert, unverified or shared account, pending four-eyes or salary change request, or free input with an amount. Each asserts on its own runs by id. AC-12's posted run and its held orders belong to its month only.<br>• **The evidence of record is the PR run's artifact** (D-QA-20). Copy what the report needs into `artifacts/plan59/`, then restore the committed evidence. |
| P-12 | **Local testing (owner's rule of 2026-11-06).** Only the classes this plan touches; **the full suite runs once, in CI.**<br>• **The cycle's stack** (a fresh prod-like stack: `scripts/acceptance-up.ps1 -Profile ProdLike -UseLocalBaseImages -NoBuild`): `AcPay59FullCycleTests` three times in a row on it, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`, the way PLAN-57 and PLAN-58 ran a class on a running stack. Then `down -v`.<br>• **The main stack** (another fresh prod-like stack):<br>&nbsp;&nbsp;– `Ac12PayrollSodTests`, `Ac21PayrollDeterminismTests`, `Ac23PayrollFreezeTests`;<br>&nbsp;&nbsp;– the prod-like classes whose code or helpers this plan touched: `AcPay58DisbursementGovernanceTests`, `AcPay56RunStateMachineTests`, `AcPay55BankAccountTests`, `AcIam53SalaryChangeTests` and `AcLva57AttendancePeriodTests` if a helper they use changed; `AcUx67ReadableInterfaceTests` if a page changed;<br>&nbsp;&nbsp;– `scripts/acceptance-evidence-summarize.ps1`;<br>&nbsp;&nbsp;– **then `Ac22PayrollResumeAfterKillTests` alone, last** (it kills Payroll); then `down -v`.<br>• **The lab stack:** `Ac24PartialDisbursementTests`, `Ac25FraudHoldTests` and any lab class whose helper changed; and the lab main group without the three moved classes, to show it still passes.<br>• **leastpriv:** only if a migration or the realm changed (none expected).<br>• **E2E:** in CI only.<br>Then compare the seeded counts of `scripts/acceptance-verify.ps1` by psql. Known local-only failures (`Ac42i` without `kubectl`) are recorded, not chased. |
| P-13 | **Scripts.**<br>• Every new or changed `.ps1` is ASCII-only, 5.1-compatible in syntax, and runs here with `pwsh -NoProfile -File …`.<br>• **`scripts/pay059-validate.ps1`** has a `-SelfTest` with planted defects, as `pay058-validate.ps1` does. It checks, at least:<br>&nbsp;&nbsp;– the plan's pattern finds nothing in `scripts/acceptance-payroll-*.ps1`, and no payroll script fixes a month;<br>&nbsp;&nbsp;– `AcPay59FullCycleTests` and `PayrollCycleDriver` hold no SQL (`QueryAcceptancePostgres`, `psql`, `Invoke-*Sql`, `ClassifyRecordsAddedSinceTheSeedAsync`);<br>&nbsp;&nbsp;– `AcPay59FullCycleTests` required in `prodLike[]` group `cycle`, its job in `stageJobs[]` and in `ci.yml`, and `prodlike-final` waiting for it;<br>&nbsp;&nbsp;– AC-12, AC-21 and AC-23 required in `prodLike[]`, and AC-22 in `excluded[]`;<br>&nbsp;&nbsp;– `[RequiredAcceptance]` on AC-12, AC-21, AC-22, AC-23 and AC-PAY-59;<br>&nbsp;&nbsp;– the months' registry overlaps nowhere;<br>&nbsp;&nbsp;– the runbook exists and names its fifteen steps;<br>&nbsp;&nbsp;– `BlockRunRefusal` takes the employees.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile <ProdLike or Lab> -UseLocalBaseImages`. |
| P-14 | **The runbook and the documents** (plan §5).<br>• **`docs/runbooks/payroll-monthly-cycle.md`** (new, in Arabic, like the other runbooks):<br>&nbsp;&nbsp;– the fifteen steps, each with its role, its screen and its API;<br>&nbsp;&nbsp;– what the operator does on a rejection, a hold, a service-regime blocker (`HoldEmployee` and `BlockRun`), an employee without an account, and missing attendance;<br>&nbsp;&nbsp;– what this plan found: run each month before a later salary change takes effect (G-PAY-11, PAY-E); a salary change is dated from the current month on (D-53-1); when `Block` is safe (P-5); the people a posting needs; the calculation's time against the gateway's limit (G-PAY-21, PAY-J); STR-002's expiry on 2027-01-31.<br>• **`docs/PILOT_DEPLOYMENT.md`** «مؤشرات النجاح»: each indicator tied to AC-PAY-59, and D-PAY-21's meaning of «مكتملة».<br>• **`docs/PAYROLL_RUN_RESUME.md`:** the acceptance scripts cancel through `CancelRun`; no SQL is left.<br>• **`docs/PAY_001_GAP_STUDY.md`:** G-PAY-05's «الحالة» line, linking the PR run's evidence folders (AC-PAY-59, AC-12, AC-21, AC-23) and naming what wave 2 keeps (PAY-E, PAY-G, PAY-I).<br>• **`docs/CI_AND_TESTING.md`:** the cycle's job and why; the moved classes; AC-22 outside CI; `ACCEPTANCE_PAYROLL_CYCLE_FROM`; the months' registry. **`docs/OPS_VPS_SINGLE_HOST.md`:** PLAN-59's section, the change table, the backup, what the checks leave.<br>• The plan's `Status` and «ملاحظات التنفيذ والانحرافات». |
| P-15 | **The demo server facts this plan must respect.**<br>• **It runs Production in demo mode** (`Deployment__Mode=demo`), with no poster, no second approver and no auditor, and all 425 employees without a classified regime. **The checks add no user,** and nothing on the demo is calculated, posted, funded, processed or settled. Record what the demo would need for a cycle: a poster, a second approver and two auditors (through `scripts/vps/add-demo-user.sh`), and its service regimes classified (a data task).<br>• **P0's read-only picture:**<br>&nbsp;&nbsp;– the running tag and the free disk;<br>&nbsp;&nbsp;– the runs, lines, batches, orders, holds, alerts, arrears rows, review records and salary change requests, by status;<br>&nbsp;&nbsp;– a checksum of the runs, lines, batches and orders, never printing an amount or an account's number.<br>• **What the owner will see change:**<br>&nbsp;&nbsp;– a run's API with its revision, its approvals and its batch;<br>&nbsp;&nbsp;– a batch's orders by employee;<br>&nbsp;&nbsp;– `BlockRun`'s refusal naming the employees (an agency setting; the demo is `HoldEmployee`);<br>&nbsp;&nbsp;– **nothing else moves:** no run, line, batch, order or amount changes. |
| P-16 | **The demo server** (the GHCR path of `docs/OPS_VPS_SINGLE_HOST.md`).<br>• **When:** after the release branch is pushed (P8), with the running tag on `origin/main` (`git branch -r --contains 25be64cf9fa8`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan59-<utc>/` (700/600), verified as before (every `CREATE DATABASE` statement, `SHA256SUMS`).<br>• **Before the deploy, read-only:** P-15's picture.<br>• **The disk:** at least 15 GB free before the pull (PLAN-58's deploy took about 6 GB). Never trim Seq's volume.<br>• **Build** in GitHub Actions: `gh workflow run deploy.yml --ref release/plan-59 -f action=build`, find its run (`gh run list --workflow deploy.yml --limit 1`), and wait in short polls (about 12 minutes; «Waiting»). It gives 17 images tagged with the first 12 characters of the branch's head, Trivy-clean and signed. A job red at Trivy has pushed an unsigned image: fix it and build again.<br>• **Deploy:** `scripts/vps/deploy.ps1 -Tag <sha12>`, and again at once if it stops at `keycloak-reconcile`. No migration is expected.<br>• **The checks' own data:** none. They read.<br>• **Rollback:** `scripts/vps/deploy.ps1 -Rollback` brings back `25be64cf9fa8` from GHCR. This plan adds reads and a message, and no data PLAN-58's tag cannot read: say so in `OPS_VPS_SINGLE_HOST.md` (or what a migration changes, if one was added).<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn:<br>&nbsp;&nbsp;– roll back to the previous tag, or fix forward;<br>&nbsp;&nbsp;– restore the backup only if data was damaged;<br>&nbsp;&nbsp;– prove the demo works, and record the downtime. |
| P-17 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P8, after the local CI steps, the Release build and the pre-push check are green, in the same turn as the pre-push check (Lessons).<br>2. `gh pr create`, then wait («Waiting»), with the server phase (P9) beside it.<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, run the unit suite and the format check, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>`, and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-18 | **No regression of PLAN-25, PLAN-36..58, PLAN-66 and PLAN-67.** These stay green:<br>• **every existing payroll result, unit for unit:** the golden pack tests (`Gov002LegalGoldenPackTests`), `PayrollEnginePipelineTests`, the determinism tests, `PayrollFinancialInvariantsTests`, `Pay029*`, `Pay023FrozenRunIsolationTests`, `Pay004ArrearsTests`, `Pay056*`, `Lva057*`, `Pay058*` (the regime refusal's text excepted, its meaning kept);<br>• `Frd001*`, `Ext003*`, `Pay055*` and the SoD tests;<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests`;<br>• `AcIam50`..`AcIam53`, `AcPay54`..`AcPay56`, `AcPay58`, AC-24, AC-25, `AcLva57`, `AcUx67`, `AcWf43`..`AcWf46` with the orphan class, `AcPol47`, both `AcTen48` classes, both `AcPol49` classes, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, `AcHcm29`, `AcQa41`, and both recruitment journeys;<br>• the architecture tests (`PayrollWriteActionsTests`, `WriteActionsHaveExplicitPolicyTests`, `ModuleOverridesAreDeclaredTests`, `GatewayApproverReachTests`, `GatewayAuditorReachTests`, `OrgScopeCoverageTests`, `InterfaceTextGuardTests`, `GuardSelfTests`, `Pay058SinglePaymentPathArchitectureTests`), `OutboxPublishOrderingArchitectureTests`, `Rel004CompatibilityTests`, `ProductionWiringMatrixTests`, `CiStepsParityTests`;<br>• the validate scripts `pay058`, `lva057`, `pay056`, `pay055`, `pay054`, `iam053`, `iam052`, `iam051`, `iam050` and `ux067`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. `pol049-validate.ps1` is red on `main` already: record its state before and after. |

## Scope boundaries

**Out of scope** (from the plan):
- **Wave 2 (PAY-I):** importing the settlement notice and the bank's statement, matching by reference, and ESS payslips after the posting.
- **Wave 2 (PAY-E):** prorating a salary change inside the month, and the other calculation rules of G-PAY-11 — among them a profile outside the period (P-4, P-5).
- **PLAN-64:** the end-of-service calculation and the final settlement, added to the cycle there.
- **Wave 2 (PAY-G, PAY-H):** retroactive differences (a salary change dated in a past month) and transfer differences.
- **PLAN-65:** rerunning all of wave 1's acceptance live, the conflicting documents (G-PAY-22), and the SQL guard over every acceptance script (`AcceptanceScriptSqlWriteTests`); the prod-like seed's profile dates (P-5).
- **PLAN-41:** acceptance in CI, done; this plan places its classes by PLAN-41's rules.
- **Wave 2 (PAY-J):** the production calculation's timeout at the gateway (G-PAY-21).
- **A data task:** classifying the demo's service regimes, and the demo users a cycle there needs.

**Never commit:**
- the owner's local edits (`global.json`, `tmp-vendors.html`) — check `git diff --cached --name-only` before every commit;
- `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- another plan's untracked prompt file, or a helper of the local image builds;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- a screenshot or any other image, a bank file, or a payslip;
- any secret, token or password (templates keep their placeholders, `__SVC_SECRET__` or `${…}`), or a literal gitleaks would read as one.

**Never:**
- push to `main` or force-push, or rewrite a branch's history after it was pushed;
- merge with a pending or red check;
- touch the side PRs (#40, #42), Dependabot's PRs, the owner's branch `ops/ghcr-release-images`, its worktree, or their branches;
- disable host-key checking;
- widen a policy, a scope or a role to make a page or a test work; turn off `RejectConflictingRoleHolders` or `Payroll:Audit:Required`; or move the attendance gate's default to get green;
- relax D-53-1's date rule, or change the run's scope (G-PAY-11), in this plan;
- add a row to any ceiling list: they only shrink;
- write SQL in AC-PAY-59 or its driver, or an SQL write in `scripts/acceptance-payroll-*.ps1`;
- write an account number into a log, an audit text, an error, an evidence file or a URL;
- add users to the demo's realm, or calculate, post, fund, process or settle anything there;
- touch another project's Docker images, containers or volumes, or the owner's dev containers;
- print a password, or rotate a secret that exists on the server;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** (port 22 at P0 excepted: Preconditions 4).
- **An existing payroll result would change,** and the cause cannot be removed without changing the plan's design.
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning.
- **On the cycle's own fresh stack, `missingAttendance` stays non-empty after the close** for a cause outside this plan's code (P-5). Name each employee and HR.Core's reason first.
- **The calculations refuse with the interim gate's message** because STR-002's exceptions expired (after 2027-01-31): the owner renews them.
- **12 GB free for an image build cannot be reached** without touching other projects, **or a port the tests read is held** by something else (P-2).
- **The server's free space is under 15 GB** before the pull.
- **Port 22 is still blocked when P9 starts:** one line asking the owner to restore it.
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, keep-awake, preconditions, baseline

1. Write the `Started (UTC)` line (see «Timing»). Start the keep-awake process (P-2). Check the Preconditions.
2. `git switch -c feature/plan-59-pay-full-cycle-production-proof origin/main`. Bring in PLAN-58's merge note, and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan59-baseline.md` (P-3), and the demo's picture (P-15).
4. If port 22 is blocked, record it, and take the demo's picture at the start of P9.

Commit: `chore(pay059): baseline — the cycle's endpoints by plan, the fourteen integration tests, the scripts' SQL, the reads and months in use, the demo's picture`.

## P1 — The fourteen, the regime refusal, the handler's rules (plan §1, P-6)

- **`BlockRunRefusal` with the employees;** `Pay059RegimeBlockerTests`; `Pay059PayrollSodHandlerTests`; 0.3's reds fixed here or recorded with their owner.
- **Tests:**
  - the plan's integration filter, without and with `RUN_TESTCONTAINERS=true`;
  - the unit filters `Syr007` and `Pay058`, and the integration class `Pay058HeldEmployeeTests`, their meaning kept.

Commit: `test(pay059): the regime gate's blocked path and the handler's separation of duties proved; BlockRun's refusal names the employees; the fourteen payroll integration tests green`.

## P2 — The reads an SQL-free cycle needs (P-9)

- **The run read's** `revision`, `approvalsGiven`, `approvalsRequired` and `paymentBatchId`; **the batch's orders read;** whatever else P-3's read inventory found missing.
- **Tests:** each read's shape, another agency's 404, no account number in a body, its stance; the auditor's reach unchanged; `PayrollWriteActionsTests` and `WriteActionsHaveExplicitPolicyTests` still green.

Commit: `feat(pay059): the reads an SQL-free cycle needs — a run's revision, approvals and batch, and a batch's orders by employee`.

## P3 — The scripts without SQL, AC-21..23, the months (plan §3, P-8)

- **Both scripts** on the cancel API, with their month from the class; `Invoke-PayrollSql` gone.
- **The months' registry,** its unit test, and the picker `AcceptancePayrollMonths`.
- **AC-21, AC-22 and AC-23:** `[RequiredAcceptance]`, their windows, their clean-up through the API, the classification before their calculation.
- **Tests:** the plan's `Select-String` finds nothing; the registry's test; the acceptance project builds.

Commit: `test(pay059): the payroll acceptance scripts cancel through the API and write no SQL; AC-21, AC-22 and AC-23 take a month of their own from one registry`.

## P4 — AC-12 (plan §2, P-7)

- **`Ac12PayrollSodTests` rewritten** on a calculated, validated run of D-PAY-22's month, with PLAN-58's people, its review message, and its clean-up.

Commit: `test(pay059): AC-12 proves separation of duties on a calculated, validated run, with an independent review by two auditors`.

## P5 — AC-PAY-59, its CI stage, its guard (plan §4, P-10, P-11, P-13)

1. **`AcPay59FullCycleTests` and `PayrollCycleDriver`** (P-10), with D-PAY-22's picker and `ACCEPTANCE_PAYROLL_CYCLE_FROM`.
2. **CI scope:** the group `cycle`, its stage job, `ci.yml`'s matrix, `prodlike-final`'s wait; the three moved classes; `ac-ownership.json` (P-11).
3. **`scripts/pay059-validate.ps1`** and its `-SelfTest` (P-13).

Commit: `test(pay059): AC-PAY-59 drives one monthly cycle by HTTP alone, one person per role, on a CI stage of its own; pay059-validate`.

## P6 — Acceptance on the local stacks, then the independent review (P-12)

1. **Images and stacks.** Check the disk and the memory (P-2). Build the images, one service at a time. Record each stack's time to ready.
2. **The cycle's stack:** AC-PAY-59 three times in a row (P-12), each execution's evidence copied (P-10). Record the three months, each execution's time, the lines, the total, the held order, both files' control figures, and the settled batch.
3. **The main stack:** P-12's list, then AC-22 alone, last. All required classes must pass.
4. **The lab stack:** P-12's list.
5. `scripts/pay059-validate.ps1` (and `-SelfTest`), the seeded counts by psql, `down -v`, and the committed evidence restored.
6. **The full Testcontainers step on the host,** with CI's filter.
7. **Independent review** (one fresh subagent), before the push, so that the server's images carry its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - a step of AC-PAY-59 that reads or writes SQL, a check that is not an API read, or a «Pass» that does not assert what the plan's step says;
   - a test changed to pass rather than to keep its meaning (AC-12, the moved classes, `Pay058`'s message assertions);
   - a month two classes can both take on one stack, or a class that leaves something another class meets (accounts, scheduled or pending changes, open requests, holds, runs not cancelled);
   - the `BlockRun` refusal leaking a national number, or moving the run before it refuses;
   - a new read without its policy or stance, readable across agencies, carrying an account number or a field named like one, or widening the auditor's reach;
   - a script that still writes SQL or fixes a month, or sends a write without its antiforgery token;
   - a deviation that defers part of the plan, or a part skipped or listed to save time;
   - something PLAN-58's tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(pay059): AC-PAY-59a three times on three months and AC-PAY-59b, AC-12, AC-21, AC-22 and AC-23 pass; review fixes`.

## P7 — The runbook, the documents, `main` (plan §5, P-14)

1. **The documents** P-14 lists, and the final report's draft up to the server's section, so that the local gates run on the documents too.
2. **`main`:** if `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to `PayrollDbContext`, rebuild and recheck. Rerun the unit, architecture and integration tests and the acceptance classes the merge touched. Record what the merged change means for P9.

Commit: `docs(pay059): the monthly payroll cycle runbook; the pilot's indicators tied to AC-PAY-59; G-PAY-05's status` (and, only if the merge needed fixes, `fix(pay059): main merged, conflicts resolved`).

## P8 — Local gates, the release branch, the PR (P-17)

1. **Local gates, before any push:**
   - the full Testcontainers step on the host, with CI's filter (P6's, if nothing it builds or tests changed since; otherwise again);
   - `dotnet build HRManagement.slnx -c Release -warnaserror` (Lessons);
   - the local CI steps (P-2) on HEAD;
   - then `pwsh -NoProfile -File scripts/plan035-prepush.ps1`.

   All must be green, with every step not run named.
2. **In the same turn as the pre-push check:**
   1. Merge the feature branch into a new `release/plan-59` from `main` (`--no-ff`).
   2. Write `reports/plan59-pr.md` (short).
   3. `git push -u origin release/plan-59`
   4. `gh pr create --base main --head release/plan-59 --title "PLAN-59: a full payroll cycle proved with production settings" --body-file reports/plan59-pr.md`
3. **Start waiting** for the checks («Waiting»), and begin P9 beside them: alternate short waits on the PR's checks and on the server's work. A red check is read and fixed at once (P-17).

## P9 — The demo server (P-15, P-16), beside the PR's checks

Record every server change and its undo command **as you make it**.

1. Check SSH and P-16's condition.
2. Take the backup, then **verify** it. Take the read-only picture (P-15), if P0 could not.
3. **The GHCR build of `release/plan-59`, then the deploy** (P-16). Record the build's run id and duration, the deploy's stretches, the downtime, and the time to all healthy.
4. **Then check**, with evidence in `artifacts/plan59/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them. The new checks go in an `AcPay59VpsChecks` class, and leave nothing open.
   1. **The release:** the running tag, everything healthy, no migration pending.
   2. **The run read:** a demo run answers its `revision`, `approvalsGiven`, `approvalsRequired` and `paymentBatchId`.
   3. **The orders read:** a demo batch answers its orders (the picture's count) with no account number and no field named like one.
   4. **Nothing moved:** the picture's checksums as before.
   5. **Everyone else is unchanged:** the PLAN-36..58 server checks pass, and `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass. The checks that flicker from this machine are rerun alone.
5. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-16 says, prove the demo works, and record it.

Commit (pushed to the release branch): `test(pay059): the demo server on this release — the new reads answer and nothing moved; PLAN-36..58 server checks still green`.

## P10 — The final report, the merge (by you), the merge note

1. **`reports/plan59-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. **Decisions applied and deviations:** step 3's date (P-4), the run's scope and the gate (P-5), the cycle's own stage (P-11), the review's message, AC-22 outside CI, the months' rules, the reads added, and anything else. No deviation defers part of the plan.
   4. **The cycle:** each step with its person, its call and what it proved; the three months; the lines, the total, the held employee, both files' control figures, the payslips' sums, the settled batch.
   5. **AC-12:** each refusal with its answer, and the posting.
   6. **The integration tests:** the fourteen, the two new classes, the regime refusal's new text.
   7. **The scripts and AC-21..23:** the SQL removed, the months, the clean-up.
   8. **The reads added,** with their policies and stances.
   9. The existing tests that changed, each with the meaning it keeps; the numbers that did not move.
   10. The guards and their own negative tests.
   11. Unit, architecture, integration and acceptance results; the PR run's artifact link as the evidence of record; AC-22's local run.
   12. **The server:** before and after, changes and their undo, the backup location, the rollback note, and what the demo needs for a cycle.
   13. **Changes for API clients and operators:** the new fields and read, the refusal's text, the runbook, `ACCEPTANCE_PAYROLL_CYCLE_FROM`.
   14. Items that other plans own (Scope boundaries), and defects for later plans (G-PAY-11's overpayment of a past month, among them).
   15. **Timing:**
       - total wall-clock;
       - active time, waiting for CI, and every stop with its cause;
       - the build, acceptance and deploy times; each of AC-PAY-59's executions;
       - Actions minutes;
       - the output of `pwsh -NoProfile -File scripts/plan-timing.ps1 -From 25 -To 67`.
   16. **A readiness note** for PLAN-64 (a final settlement added to this cycle), PLAN-65 (the rerun of wave 1, the seed's profile dates) and wave 2 (PAY-E, PAY-G, PAY-I).

   Commit it on the release branch with the documents' last changes. A commit that leaves the code tree unchanged reuses the PR's green run.
2. **The merge (P-17), when every check is green:** `gh pr merge <n> --merge`.
3. **Then:**
   - `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`;
   - record the PR link, each check result, the merge commit and the post-merge run in `reports/plan59-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`;
   - commit that note on a local branch `docs/plan-59-merge-note`, as the plans before did, so the next plan brings it in. **Do not open a second PR for it;**
   - stop the keep-awake process by its PID (P-2).

## Definition of done

- **One monthly cycle is proved by HTTP alone, one person per role:** AC-PAY-59a Pass three times in a row on three consecutive months locally, and on the PR run (its evidence of record); AC-PAY-59b Pass.
- **AC-12, AC-21 and AC-23 = Pass** under `ACCEPTANCE_REQUIRE_ALL=true` on the prod-like profile, required in CI; **AC-22 = Pass** on the local prod-like stack.
- **The fourteen integration tests, `Pay059RegimeBlockerTests` and `Pay059PayrollSodHandlerTests` are green,** the four on PostgreSQL among them.
- **No SQL write in `scripts/acceptance-payroll-*.ps1`, and no SQL at all in AC-PAY-59.** The guard over every acceptance script stays PLAN-65's.
- **The runbook is published,** the two indicators are tied to the evidence, and G-PAY-05's «الحالة» line links the evidence folders.
- **Every existing payroll result is unchanged.**
- **Nothing in the plan was deferred to save time;** the deviations are P-4's date, P-11's stage, the review's message and AC-22's place, each with its reason.
- **The server** runs this code, and the checks moved nothing.
- **The merge** is done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what the cycle proved, in numbers (its months, lines, total, the held employee, both files, the settlement);
- what changed for payroll users and API clients, and what the owner will see on the demo;
- the deviations in plain words (Z's date and why; the cycle's own CI stage; AC-22);
- what the demo would need before a cycle can run there;
- how long the plan took, how long it waited for CI, and every stop with its cause;
- the PR link;
- the path of the final report.
