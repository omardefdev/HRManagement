# Task: PLAN-56 — The payroll run's state machine: one net, a rejection that reopens, a cancellation, and an approved reversal (PAY-C)

The deliverables:
- **One definition of the net**, used by the engine, the validation, the posting, the payslip and the files. A cycle with positive and negative adjustments validates, and what is posted equals what was approved.
- **A rejection reopens the run:** review → reject → correct → resubmit works.
- **Any run not posted can be cancelled,** and its period and its inputs are freed. A calculation stuck on a fingerprint conflict can be restarted.
- **No run moves between two states without a guard.** The manual salary line is gone.
- **A reversal is a request with a reason that another person approves.** It voids a batch that has not left, refuses one that has (D-PAY-16), frees the inputs and publishes its event.
- **The correction run passes the validation.** It takes only the adjustments aimed at its source, taxes no difference (D-PAY-17), and turns a negative difference into a recovery in the next cycle.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P10** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**Why this plan exists** (`docs/PAY_001_GAP_STUDY.md`: G-PAY-03, G-PAY-04, G-PAY-06, and part of G-PAY-07):
- **The net has two definitions.** The engine's net counts the adjustments, but the validation compares it with gross − deductions. So any adjustment with an amount (a transfer difference, a secondment, a manual settlement) fails the whole cycle. Then the posting rewrites every frozen net without the adjustments.
- **A rejection is a dead end.** The rejected run stays frozen and cannot be recalculated. The only cancellation refuses a completed job and frees nothing, so an input applied in a cancelled run is lost for good. A job that failed on a fingerprint conflict cannot be resumed.
- **No state is guarded.** `MarkValidated` returns any state to «محسوب», and the validation checks no state, so it revives an approved, posted or reversed run. A manual salary line has no components.
- **The reversal answers to nobody.** It has no actor, no reason and no separation of duties (the poster holds `Payroll.Reverse`). It sets the batch «معكوس» around its transitions and writes a reversal batch no channel was asked for. It leaves the orders and the adjustments behind, and publishes nothing.
- **The correction is outside every rule.** It takes every free adjustment of any period, counts the difference in the base and again as adjustments (and taxes it), and skips the validation.

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder. There is no second clone and there are no locks.
- **The main folder** is `/home/omar/HRManagement` on the owner's Linux notebook, where PLAN-53..55 ran. If the owner opened this session elsewhere, that folder is the main folder: record it. A folder's path is never a reason to stop.
- **PLAN-55 is merged** (Preconditions).

**Complete execution** (owner, 2026-11-12, standing since PLAN-51): the owner wants every plan carried out in full, however long it takes.
- Time is never a reason to skip, defer or allowlist anything.
- Only what the plan itself assigns elsewhere stays out: its «Out of scope».
- Where the plan speaks in general, this prompt makes it concrete, and all of it is in scope:
  - **the net:** `PayrollNet`, `SalaryLine.TotalAdjustments`, the validation, a posting that checks and writes nothing, every consumer that shows or exports a line, and the architecture test that keeps the net in one place;
  - **the correction:** targeted adjustments, the validation, no base and no tax on the difference, and the recovery of a negative difference;
  - **the run's life:** a rejection that reopens with its revision, a cancellation with its reason, the release of inputs, the restart after a fingerprint conflict, and a worker that writes nothing after a cancellation;
  - **the guards:** the transitions table, a status no code sets around it, the validation's guard, the manual line removed, a duplicate bank reference answered 409;
  - **the reversal:** the request, the decision by another person, the voided batch and its cancelled orders, D-PAY-16's refusal, the released inputs, `IPayrollReversed` through the outbox, and its consumer;
  - **two migrations,** `PAY056_NetFormula` and `PAY056_RunLifecycle`, each proved as the migrator;
  - **the API and the screens** of every new act. Every new action names its policy, every new controller declares its org-scope stance (PLAN-52), every new view passes PLAN-67's interface guard, and every new API write validates an antiforgery token — each from its first commit;
  - **AC-PAY-56a..f,** positive and negative, and the four PostgreSQL integration tests.

**Standing owner instruction on merges** (PLAN-38 to PLAN-55, PLAN-66 and PLAN-67): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-17).
- **Never ask the owner whether to merge:** his instruction is the permission. Asking cost PLAN-51 2 h 09 min.

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Check formatting on the files you changed (see «Lessons»).
4. Commit, using the phase's commit message.
5. Update **`reports/plan56-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan56-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan56-progress.md`. Its first line is:
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
- Write long outputs (inventories, logs, trx files) under `artifacts/plan56/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.
- Build the acceptance images once per code state. Run the unit and integration filters before a stack run, and rerun only the classes that failed.

**Long commands.** A foreground command that outlives the tool's time limit ends the turn. So:
- Run anything that can outlive the limit in the background or detached, with its log under `artifacts/plan56/`, and poll it. That covers the unit suite, the Testcontainers step, an image build, a stack start, an acceptance chain, the local CI steps and the server build.
- **A wait that returns because it reached the tool's limit is not the end of anything: the very next action is another wait or check, in the same turn.** PLAN-55 stood still **9 h 38 min**: a wait for the fourth start of its prod-like stack reached the limit, no further call followed, and the turn ended until the owner wrote «Try again». A turn ends only with the final reply, `BLOCKED:` or `WAITING-CI:`.
- Run **one heavy job at a time**: never an image build beside a host test run (P-2).
- Stop a process **by its id**, never by a pattern: PLAN-53's watch loop killed its own shell with a pattern that matched itself, and a build filled the disk.

**Waiting — for CI and for your own work.**
- **Never end the turn to wait** for CI or for work you started (a review, a build, a stack, a long check). Wait for it in the turn, as PLAN-49 and PLAN-50 did.
- **For CI:** run `gh pr checks <n> --watch --interval 60` in the foreground, with the tool's longest time limit. When it returns because of the limit, run it again, until every check has finished. A call that returns at the limit is not a refusal: run it again.
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
- run state machines with a guard on every transition, and an entity whose state no code can set around them;
- one definition of a computed figure that every reader shares, and an architecture test that keeps it that way;
- maker-checker on financial acts: a request and a decision by different people, with the reason recorded;
- releasing what a cancelled or rejected calculation consumed, inside one transaction, and a background worker that cannot write after a cancellation;
- a payment batch's legal transitions, and the line between money that has left and money that has not;
- the transactional outbox (PLAN-40) for an event and its consumer;
- EF Core 10 with PostgreSQL: migrations under forced row security, a data fill as the migrator, filtered unique indexes, optimistic concurrency on `xmin`;
- ASP.NET Core MVC and Web API: named policies, antiforgery on API writes;
- architecture tests with ceiling lists that only shrink; acceptance tests on Docker Compose; PowerShell (ASCII, 5.1-compatible syntax, run by `pwsh` 7 here) and bash.

You work on the `HRManagement` repository on the owner's Linux notebook (P-2), signed in to GitHub with push rights, and you operate the demo server through `ssh hrm-vps`, as PLAN-36..55 did.

**Where things stand** when this prompt was written (2026-11-25):
- **`main` is `a8f6ee32`:** PLAN-55 merged through PR #55 at 2026-11-25T02:48:39Z.
  - Its PR's first CI run failed CodeQL's `cs/cleartext-storage-of-sensitive-information`: a helper's parameter named `accountId` carried a row's key into an audit line, and the query read the name as account data. The fix (`c9517990`) renamed it; nothing else changed (Lessons).
  - Its merge note is on the local branch `docs/plan-55-merge-note` of the owner's machine. Read it, and PLAN-55's final report.
- **The demo server runs PLAN-55's tag `ea0d97a6ca26`** (`/opt/hrm/current-tag`; deployed 2026-11-24T22:35:47Z, previous `014db7e3a01a`): your rollback target.
  - 22 containers; 27 GB free after that build. Its disk fell to **8.8 GB** during the build from 35 GB. The local images of `5aa61b78e3a6` and `83b99786a420` were removed; they stay in the loopback registry.
  - Its last backup is `/opt/hrm/backups/pre-plan55-20261124T215254Z`.
  - PLAN-55's checks left four test accounts (one `Retired`, three `Rejected`), and PLAN-54's left two `LAW_50_2004_CHK54` versions and two rejected allowances: records, not to touch.
  - PLAN-55's picture of its payroll: 425 pay profiles, none with a legal regime; runs `Calculated` 1, `Posted` 4, `Approved` 1; no payment order and no hold. Verify it in P0.
- **PLAN-55 took 21 h 34 min to its merge** (started 2026-11-24T05:14:03Z), of which **9 h 38 min stood still** in one stop (see «Long commands»). To its final report it had worked about 9 h 23 min; the local gates and CI took the rest.
  - The costliest waits after the stop: the first prod-like stack needed **four starts**. Three failed under this machine's load: RabbitMQ's 5 s health check timed out at a load of 54 with the swap full, then a Keycloak admin call answered 401 during the identity sync.
  - A full unit run was aborted once by a test host crash after 6,316 passes (swap 3.6 of 4 GB). It passed when rerun with `--blame`.
- **The owner's machine** (PLAN-53..55 progress files):
  - Linux 6.14, 15 GB RAM with a 4 GB swap; PowerShell 7.7 at `~/bin/pwsh`, with `~/bin/powershell` linked to it as CI has; .NET SDK 10.0.400;
  - `/` is 233 GB, with 9 to 50 GB free across the three plans (38 GB at PLAN-55's start). Docker also holds other projects' images, containers and volumes, and the owner's dev containers: never touch them;
  - images are built one service at a time from local base images; **the migrator image through `docker compose build`** (no buildx here, so the job's `docker build` cannot read `COPY --exclude`);
  - **`scripts/ci-local.ps1` cannot run there:** the same `scripts/ci/steps.json` runs natively with `scripts/ci/run-steps.sh --all` in a fresh clone of HEAD, every step not run named, and `scripts/plan035-prepush.ps1` accepts those by name;
  - **its compiler servers go stale** (Razor parse errors on unchanged files): run the local CI steps with `UseSharedCompilation=false`, `UseRazorBuildServer=false`, `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0`, and `dotnet build-server shutdown` after such a failure;
  - **the owner's dev container `hrm-keycloak` restarts about every minute:** Chromium then meets `ERR_NETWORK_CHANGED`, and stalls over HTTP/3. The browser checks launch Chromium without QUIC and probe page by page (`Ux067PageProbe.ProbeEachAsync`);
  - port 22 reached the server without the VPN; `kubectl` is absent (`Ac42i` fails locally only); the job's Chromium step uses `--with-deps` (sudo): use the Chromium in `~/.cache/ms-playwright`.
- **The side PRs** about the E2E Playwright install (#40, #42), if still open: never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI today:**
  - a PR run has 15 checks (about an hour; `CI` about 42 min, `E2E` about 13 min). One acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - the required checks on `main` are `CI`, `CodeQL`, `Gitleaks`, `E2E` and `Acceptance`. The last step of `Acceptance` («Every stage job succeeded») fails when the image build or any stage failed;
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - a new acceptance class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs. The `prodlike` job runs the stages `journey`, `main`, `isolated` and `s2s42` on one stack;
  - AC-23, AC-24 and AC-25 are required (lab); `AcIam53SalaryChangeTests`, `AcPay54LegalFrameworkTests` and `AcPay55BankAccountTests` are required (prod-like);
  - **the committed evidence of AC-23..25 is still the «Fail» of the run before PLAN-53** (D-QA-20: nothing of a local run is committed). A narrowed local run that leaves them out copies that evidence in and its guard names them: include them, or read the run by its class table;
  - **the build and test steps live in `scripts/ci/steps.json`;** `CiStepsParityTests` keeps `ci.yml` in line with it.
- **From PLAN-53..55** (`docs/PAYROLL_SALARY_CHANGE.md`, `docs/PAYROLL_CONFIG_VERSIONING.md`, `docs/PAYROLL_RULES.md`, and their reports' readiness notes):
  - the policies `Payroll.Process`, `Payroll.Approve`, `Payroll.Post`, `Payroll.Reverse`, `Payroll.MasterData`, `Payroll.ChangeCancel`, `Payroll.LegalAttest`, `Payroll.Repair`, with their rules in `PayrollPolicyRules` and `HrManagementAuthorizationExtensions`. `PayrollWriteActionsTests` fixes each Payroll write's policy and who reaches it, role by role;
  - `IPayrollCalendar` for «today» (Damascus); `HrInvariantDecimalModelBinder`;
  - **every new API write validates an antiforgery token:** a `GET …/antiforgery` endpoint per API controller (`MessagingRepairAntiforgery.Issue`, as `PayrollFrameworksApiController` has), and `AcceptanceHarness.CreatePayrollClientAsync` for tests (its token, fetched from one Payroll endpoint, is accepted by every Payroll API write);
  - Payroll's org scope is wired (`PayrollOrgScopeKeys`, the filter); `OrgScopeCoverageTests` refuses new rows;
  - `InterfaceTextGuardTests` scans every view; the prod-like and E2E stacks enforce the CSP;
  - **a calculation path** passes `PayrollInterimGate.RefusalForRun`. A run in a month an interim v2 version governs stops after 2027-01-31 (PLAN-54 §15);
  - **manual inputs are approved by a second person** (PLAN-54, `ManualInputCommands`, `ApprovalStatus`); a manual input's `CreatedBy` is set by its command from the actor;
  - **PLAN-55's pinning:** a run pins each employee's account at its snapshot (`PayrollRunInputSnapshot.BankAccountId` → `SalaryLine.BankAccountId`). `BankAccountPinning` holds the one definition of «disbursed» (`DisbursedOrderStatuses`, `UnpaidBatchStatuses`) and the retirement rule (`RetiredAndNotReleasedAsync`). A retirement puts an `ACCOUNT-RETIRED:` hold on that employee in that run. A correction run writes no snapshot; its lines pin the account in force when it is calculated.
- **The code today** (read on `a8f6ee32`; the plan's line numbers predate PLAN-53..55, so find code by its content if a line moved):
  - **The run** (`PayrollRun.cs`):
    - `Status` has a public setter (`:24`). `ValidateCanCalculate` (`:60-65`) refuses frozen results;
    - `MarkValidated` (`:113-119`) sets `Calculated` from any state, freezes and marks validated;
    - `MarkCancelled` (`:121-126`) refuses only `Posted` and `Reversed`;
    - `Reject` (`:151-164`) returns to `Calculated` with the results still frozen;
    - `Post` (`:166-172`) and `Reverse` (`:174-179`);
    - there is no revision, no `PostedBy`/`PostedAt`, and nothing about who cancelled or why.
  - **Its statuses** (`PayrollRunStatus`): Draft 0, Calculated 1, Posted 2, UnderReview 3, Approved 4, Reversed 5, Calculating 6, Validating 7, Cancelled 8 (with `Pending` and `Processed` as aliases). Types: Regular 0, Correction 1, FinalSettlement 2.
  - **Its indexes** (`PayrollDbContext.cs`): `IX_PayrollRuns_LivePeriod` (`:105-108`), unique on (agency, period start, period end, run type) where the status is neither 5 nor 8; `IX_PaymentBatches_Run_BankRef` (`:142-145`).
  - **The engine** (`Engine/Rules/PayrollRules.cs`):
    - `AdjustmentRule` (order 60) adds each adjustment as a component, and consumes zero-amount rows (lifecycle markers) without one. It does not add to the gross;
    - `TaxRule` (order 70) taxes the gross, so the adjustments are outside the tax base: D-PAY-17 as the engine works today;
    - `RoundingRule` (`:331-358`) sets the net to max(0, gross − deductions + Σ adjustments) with the framework's rounding. For a correction it sets the net to `CorrectionDeltaNet`, which can be negative;
    - for a correction, `BaseSalaryRule` (`:14-24`) adds a «تصحيح رجعي — فرق إجمالي» component of type `Adjustment` with no `ReferenceId`, and adds the delta to the gross. So the difference is in the base, again as adjustments, and taxed.
  - **The validation** (`PayrollRunReconciliation.cs:8-34`) compares the net with gross − deductions, and the components with the net or with gross − deductions. Any adjustment with an amount fails.
    - The calculator (`PayrollRunParallelCalculator.cs:355-385`) then fails the job and sets the run back to `Calculating` by hand. Its direct status writes are at `:345`, `:360` and `:379`;
    - a profile version or input fingerprint that changed since the snapshot fails a partition (`:238-262`), and the job with it.
  - **The posting** (`PayrollRunHandlers.cs:178-272`):
    - it checks the separation of duties and a closed audit record («يجب إغلاق سجل مراجعة الرواتب قبل الترحيل»), then the job;
    - **it rewrites every line's net with `PayrollCalculation.ApplyNetFromGrossAndDeductions` (`:238-239`)**, dropping the adjustments, and posts that sum;
    - it adds a `Pending` disbursement batch (`BankTransferReference` = `PAY-yyyyMM-<run>`), and stages `IPayrollProcessed` in one save (PLAN-40 class B). **The Save→Publish→Save the plan assigns to PLAN-40 is already gone;**
    - a duplicate bank reference has no handler today: it would be a 500.
  - **The rejection** (`:149-176`) calls `run.Reject`; nothing reopens.
  - **The reversal** (`ReversePayrollRunHandler`, `:274-342`):
    - it has no actor, no reason and no separation of duties;
    - it adds a `Pending` batch of kind `Reversal` with `ReversesBatchId`, and sets the original to `Reversed` directly, around `PaymentBatchTransitions` (where `Pending → Reversed` is not even allowed);
    - it releases deductions and absences, but not adjustments; it writes a closed audit record with no actor; it publishes nothing.
  - **The correction** (`CorrectionRunHandlers.cs:1-130`):
    - it refuses a source that is not posted or has no pinned version, and passes PLAN-54's interim gate;
    - for each source line it takes every free approved adjustment of the employee, of any period, as `correctionDeltaGross` and `correctionDeltaNet`;
    - it saves the packages, then calls `MarkCalculated` and sets `ValidatedAtCalculate` and `ResultsFrozen` by hand: no validation.
  - **The jobs** (`PayrollRunJobHandlers.cs`):
    - `Resume` (`:273-343`) reruns the same snapshot, so a fingerprint conflict fails again;
    - `CancelPayrollRunJobHandler` (`:345-377`) refuses a completed job and frees nothing;
    - `ValidatePayrollRunHandler` (`:379-428`) checks no state, then calls `MarkValidated`.
  - **The loader** (`PayrollPartitionInputLoader.cs:122-165`) takes inputs that are free or applied in the same run, and approved: deductions due by the period's end; adjustments of any date; absences that overlap the period.
    - `PayrollRepository.cs` (`:165-181`, `:242-260`) marks what a package consumed: `AppliedInRunId` on deductions and absences, `PayrollRunId` on adjustments;
    - advance installments arrive from `HandleAdvanceApprovedHandler` as `AdvanceRecovery` deductions due month by month from today.
  - **Batches and orders:**
    - `PaymentBatchStatus`: Pending 0, Processed 1, Failed 2, Reversed 3, Submitted 4, Acked 5, Settled 6, Rejected 7, Returned 8. `PaymentBatchTransitions` (`PayrollRunJobEnums.cs:91-115`) and `PaymentBatchKind` (`Disbursement`, `Reversal`);
    - `PaymentOrderStatus`: Draft 0, Submitted 1, Acknowledged 2, Rejected 3, Settled 4, PartiallySettled 5, with `PaymentOrderService`'s transitions (`:110-115`);
    - a run that is `Approved` or `Posted` can take a funding decision and a partial disbursement (`CreateFundingDecisionCommand`, `ExecutePartialDisbursementCommand`, `PartialDisbursementService`): batches and arrears rows before the posting (G-PAY-08, PLAN-58's).
  - **Decisions on a run:** `PayrollRunApproval` with `ApprovalDecision` Pending/Approved/Rejected.
  - **The manual line:** `PayrollRunsController.AddSalaryLine` (`:162-212`, GET and POST) and `Views/PayrollRuns/AddSalaryLine.cshtml` make a line with no components. It also has a row in `tests/architecture/entity-binding-allowlist/payroll.txt` and a test in `MassAssignmentHttpTests`.
  - **The run's acts on the screens** (`PayrollRunsController`): `RejectRun` (`Payroll.Approve`), `ReverseRun` (`Payroll.Reverse`), `CreateCorrection` (`Payroll.Process`), `CancelJob` (`Payroll.Process`), all with antiforgery.
    - **`PayrollApiController` has no reject, reversal or correction, and none of its existing POSTs validates an antiforgery token;**
    - `Payroll.Reverse` admits `hr-admin`, `payroll-poster` and `payroll-reverser`. `docs/SOD_MATRIX.md` names the poster and the reverser as reversers.
  - **Who writes a run's status around its methods:**
    - the calculator (three writes);
    - the seeders, at construction (`AcceptancePayrollSeeder.cs:129-136`, `DemoPayrollSeeder.cs:366-373`);
    - the two creators of a draft (`CreatePayrollRunHandler`, `CreateCorrectionRunHandler`);
    - and the tests: about 36 set it in an initializer, and 6 assign it afterwards, in 3 files.
  - **The event:** `IPayrollProcessed`, staged by `PostPayrollRunHandler` (`docs/EVENT_PUBLISHER_INVENTORY.md`: SingleSave), consumed by Dev.Analytics' `PayrollProcessedConsumer`. It records an analytics event, idempotent per run; consumers are found by assembly scan.
  - **Test scripts that act around the code:** `scripts/acceptance-payroll-determinism.ps1:77-90` releases inputs by SQL, and AC-21..23 cancel by SQL: PLAN-59's to rewrite with this plan's cancellation.
  - **Personas** (`testdata/acceptance/personas.json`, 68):
    - agency A has `e2e-payroll-processor`, `e2e-payroll-approver`, `acc-a-payroll-approver-2`, `acc-a-payroll-poster`, `acc-a-payroll-reverser` (no scenario yet) and `acc-auditor`;
    - agency B has a processor and an approver;
    - **posting needs a closed audit record that no persona can close through the screens** (G-PAY-05, PLAN-58's): the lab classes close it with the test-only `scripts/acceptance-payroll-audit-fixture.ps1` (AC-25).
  - **The prod-like stack's months:**
    - the seed's runs take months from 2025-08 (posted, `DemoPayrollSeeder`) and 2026-08 to 2027-02 (one per status, `AcceptancePayrollSeeder`);
    - AC-IAM-53a takes the current month; AC-PAY-54 takes 2031 onwards; AC-PAY-55 takes May and June 2029 for agency A and the current month for agency B;
    - **AC-PAY-55c refuses to run while agency A holds a pending adjustment with an amount;**
    - no class and no seed uses 2028. The seed's `LAW_50_2004` v1 governs every month before 2031.
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43). This plan's first migration fills a column from another table (P-11): lift `FORCE` inside it on both, as `WF044` and `PAY055` do, and prove it as the migrator.
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role (PLAN-54's and PLAN-55's migration tests are the pattern).
  - **A new PostgreSQL test uses the connection strings of `PostgreSqlFixture`,** whose idle connections close after seconds. Make sure CI's Testcontainers filter picks up your new classes.
  - **The prod-like stack runs `ASPNETCORE_ENVIRONMENT=Acceptance`,** so it does not load `appsettings.Production.json`. A value in a Production file must be mirrored in `docker-compose.acceptance-prodlike.yml`.
  - **Personas:**
    - a persona change needs `scripts/acceptance-reset.ps1` before the first start (the personas' hash is in the seed pack's fingerprint);
    - **never write `personas.json` while an image is building** (PLAN-53 built one image again for it);
    - a new persona also changes `DATA-AUTH`; a user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh`.
  - **The classes of an acceptance group run in an order that changes from run to run.** Before any payroll calculation on the prod-like stack, call `AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync`. A class never relies on what another class did or did not do, and leaves nothing that changes another class's runs (P-12).
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - **Formatting:** before every commit, run `dotnet format whitespace HRManagement.slnx --verify-no-changes --no-restore --include <the files you changed>`. CI runs the full check.
  - **Acceptance tests:** a test posts the URL and the token the page renders (PLAN-44); an API write needs the antiforgery token (PLAN-47, PLAN-53).
  - **CodeQL runs in CI only:**
    - a new API `POST` or `PUT` without `[ValidateAntiForgeryToken]` fails `cs/web/missing-token-validation` (PLAN-53's first CI run);
    - **a value whose name reads as sensitive** (account, password, secret, token, IBAN) that flows into a log, an audit text or a stored field fails `cs/cleartext-storage-of-sensitive-information`, even when it is an opaque key (PLAN-55's first CI run). Name an identifier by what it is;
    - a log line that writes a value built from the request is `cs/log-forging`;
    - never answer CodeQL with a baseline entry or an ignore attribute.
  - **`-warnaserror` turns a use of an `[Obsolete]` member into an error.**
  - **The machine** (P-2): the disk (PLAN-53 filled it once); its memory under a stack start (PLAN-55's three failed starts); stale compiler servers; never delete build outputs while a step uses them; helpers outside the tree; never change `~/.ssh/config` in the middle of a run; after the host sleeps, the Docker VM's clock may lag (PLAN-50).
  - **The session may stand still** (PLAN-51: 7 h 44 min after the pre-push check; PLAN-55: 9 h 38 min during a stack start). Keep the progress file's next step exact, and go from the pre-push check straight to the push and the PR, in the same turn.
  - **The server:**
    - **its disk:** check the free space before the build, and watch it during the build (P-16);
    - run the server build detached (PLAN-44, PLAN-53), and the server suites with nothing heavy beside them (PLAN-45);
    - **a deploy may stop at `keycloak-reconcile` after PostgreSQL is recreated** (PLAN-40, PLAN-54), leaving the application containers created and not started: run the same deploy again at once;
    - if the server build fails because the provider's resolvers answer SERVFAIL, wait and retry; never change the server's resolver configuration (PLAN-50);
    - the server suites write into the committed `acceptance-evidence/` and `reports/`: restore those files, or move them under `artifacts/plan56/vps/` (PLAN-48);
    - a server check cleans up what it leaves open, and decodes a page before it compares Arabic text (PLAN-49);
    - **Production and the demo keep the password grant closed:** a script that signs in there uses a signed-in session's cookie or a bearer token (PLAN-55's repair script);
    - three older server checks fail or flicker from this machine only, so record them and do not chase them:
      - `AC_POL_47_vps_the_migration…` reads a «before» file of the formatted Windows machine;
      - a long browser tour meets the restarting dev container;
      - the workflow sweeps check reads the last 5,000 log lines of services that write about 6,800 in ten minutes, so run it just after a sweep cycle.
  - **Before the push, and before any fix pushed to the release branch:**
    - run the unit suite (its tests scan every source file) and the format check. After a test host crash, rerun it with `--blame`;
    - compare the seeded counts of `scripts/acceptance-verify.ps1` on your local stack, by psql;
    - run the full Testcontainers step on the host with CI's filter;
    - a test literal that looks like a key trips gitleaks' `generic-api-key`. Write it differently rather than allowlisting it.
  - **The E2E suite runs in CI only.**
  - **The architecture project's first run after a build** takes about 2 min 25 s, outside the tests (PLAN-51).
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools, or a script that reads and writes UTF-8 without a byte order mark.
  - The acceptance services have no container health check: readiness means `/health/ready`.

## Read first (mandatory)

1. **The specification:**
   - `docs/acceptance-plans/PLAN-56-pay-run-state-machine.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
   - `docs/PAY_001_GAP_STUDY.md`: G-PAY-03, G-PAY-04, G-PAY-06, G-PAY-07, and the PAY-C row.
2. **These reports:**
   - `reports/plan55-final-report.md` (§3, §5, §11, §13, §15) and its merge note;
   - `reports/plan54-final-report.md` (§15) and `reports/plan53-final-report.md` (§16);
   - `reports/plan55-progress.md`, for the machine's stack starts and the stop.
3. **These documents:**
   - `docs/PAYROLL_RULES.md`, `docs/PAY_001_APPROVAL_BYPASS.md`, `docs/PAYROLL_RUN_RESUME.md`, `docs/EVENT_PUBLISHER_INVENTORY.md`, `docs/SOD_MATRIX.md`;
   - the rows D-PAY-16, D-PAY-17 and D-PAY-18 of `docs/OWNER_DECISIONS_REGISTER.md`;
   - PLAN-57's plan (§4–5), for what this plan must leave ready for it.
4. The code named in «The code today».
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **No other plan session is running,** and you are in the main folder (see «How to run»), on `main` or on `docs/plan-55-merge-note`.
2. **`git fetch origin`**, and `origin/main` holds PLAN-55: the merge of `release/plan-55` is in `git log origin/main --merges --oneline -5`.
3. **The working tree is clean,** except for these, which may be present:
   - the owner's local edits to `global.json` and `tmp-vendors.html`, if any;
   - git-ignored files (`artifacts/`, `.env`, the local key rings of the dev scripts);
   - untracked prompt files under `docs/acceptance-plans/execution/`, this one included.

   Anything else uncommitted: leave it untouched, record it, and go on if it does not touch this plan's files. `BLOCKED:` only if it does. Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
4. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
   - **If port 22 is blocked** from the owner's network, this precondition does not stop P0. Record it, take the server's read-only picture at the start of P9, and go on with the local phases.
5. **`gh auth status`** shows an account with push rights to `omardefdev/HRManagement` and the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

The owner accepted the plan's defaults on 2026-10-31 (the wave 1 survey; the rows of `OWNER_DECISIONS_REGISTER.md`). If the owner changed one in a message before P0 starts, his message wins; record it.

- **D-PAY-16, reversing a run whose money left for the bank:**
  - a reversal is allowed while the run's batch has not been sent (`Pending`, `Failed`, `Rejected`): the batch is voided, and so are its orders;
  - after it was sent or settled, the reversal is refused with a message that points to a correction run (a negative difference → a recovery deduction);
  - recovering money already paid through the channel is wave 2 (PAY-I). No «reversal batch» claims to recover money no channel was asked for.
- **D-PAY-17, tax and insurance on correction differences:** an adjustment is outside the tax and insurance base, in a regular run and in a correction alike, as the engine works today. Taxing retroactive differences is wave 2 (D-PAY-8, PAY-H). The double count of the difference in the correction goes; no new base is invented.
- **D-PAY-18, who cancels a run:** the processor cancels what was not sent for review. A run `UnderReview` or `Approved` is cancelled by the holder of `Payroll.Approve`. The reason is always mandatory.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-56-pay-run-state-machine` from `origin/main` (plan 0.1), in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-55's merge note from `docs/plan-55-merge-note`. If that branch is gone, rebuild the record from GitHub (the PR, its runs, the merge commit), and say so;<br>&nbsp;&nbsp;– commit this prompt file. If the owner pasted it into the session rather than saving it, write it from the session's text first, as PLAN-54 and PLAN-55 did;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **The machine (Linux): awake, the disk, the memory, Docker, the ports.**<br>• **Keep the machine awake for the session, changing no setting:** `systemd-inhibit --what=sleep:idle --who=plan56 --why="PLAN-56" sleep infinity`, detached. Record its PID in `artifacts/plan56/keepawake.pid`, and stop it **by that PID** at the end. If the machine restarts, start it again and record it.<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• **Remove only this repository's leftovers:** compose projects `hrm-*`, Testcontainers, this plan's images and this repository's build cache. Never prune, stop or remove another project's images, containers or volumes, or the owner's dev containers.<br>• **The disk:** read `df -h /` and `docker system df` before every image build and every stack start.<br>&nbsp;&nbsp;– Build an image only with at least 12 GB free. Below that, prune this repository's build cache and remove this plan's unused images first;<br>&nbsp;&nbsp;– a watch that stops a build near 2 GB free stops it **by its PID**;<br>&nbsp;&nbsp;– if 12 GB cannot be reached without touching other projects: `BLOCKED:`, one line — the owner frees space.<br>• **The memory, before a stack start** (PLAN-55: three failed starts at a load of 54 with the swap full):<br>&nbsp;&nbsp;– nothing heavy beside it: no build, no test host, no browser, no idle stack of this repository;<br>&nbsp;&nbsp;– read `uptime` and `free -m` first, and start only when the load is below the number of CPUs;<br>&nbsp;&nbsp;– a start that fails from the load (a health check timing out, a 401 during the identity sync) is retried after the load falls, three times at most, each failure and its cause recorded;<br>&nbsp;&nbsp;– wait for each start in the turn («Long commands»).<br>• **The images:** one service at a time from the local base images (`acceptance-up.ps1 -UseLocalBaseImages`, and the helpers PLAN-53..55 kept outside the tree); the migrator through `docker compose build`. Never commit a helper.<br>• **One heavy job at a time:** never an image build beside a host test run. Close what you opened before a full unit run (a browser, an idle stack).<br>• **After an image build,** check that every service image was rebuilt from this tree. A failed build leaves stale images, and a `-NoBuild` run on them proves nothing.<br>• **Before each stack start,** check the stack's host ports with `ss -ltn` and `docker ps`. A port held by something that is not this repository's: `BLOCKED:` with the port.<br>• **The local CI steps:** `scripts/ci/run-steps.sh --all` natively in a fresh clone of HEAD, with the compiler servers off (see «Where things stand»), naming each step not run and why.<br>• **If this session runs on Windows again,** use PLAN-67's forms of these checks, and record it. |
| P-3 | **The baseline (plan 0.2–0.4),** into `reports/plan56-baseline.md`.<br>• **0.2:** `dotnet build HRManagement.slnx`; the unit tests filtered on `Pay003`, `PayrollFinancialInvariants`, `Correction` and `Pay001` (the plan's), then `Pay023`, `Qa002InvariantCoverage`, `PayrollEnginePipeline`, `Pay054` and `Pay055`. Green, or a list.<br>• **The inventories,** generated by search, never typed, into `artifacts/plan56/inventory.*`, and summarised:<br>&nbsp;&nbsp;– **0.3, the net:** every computation of a net from the gross and the deductions in Payroll, and every reader that shows or exports a line's figures (the engine, the validation, the posting, `PayrollRunDetailsDto`, `PayrollReportsController`, `Ess/Payslips.cshtml`, `PayrollExternalExportService`, `PayrollExportJobService`, `PayrollDeterminismComparer`, `PayrollCalculation`'s second formula at about `:139`, and any other the search finds);<br>&nbsp;&nbsp;– **0.4, the transitions:** every write of `PayrollRun.Status` outside `PayrollRun.cs` — product code, seeders and tests;<br>&nbsp;&nbsp;– every reader of a set of batch or order statuses (`BankAccountPinning`, `PaymentOrderService`, the coordinators, the bank reconciliation, the seeders, the scripts and their SQL), since this plan adds a value to each enum;<br>&nbsp;&nbsp;– every caller of `ReversePayrollRunCommand`, `AddSalaryLine` and `PaymentBatchKind.Reversal`, and every test or document that relies on a rejection leaving the run `Calculated`;<br>&nbsp;&nbsp;– the tests that pin today's behaviours this plan changes, each with what it will mean afterwards.<br>• **One more, from this prompt:** the demo's picture (P-15). |
| P-4 | **Policies, stances and the API's surface.**<br>• **Every new action names its policy:**<br>&nbsp;&nbsp;– reject → `Payroll.Approve`;<br>&nbsp;&nbsp;– restart a calculation → `Payroll.Process`;<br>&nbsp;&nbsp;– request a reversal → `Payroll.Reverse`, its roles unchanged (the SoD matrix names the poster a reverser);<br>&nbsp;&nbsp;– decide a reversal → `Payroll.Approve`;<br>&nbsp;&nbsp;– **cancel:** a policy that admits exactly the processing and the approval roles (and `hr-admin` as the others do), new if none is exactly that, so that no one else reaches it. D-PAY-18 is then decided in the controller by `IAuthorizationService`: a run `UnderReview` or `Approved` needs `Payroll.Approve`, any earlier state `Payroll.Process`. A caller without it gets 403, the run unchanged;<br>&nbsp;&nbsp;– the reversal requests' list → `Module.Payroll`.<br>&nbsp;&nbsp;`PayrollWriteActionsTests` gains each one, with who reaches it role by role.<br>• **The separation of duties lives in the handlers:** the decider of a reversal is neither its requester nor the run's poster (`PostedBy`). The requester may be the poster: the SoD matrix lets a poster ask, and the decision is another person's. Record it in `SOD_MATRIX.md`.<br>• **The stances:**<br>&nbsp;&nbsp;– actions added to `PayrollApiController` or `PayrollRunsController` are covered by their existing rows;<br>&nbsp;&nbsp;– a new controller (the reversal requests' screen, an API controller if you make one) declares `[OrgScopeCentralOnly]`: a run belongs to the whole agency, not to a unit.<br>• **The API** (on `PayrollApiController`, as the plan places it, or on a new API controller of its own, recorded): `POST runs/{id}/reject` (body `notes`), `POST runs/{id}/cancel` (body `reason`), `POST jobs/{jobId}/restart`, `POST runs/{id}/reversal-requests` (body `reason`), `POST reversal-requests/{id}/decide` (body `approve`, `notes`), `GET reversal-requests`, and a `GET …/antiforgery` endpoint on the controller that holds them.<br>• **Every new `POST` carries `[ValidateAntiForgeryToken]`** from its first commit. The existing POSTs of `PayrollApiController` stay as they are: their clients (AC-23..25, the scripts) send no token, and changing them is not this plan's.<br>• **The HTTP answers,** one rule everywhere:<br>&nbsp;&nbsp;– a missing note or reason, or a state the act cannot leave → 400, as the run's acts answer today (validate on `Approved`, cancel on `Posted`, recalculate on `Approved` without a rejection, D-PAY-16's refusal);<br>&nbsp;&nbsp;– a separation-of-duties refusal in a handler → 400 with its Arabic message, as the approval and the posting answer today; a policy refusal → 403;<br>&nbsp;&nbsp;– a second pending reversal request, a second open correction of the same source, a concurrent decision on the same run (`xmin`), a duplicate bank reference → 409, never a 500;<br>&nbsp;&nbsp;– another agency's run, job or request → 404.<br>&nbsp;&nbsp;Every refusal is an Arabic message, and changes nothing. |
| P-5 | **One net (plan §1).**<br>• **`PayrollNet`** (`Payroll.Domain/Services/PayrollNet.cs`): `Unclipped = gross − deductions + adjustments`, and `Net = max(0, Unclipped)` with the framework's rounding. It accepts a negative `TotalDeductions`, for PLAN-57's attendance recovery (its §4, rule 5).<br>&nbsp;&nbsp;Its users: `RoundingRule`, `ValidateLineIntegrity`, every builder of a line, and every place that needs a net it does not read from a stored line.<br>• **`SalaryLine.TotalAdjustments`:** the sum of the line's adjustments, written by the engine with the gross and the deductions.<br>• **`ValidateLineIntegrity`:** `NetPay == PayrollNet.Net(line)` and `Σ components == Unclipped`, within the existing tolerance.<br>• **`PostPayrollRunHandler` checks and writes nothing:**<br>&nbsp;&nbsp;– it runs `ValidateTotals` again on the loaded lines;<br>&nbsp;&nbsp;– it compares `Σ NetPay` with the `TotalNetPay` frozen at the validation. Any difference → refused, «تغيّرت النتائج بعد التحقق»;<br>&nbsp;&nbsp;– it assigns nothing to any line. `ApplyNetFromGrossAndDeductions` is deleted, and so is any second formula the inventory found.<br>• **The readers:**<br>&nbsp;&nbsp;– wherever a line's figures are shown or exported (the run's details, the payslip, the reports, the export jobs' CSV, the treasury file), its adjustments appear beside its gross and deductions, so a reader sees gross − deductions + adjustments = net;<br>&nbsp;&nbsp;– a CSV gains its new column at its end; every format change is listed for clients in the report;<br>&nbsp;&nbsp;– **no reader recomputes a stored net:** a posted line shows the net it was posted with, whatever the formula says of it.<br>• **The plan's rule 3:** the clipping of excess deductions in a regular run stays as it is. Carrying the excess with a legal ceiling is wave 2 (PAY-E).<br>• **`Pay056NetComputationArchitectureTests`:** no code in Payroll computes a net but `PayrollNet`. It searches for `GrossPay - ` and its other spellings, and has its own negative test with a planted computation.<br>• **A run with no adjustment gives every number it gave before,** unit for unit (P-18). |
| P-6 | **The correction (plan §1).**<br>• **`PayrollAdjustment.TargetSourceRunId`:** an adjustment aimed at the correction of one posted run, chosen on the adjustment's form and approved like any manual input (PLAN-54). The regular loader ignores targeted adjustments; the correction takes nothing else.<br>• **`CreateCorrectionRunHandler`:**<br>&nbsp;&nbsp;– it takes only the approved adjustments whose `TargetSourceRunId` is its source;<br>&nbsp;&nbsp;– there is no base and no tax on the difference (D-PAY-17): the `correctionDeltaGross` path leaves `BaseSalaryRule`, and the `CorrectionDeltaNet` override leaves `RoundingRule`, so a correction line's net is `PayrollNet`'s;<br>&nbsp;&nbsp;– then the run passes `ValidateLineIntegrity` before `MarkValidated`, as a regular run does. Nothing freezes it by hand;<br>&nbsp;&nbsp;– it keeps PLAN-54's interim gate and PLAN-55's pinning.<br>• **A negative difference** (`Unclipped < 0`): the line's net is 0, and a `DeductionRecord` is created in the same save, with:<br>&nbsp;&nbsp;– type `CorrectionRecovery`, system-made and approved;<br>&nbsp;&nbsp;– its amount, the line as `SourceReference`;<br>&nbsp;&nbsp;– `DueDate` = the first day of the period after the corrected one, a calendar date like every other due date.<br>&nbsp;&nbsp;So the next regular run recovers it, instead of the difference becoming zero.<br>• **One correction not yet posted per source run** (the index of P-11). A second one → 409. |
| P-7 | **A rejection reopens; a cancellation; the release of inputs; the restart (plan §2).**<br>• **`PayrollInputRelease.ReleaseAsync(runId, reason)`,** inside the caller's transaction, idempotent:<br>&nbsp;&nbsp;– it frees `DeductionRecord.AppliedInRunId`, `AbsenceDeduction.AppliedInRunId` and `PayrollAdjustment.PayrollRunId` for that run;<br>&nbsp;&nbsp;– it deletes the `CorrectionRecovery` deductions that run created and nothing applied;<br>&nbsp;&nbsp;– it touches the applied markers only, never any other field, so PLAN-57 adds its own fields (`CarryForward`, `OriginalPeriodStart/End`, `SourceVersion`) without changing it. A reopened run then picks up the attendance differences of its own period, as PLAN-57 §4 says;<br>&nbsp;&nbsp;– its callers: the cancellation, the rejection, the reversal and the restart.<br>• **`PayrollRun.ReopenAfterRejection`:** from `UnderReview` only → `Draft`, with `ResultsFrozen` and `ValidatedAtCalculate` false and `Revision` + 1. The rejection's note is mandatory.<br>• **`PayrollRunRevision`:** the rejected version's number, line count, `TotalNetPay`, a fingerprint of its lines, the rejecter and the note. Then its lines and their components are deleted, and `EntityAuditLog` records the deletion.<br>• **`RejectPayrollRunHandler`:** the rejection, the revision, the deletion and the release, in one transaction.<br>• **`CancelPayrollRunCommand`:**<br>&nbsp;&nbsp;– any state but `Posted`, `Reversed` and `Cancelled`; reason mandatory; D-PAY-18 (P-4);<br>&nbsp;&nbsp;– it cancels the active job and its partitions, releases the inputs, and sets the run `Cancelled` with `CancelledBy`, `CancelledAt` and `CancellationReason`;<br>&nbsp;&nbsp;– `CancelPayrollRunJobHandler` delegates to it.<br>• **A run with a funding decision or a partial batch** (allowed once `Approved`: G-PAY-08, PLAN-58's) is neither cancelled nor reversed by this plan. It is refused with an Arabic message that names the disbursement path: what its arrears rows become belongs to PLAN-58. Record it as a deviation.<br>• **The worker writes nothing after a cancellation:** its per-employee save checks, in the same transaction as the save, that the job is still running. So a package written after the release can neither re-attach an input nor leave a line.<br>• **`RestartCalculationCommand`:** for a `Failed` or `Paused` job on a run that is not frozen. The job is cancelled, its lines deleted, the inputs released, the run back to `Draft`; then a calculation starts with a new snapshot, through `StartPayrollRunJobHandler` and so through PLAN-54's interim gate. This is the way out of a fingerprint conflict.<br>• **The plan's rules:**<br>&nbsp;&nbsp;– no input stays attached to a cancelled run or a rejected revision;<br>&nbsp;&nbsp;– a cancellation and a rejection write `EntityAuditLog` and a `PayrollRunApproval` row with the decision and the reason (a new `ApprovalDecision.Cancelled`).<br>• **The screens:** on the run's page (`Details.cshtml`), reject with a note, cancel with a reason, and restart a failed or paused calculation. Each has `data-hr-confirm`, is shown only to who may do it, and passes the guard. |
| P-8 | **A guard on every transition (plan §3).**<br>• **`PayrollRunTransitions`** (`Payroll.Domain/Services/PayrollRunTransitions.cs`):<br>&nbsp;&nbsp;– `Draft → Calculating → Calculated (validated) → UnderReview → Approved → Posted → Reversed`;<br>&nbsp;&nbsp;– `UnderReview → Draft` (the rejection);<br>&nbsp;&nbsp;– `* → Cancelled` before the posting;<br>&nbsp;&nbsp;– and the calculator's own steps (`Validating`, a failure back to `Calculating`), each named.<br>&nbsp;&nbsp;Every method of `PayrollRun` calls `EnsureTransition`.<br>• **`MarkValidated`** only from `Calculating`, `Calculated` or `Validating`. **`ValidatePayrollRunHandler`** checks the state first: `validate` on an `Approved` run → 400, its state as it was.<br>• **Nothing sets `Status` after construction:**<br>&nbsp;&nbsp;– the calculator's writes become named methods (`MarkCalculationFailed()`, and one for `Validating`);<br>&nbsp;&nbsp;– **a seeded run is created in its state, not moved to it:** the seeders and the two creators of a draft set it at construction, through one named way (an init accessor over the field the methods set, or a factory). `Pay056TransitionTests` allows that way only there;<br>&nbsp;&nbsp;– the tests use `ForTesting.SetStatus` in the test projects only;<br>&nbsp;&nbsp;– EF still reads and writes the column.<br>• **`AddSalaryLine` is removed:** its two actions, its view, its link, its row in the entity-binding allowlist (the list shrinks) and its test in `MassAssignmentHttpTests`. That test's meaning moves to «no action binds a salary line». A manual change is an approved adjustment (PLAN-54).<br>• **A duplicate bank reference** (`IX_PaymentBatches_Run_BankRef`) → 409 with an Arabic message, never a 500.<br>• **`Pay056TransitionTests`:** the matrix of every state × every method, allowed or refused. |
| P-9 | **The reversal is a request another person approves (plan §4).**<br>• **`PayrollRun.PostedBy` and `PostedAt`,** set by the posting.<br>• **`PayrollReversalRequest`:** `PayrollRunId`, `RequestedBy`, `Reason` (mandatory), `Status` (`Pending`, `Approved`, `Rejected`), `DecidedBy`, `DecidedAt`, `DecisionNotes`. A filtered unique index allows one `Pending` request per run.<br>• **The commands** (`Features/PayrollRuns/Reversal/`): `RequestPayrollReversalCommand` and `DecidePayrollReversalCommand` (P-4). `ReversePayrollRunCommand` is deleted; the screens' `ReverseRun` becomes the request, with its reason.<br>• **An approved decision does, in one save:**<br>&nbsp;&nbsp;1. **every disbursement batch of the run** that is `Pending`, `Failed` or `Rejected` → a new `Voided`, through `PaymentBatchTransitions` (which gains `Pending → Voided`, `Failed → Voided` and `Rejected → Voided`). Its `Draft` orders → a new `PaymentOrderStatus.Cancelled`; a `Rejected` order stays the channel's record;<br>&nbsp;&nbsp;2. **a batch `Submitted`, `Acked`, `Settled`, `Processed` or `Returned`,** or an order that left: the whole decision is refused with D-PAY-16's Arabic message, which points to a correction run. Nothing changes;<br>&nbsp;&nbsp;3. `PayrollInputRelease`;<br>&nbsp;&nbsp;4. `run.Reverse()`;<br>&nbsp;&nbsp;5. `IPayrollReversed` staged through the outbox, before that one save (PLAN-40's SingleSave).<br>• **No reversal batch is written any more.** `PaymentBatchKind.Reversal` stays in the enum for the rows that exist; the inventory says what reads them.<br>• **The closed audit record** the reversal wrote with no actor is no longer written (§4, rule 2). The trace is the request and `EntityAuditLog`.<br>• **A rejected decision** needs its note and changes nothing but the request.<br>• **The event:**<br>&nbsp;&nbsp;– `HRManagement.MessageContracts/Events/IPayrollReversed.cs`, with its row in `EVENT_PUBLISHER_INVENTORY.md` (SingleSave, rule R4);<br>&nbsp;&nbsp;– a consumer in Dev.Analytics beside `PayrollProcessedConsumer`, recording `PayrollReversed` with the amount it takes back, idempotent per run. Together the two events give the run's net effect; verify that no other analytics reader needs to change.<br>• **After an approved reversal,** a new regular run of the same period can be created: the live index leaves a reversed run out.<br>• **The screens:** a «طلبات العكس» list where an approver decides, and the request on the run's page. Each act has `data-hr-confirm`, the names come through `_UserDisplayName`, and the views pass the guard. |
| P-10 | **Holds, pinned accounts and PLAN-57 under the new transitions.**<br>• **A hold bound to a run is never deleted or released** by a reopen, a cancellation, a restart or a reversal. Holds are controls, released only by their four-eyes release; their life is PLAN-58's.<br>&nbsp;&nbsp;Record what a stale `ACCOUNT-RETIRED:` hold does after a recalculation pins the new account (it still blocks the batch until a person releases it), as a note for PLAN-58.<br>• **PLAN-55's definitions:**<br>&nbsp;&nbsp;– `BankAccountPinning.UnpaidBatchStatuses` gains `Voided`, since a voided batch paid nothing;<br>&nbsp;&nbsp;– `DisbursedOrderStatuses` stays a list of what left, so a cancelled order is not «disbursed»;<br>&nbsp;&nbsp;– the retirement rule's queries leave a cancelled run out, as they leave out a reversed one;<br>&nbsp;&nbsp;– the readiness report of a reopened run reads its new lines.<br>• **A reopened, restarted or new run** pins accounts again at its new snapshot (D-PAY-15).<br>• **PLAN-57's handover:** `PayrollNet` accepts negative deductions, and the release touches the applied markers only (P-7). |
| P-11 | **The migrations: `PAY056_NetFormula` and `PAY056_RunLifecycle`.**<br>• **`PAY056_NetFormula`** (P1): the column `SalaryLines.TotalAdjustments`, filled for every existing line.<br>&nbsp;&nbsp;– The fill is the sum of the line's `Adjustment` components that reference an adjustment (`ReferenceId`). A historical correction line's «فرق إجمالي» component references none, and counting it would count the difference twice. Verify both shapes of line against the engine's history, and record what the fill found;<br>&nbsp;&nbsp;– it changes no amount: no net, gross or deduction is written;<br>&nbsp;&nbsp;– it reads `SalaryLineComponents` and writes `SalaryLines`, both tenant tables under forced row security. **Lift `FORCE` on both inside the migration, as `PAY055` does,** or it fills nothing and says nothing.<br>• **`PAY056_RunLifecycle`**, made in P3 and regenerated as P4 and P5 add to the model. Its hand-written SQL (row security, grants) goes back into each regenerated file; never hand-edit the generated parts:<br>&nbsp;&nbsp;– the run's `Revision` (1 for every existing run), `PostedBy`/`PostedAt`, `CancelledBy`/`CancelledAt`/`CancellationReason`;<br>&nbsp;&nbsp;– the tables `PayrollRunRevisions` and `PayrollReversalRequests`, with `TenantId`, row security through `TenantRlsMigrationHelper` and the grants, as `PAY055`;<br>&nbsp;&nbsp;– `PayrollAdjustments.TargetSourceRunId` with its index and a `Restrict` relation;<br>&nbsp;&nbsp;– **`IX_PayrollRuns_LivePeriod` limited to `RunType = Regular`**, its status filter kept;<br>&nbsp;&nbsp;– a unique index allowing one correction not yet posted per `SourceRunId`. The key of a `FinalSettlement` is PLAN-64's: say what the limited index leaves it.<br>• **The new enum values** (`PaymentBatchStatus.Voided`, `PaymentOrderStatus.Cancelled`, `ApprovalDecision.Cancelled`) take numbers no row and no SQL filter uses; check every numeric status in an index filter, a script and a seed.<br>• No byte order mark; `dotnet ef migrations has-pending-model-changes -p src/Services/Payroll/Payroll.Infrastructure -s src/Services/Payroll/Payroll.Web` reports none.<br>• **`Pay056MigrationPostgreSqlTests`:** as the migrator, a non-superuser under forced row security, lines with adjustment components in two agencies get their `TotalAdjustments`, each in its own agency, with every amount unchanged. A historical correction line is counted once. |
| P-12 | **Acceptance placement (PLAN-41's rules).**<br>• **`AcPay56RunStateMachineTests`** (AC-PAY-56a..f): `prodLike[]`, `required: true`, group `main`; `[RequiredAcceptance]` and `EvidenceRecorder("AC-PAY-56")`. It does not stop or recreate containers.<br>• **Its steps run in order,** as `AcPay55`'s do, and each scenario has its own period (the plan's rule):<br>&nbsp;&nbsp;– months of 2028, which no class and no seed takes, governed by the seed's v1;<br>&nbsp;&nbsp;– a precondition shows that no run of agency A covers them. A rerun on the same stack stops there with a clear message, and needs a fresh stack;<br>&nbsp;&nbsp;– `ClassifyRecordsAddedSinceTheSeedAsync` before each calculation.<br>• **The people** (agency A): `e2e-payroll-processor` creates and calculates; `e2e-payroll-approver` approves, rejects and decides; `acc-a-payroll-poster` posts; `acc-a-payroll-reverser` requests. Agency B's processor proves the 404.<br>&nbsp;&nbsp;The closed audit record a posting needs is made by `scripts/acceptance-payroll-audit-fixture.ps1`, as AC-25 does: a test-only precondition, recorded, because the auditor cannot reach it (G-PAY-05, PLAN-58).<br>• **The scenarios** (the plan's table; answers by P-4):<br>&nbsp;&nbsp;– **56a:** approved adjustments of +50,000 for one employee and −20,000 for another → the run validates; after the posting, `TotalNetPay` = the approved total = the sum of the payslips' nets (`GET api/payroll/payslips/{line}`);<br>&nbsp;&nbsp;– **56b:** the approver rejects with a note → `Draft`, `Revision` 2. The processor adds an adjustment, recalculates, validates and submits; the approver approves. Negative: a rejection without a note → 400; recalculating an `Approved` run without a rejection → 400;<br>&nbsp;&nbsp;– **56c:** a `Calculated` run holding an advance installment, an absence and an adjustment is cancelled → the three appear in a new run of the same period. Negative: cancelling a `Posted` run → 400; the processor cancelling an `Approved` run → 403;<br>&nbsp;&nbsp;– **56d:** `validate` on an `Approved` run → 400, its state as it was;<br>&nbsp;&nbsp;– **56e:** `acc-a-payroll-reverser` requests the reversal of a posted run whose batch has not left, with a reason; `e2e-payroll-approver` approves → the run `Reversed`, its batch `Voided`, its orders cancelled, its inputs free, and a new regular run of the period is created and calculated. Negative: the reverser deciding his own request → 403; the reversal of a run whose batch is `Submitted` (after `process`) → 400 with D-PAY-16's message. The rule «the decider is neither the requester nor the poster» is proved by `Pay056ReversalSodTests` on the handler: no persona holds both the reversal and the approval before PLAN-58;<br>&nbsp;&nbsp;– **56f:** a correction of a posted run with a targeted adjustment of −30,000 → it passes the validation, and creates a recovery deduction of 30,000 that the next cycle's run applies. Negative: an adjustment not aimed at it does not enter it.<br>• **Isolation** (the classes' order changes from run to run):<br>&nbsp;&nbsp;– the class leaves no free input with an amount in agency A after any scenario. What it creates is consumed by one of its own runs, or rejected. 56e's new run takes the released inputs; 56f's next-cycle run takes the adjustment its negative left;<br>&nbsp;&nbsp;– the reasons: AC-PAY-55c refuses to run while agency A holds a pending adjustment with an amount, and the regular loader takes a free approved input of any date into any later run;<br>&nbsp;&nbsp;– it asserts on its own employees' lines and its own inputs by id, never on a total another class's inputs could move;<br>&nbsp;&nbsp;– **AC-PAY-55c's gate keeps its condition.** Its stated reason becomes isolation (such an input would enter its runs): change its text, not its meaning.<br>• **An input that no screen or API of this release creates** (an absence comes from attendance; an advance's installments from an upstream approval, due from today) may be planted by psql as a test-only precondition, recorded, as AC-PAY-55b did. It is due or dated in the scenario's own month, one installment only. Everything a scenario proves goes through the API or the screens.<br>• **`ac-ownership.json`:** `AC-PAY-56` (owner PLAN-56, `provesClaim: true`, and its reason). `acc-a-payroll-reverser` and the other personas it uses gain AC-PAY-56 in their scenarios (Lessons: reset).<br>• **The evidence of record is the PR run's artifact,** not `acceptance-evidence/AC-PAY-56/`. Copy what the report needs into `artifacts/plan56/`, then restore the committed evidence. |
| P-13 | **Local testing (owner's rule of 2026-11-06).** Run only the classes this plan touches, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`, and the overlay groups with `-Stage prodLike:<group>`. **The full suite runs once, in CI.**<br>• **prod-like:** `AcPay56RunStateMachineTests`, `AcPay55BankAccountTests`, `AcPay54LegalFrameworkTests`, `AcIam53SalaryChangeTests`, `AcIam51ExplicitPolicyTests`, `AcIam52BranchScopeTests`, `AcUx67ReadableInterfaceTests` (with the run's page and the reversal requests' list added to its key pages), `AcHcm25ProductionApprovalTests`, the prod-like journey, and the `isolated` group;<br>• **lab:** `Ac12PayrollSodTests`, `Ac21PayrollDeterminismTests`, `Ac23PayrollFreezeTests`, `Ac24PartialDisbursementTests`, `Ac25FraudHoldTests`, `AcceptanceV2CatalogTests`, `AcHcm29HireContractTests`, the lab journey;<br>• **leastpriv:** `AcOps38FirstBootTests` and `AcTen48LeastPrivilegeTests` (the two migrations);<br>• **E2E:** in CI only.<br>Then compare the seeded counts of `scripts/acceptance-verify.ps1` by psql. Known local-only failures (`Ac42i` without `kubectl`) are recorded, not chased. |
| P-14 | **Scripts.**<br>• Every new or changed `.ps1` is ASCII-only, 5.1-compatible in syntax, and runs here with `pwsh -NoProfile -File …`.<br>• **`scripts/pay056-validate.ps1`** has a `-SelfTest` with planted defects, as `pay055-validate.ps1` does. It checks, at least:<br>&nbsp;&nbsp;– no computation of a net outside `PayrollNet`;<br>&nbsp;&nbsp;– no write of a run's status outside `PayrollRun.cs`, the named construction and the test helper;<br>&nbsp;&nbsp;– no new batch of kind `Reversal`, and no reference to `ReversePayrollRunCommand` or `AddSalaryLine`;<br>&nbsp;&nbsp;– the two migrations have no byte order mark, and the first lifts `FORCE` around its fill;<br>&nbsp;&nbsp;– every new `POST` with `[ValidateAntiForgeryToken]`;<br>&nbsp;&nbsp;– the `IPayrollReversed` row in `EVENT_PUBLISHER_INVENTORY.md`;<br>&nbsp;&nbsp;– `AcPay56RunStateMachineTests` required in `prodLike[]`.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile <ProdLike or Lab> -UseLocalBaseImages`. |
| P-15 | **The demo server facts this plan must respect.**<br>• **It runs Production in demo mode** (`Deployment__Mode=demo`): `hradmin` keeps the payroll policies; `e2e-payroll-processor` and `e2e-payroll-approver` are the two people of a server check.<br>• **P0's read-only picture:**<br>&nbsp;&nbsp;– the running tag and the free disk;<br>&nbsp;&nbsp;– the runs by status and type; the batches by status and kind; the orders by status; the holds by type and status;<br>&nbsp;&nbsp;– the free and the applied inputs (deductions, adjustments, absences);<br>&nbsp;&nbsp;– a checksum of every line (id, gross, deductions, net), never printing an amount;<br>&nbsp;&nbsp;– how many lines, by their run's status, have a stored net that differs from gross − deductions + their referenced adjustment components (the posts that rewrote a net).<br>• **What the owner will see change:**<br>&nbsp;&nbsp;– on a run's page: reject with a note, cancel with a reason, restart a failed calculation, request a reversal with a reason;<br>&nbsp;&nbsp;– a «طلبات العكس» list where an approver decides;<br>&nbsp;&nbsp;– the payslips, the run's details and the reports show the adjustments beside the gross and the deductions;<br>&nbsp;&nbsp;– the adjustment's form can aim it at a posted run, for a correction;<br>&nbsp;&nbsp;– the manual «إضافة سطر» page is gone;<br>&nbsp;&nbsp;– **nothing else moves:** no demo run changes state, and no amount changes. |
| P-16 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan56-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Before the deploy, read-only:** P-15's picture.<br>• **The disk:** at least 30 GB free before the build; PLAN-55's build fell from 35 GB to 8.8 GB.<br>&nbsp;&nbsp;– Remove the server's build cache and the local images of every release older than the running one. They stay in the loopback registry `127.0.0.1:5050` (undo: `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>`). Record each removal;<br>&nbsp;&nbsp;– watch the disk during the build, and stop the build **by its PID** below 3 GB.<br>• **Build once,** on the final code, detached; then `scripts/vps/deploy.ps1 -Tag <sha12>` (run it again at once if it stops at `keycloak-reconcile`). The migrations run at start-up.<br>• **The checks never cancel, reject, reverse or recalculate a run of the demo.**<br>&nbsp;&nbsp;– They make their own runs, in a month no demo run covers, and end each one `Cancelled`;<br>&nbsp;&nbsp;– a reversal request is made on a demo posted run only to be refused by its decider, never approved: an approval would void that run's batch and reverse the owner's data.<br>• **Rollback safety:** find out what PLAN-55's tag does with this plan's data, and record it in `OPS_VPS_SINGLE_HOST.md`:<br>&nbsp;&nbsp;– it ignores the new columns and tables;<br>&nbsp;&nbsp;– it meets `Voided`, an order's `Cancelled` and a decision's `Cancelled` as numbers it does not know;<br>&nbsp;&nbsp;– its validation fails adjustments again, its posting rewrites nets again, and its direct reversal goes around the requests.<br>&nbsp;&nbsp;So no run is posted, cancelled or reversed while the older release runs.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn:<br>&nbsp;&nbsp;– roll back to PLAN-55's tag, or fix forward;<br>&nbsp;&nbsp;– restore the backup only if data was damaged;<br>&nbsp;&nbsp;– prove the demo works, and record the downtime. |
| P-17 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P10, after the local CI steps and the pre-push check are green, in the same turn as the pre-push check (Lessons).<br>2. `gh pr create`, then wait («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, run the unit suite and the format check, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>`, and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-18 | **No regression of PLAN-25, PLAN-36..55, PLAN-66 and PLAN-67.** These stay green:<br>• **every existing payroll result of a run with no adjustment, unit for unit:** the golden pack tests (`Gov002LegalGoldenPackTests`), `PayrollEnginePipelineTests`, the determinism tests, `PayrollFinancialInvariantsTests`, `Pay029*`, `Pay023FrozenRunIsolationTests`, `Pay004ArrearsTests`, AC-21, AC-23, AC-24 and AC-25;<br>• the fraud tests `Frd001*`, the `Pay003*` tests (their meaning kept where this plan changes them), `Pay054*` and `Pay055*`;<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests`;<br>• `AcIam50`..`AcIam53`, `AcPay54`, `AcPay55`, `AcUx67`, `AcWf43`..`AcWf46` with the orphan class, `AcPol47`, both `AcTen48` classes, both `AcPol49` classes, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, `AcHcm29`, `Ac12`, and both recruitment journeys;<br>• the architecture tests (`PayrollWriteActionsTests`, `PayProfileWritersTests`, `PayrollReferenceWritersTests`, `Pay054FrameworkWritersTests`, `OrgScopeCoverageTests`, `InterfaceTextGuardTests`, `GuardSelfTests`, `WriteActionsHaveExplicitPolicyTests`), `Pay001ApprovalBypassTests` (its meaning kept: a batch is created only at the posting), `Iam053ApplierCopyForwardTests`, `OutboxPublishOrderingArchitectureTests`, `Rel004CompatibilityTests`, `ProductionWiringMatrixTests`, `CiStepsParityTests`;<br>• `scripts/pay055-validate.ps1`, `pay054`, `iam053`, `iam052`, `iam051`, `iam050`, `ux067`, `pol047`, `pol049` validate scripts, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-57:** attendance differences after the close, carrying them to the next cycle, their recovery, and the attendance gate before a calculation. This plan gives it the guards and the release only.
- **Wave 2 (PAY-H):** a payroll period entity with a cut-off date; unpaid leave approved after its period was posted; the retroactive differences engine; returning a cancelled leave's deduction after it was applied (`InboundEventHandlers.cs`, about `:430`).
- **Wave 2 (PAY-I):** recovering money paid through the channel, and the channel's `Ack`/`Settle` notices.
- **PLAN-58:** the unified disbursement path, the four eyes on holds and alerts, the double approval, and what a funding decision's arrears become when its run goes (P-7).
- **PLAN-40, done:** the posting's single save.
- **PLAN-64:** the end-of-service calculation (G-PAY-14) and the key of a `FinalSettlement` run.
- **Wave 2 (PAY-E):** carrying deductions above the salary with a legal ceiling (G-PAY-11).
- **PLAN-59:** rewriting the acceptance scripts that cancel or release by SQL (AC-21..23, the determinism script), with this plan's cancellation.

**Never commit:**
- the owner's local edits (`global.json`, `tmp-vendors.html`) — check `git diff --cached --name-only` before every commit;
- `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- another plan's untracked prompt file, or a helper of the local image builds;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- a screenshot or any other image;
- any secret, token or password (templates keep their placeholders, `__SVC_SECRET__` or `${…}`).

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- touch the side PRs (#40, #42) or their branches;
- disable host-key checking;
- widen a policy, a scope or a role to make a page or a test work;
- add a row to any ceiling list: they only shrink;
- change an amount of a run with no adjustment, or a stored amount of an existing line;
- let a reader recompute a posted net, or let the posting write a line;
- set a run's status around its transitions in product code;
- delete or release a payment hold outside its four-eyes release;
- write a reversal batch, or move a batch around `PaymentBatchTransitions`;
- cancel, reject, reverse or recalculate a run of the demo, or approve a reversal there;
- touch another project's Docker images, containers or volumes, or the owner's dev containers;
- print a password, or rotate a secret that exists on the server;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** (port 22 at P0 excepted: Preconditions 4).
- **An existing payroll result of a run with no adjustment would change,** and the cause cannot be removed without changing the plan's design.
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning.
- **12 GB free for an image build cannot be reached** without touching other projects, **or a port the tests read is held** by something else (P-2).
- **Port 22 is still blocked when P9 starts:** one line asking the owner to restore it.
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, keep-awake, preconditions, baseline

1. Write the `Started (UTC)` line (see «Timing»). Start the keep-awake process (P-2). Check the Preconditions.
2. `git switch -c feature/plan-56-pay-run-state-machine origin/main`. Bring in PLAN-55's merge note, and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan56-baseline.md` (P-3), and the demo's picture (P-15).
4. If port 22 is blocked, record it, and take the demo's picture at the start of P9.

Commit: `chore(pay056): baseline, every computation of the net, every write of a run's status, the readers of batch and order statuses, the demo's picture`.

## P1 — One net (plan §1, P-5, P-11's first migration)

- **`PayrollNet`, `SalaryLine.TotalAdjustments`,** the engine, the validation, the posting that checks and writes nothing, and every reader.
- **Migration `PAY056_NetFormula`** with its fill.
- **Tests:**
  - `Pay056NetFormulaTests`: every consumer gives the same net; adjustments positive, negative and zero; a clipped line; negative deductions;
  - `Pay056NetComputationArchitectureTests` with its planted defect;
  - the posting refuses a changed sum and writes no line;
  - `Pay056MigrationPostgreSqlTests`;
  - the golden pack, the pipeline and the determinism tests unchanged.

Commit: `feat(pay056): one net for the engine, the validation, the posting, the payslip and the files; the posting checks and writes nothing; migration PAY056_NetFormula fills each line's adjustments`.

## P2 — A guard on every transition (plan §3, P-8)

- **`PayrollRunTransitions`,** `EnsureTransition` in every method, a status no code sets after construction, the calculator's named methods, the seeders' construction, `ForTesting.SetStatus`.
- **The validation's guard; `AddSalaryLine` removed; the duplicate bank reference as 409.**
- **Tests:** `Pay056TransitionTests` (the full matrix, and the named construction allowed only where P-8 says); `validate` on every state; the changed tests with their meaning kept.

Commit: `feat(pay056): every transition of a payroll run passes a guard; the validation revives nothing; the manual salary line is removed`.

## P3 — A rejection reopens; a cancellation; the release; the restart (plan §2, P-7, P-10)

- **`PayrollInputRelease`,** `ReopenAfterRejection`, `PayrollRunRevision`, the rejection in one transaction, `CancelPayrollRunCommand` with D-PAY-18, the worker that writes nothing after a cancellation, `RestartCalculationCommand`.
- **The run's indexes** (the live index limited to regular runs; one open correction per source) in `PAY056_RunLifecycle`.
- **The API, the screens, the audit.**
- **Tests:** every release (deductions, absences, adjustments, recoveries), idempotent; the reopened run recalculates; the revision's snapshot; cancellation in every state, with D-PAY-18's 403; the restart after a planted fingerprint conflict; a run with a partial batch refused; holds untouched.

Commit: `feat(pay056): a rejection reopens the run with its revision; any run not posted can be cancelled with a reason; inputs are released; a failed calculation can be restarted`.

## P4 — The correction (plan §1, P-6)

- **`TargetSourceRunId`** on the adjustment and its form; the correction through the validation; no base and no tax on the difference; the recovery of a negative difference.
- **Tests:** an untargeted adjustment stays out of the correction and in the next regular run; a targeted one stays out of every regular run; a positive and a negative correction, each validated; the recovery's due date and its application in the next cycle; a second open correction → 409.

Commit: `feat(pay056): the correction run takes only the adjustments aimed at its source, passes the validation, taxes no difference, and recovers a negative difference in the next cycle`.

## P5 — The approved reversal (plan §4, P-9)

- **`PostedBy`/`PostedAt`,** `PayrollReversalRequest`, the two commands, the voided batch and its cancelled orders, D-PAY-16's refusal, the release, `IPayrollReversed` and its consumer, the screens; `ReversePayrollRunCommand` deleted.
- **Tests:**
  - `Pay056ReversalSodTests`: the decider is neither the requester nor the poster;
  - every batch status, allowed or refused;
  - one pending request per run;
  - the event staged before the one save (`OutboxPublishOrderingArchitectureTests`);
  - the consumer is idempotent;
  - a new regular run after a reversal.

Commit: `feat(pay056): a reversal is a request with a reason that another person approves; it voids a batch that has not left, refuses one that has, frees the inputs and publishes IPayrollReversed`.

## P6 — The PostgreSQL proofs (the plan's integration tests)

- **`Pay056RunLifecyclePostgreSqlTests`:**
  - a rejection concurrent with an approval on the same run → one wins, the other an `xmin` conflict (409);
  - a cancellation while a job runs → the inputs free and no orphan line after the worker finishes;
  - the new indexes → a second regular run after the first is reversed succeeds, and a second open correction of the same source fails;
  - `IPayrollReversed` in the outbox in the reversal's own transaction.
- **The full integration filter** on PostgreSQL, CI's filter.

Commit: `test(pay056): PostgreSQL proofs of a concurrent rejection, a cancellation under a running job, the run indexes, and the reversal's event in its own transaction`.

## P7 — Acceptance, then the independent review (plan Acceptance, P-12, P-13, P-14)

1. **The pieces:** `AcPay56RunStateMachineTests`, `ci-scope.json`, `ac-ownership.json`, the personas' scenarios, AC-PAY-55c's gate text, `scripts/pay056-validate.ps1`.
2. **Acceptance.**
   1. Check the disk and the memory (P-2). Reset the local stacks (the scenarios). Build the images, one service at a time. Start the prod-like stack, and record the time to ready.
   2. Run `AcPay56RunStateMachineTests` (AC-PAY-56a..f), positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   3. Then P-13's lists, `scripts/pay056-validate.ps1` (and `-SelfTest`), and the seeded counts by psql. All required classes must pass.
   4. `down -v`. Restore the committed evidence.
3. **The full Testcontainers step on the host,** with CI's filter.
4. **Independent review** (one fresh subagent), before the server, so the server build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - a net computed anywhere but `PayrollNet`, a reader that recomputes a posted net, a posting that writes a line;
   - a run with no adjustment whose numbers moved;
   - a status set around the transitions, a transition with no guard, a validation that revives a run;
   - an input left attached to a cancelled run or a rejected revision, or re-attached by a worker after a cancellation;
   - a correction that takes an untargeted adjustment, taxes a difference, skips the validation, or loses a negative difference;
   - a reversal decided by its requester or the poster, a batch moved around its transitions, a reversal batch written, a voided batch whose orders stay open, a reversal allowed after money left;
   - an event not staged before the one save;
   - a hold deleted or released outside its four-eyes release;
   - a request or run visible outside the caller's agency, an API write without an antiforgery token, a name CodeQL reads as sensitive flowing into a log or audit;
   - a migration that fills nothing under forced row security, or changes an amount;
   - a test changed to pass rather than to keep its meaning, or a part of the plan deferred, skipped or listed to save time;
   - something PLAN-55's tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(pay056): AC-PAY-56a..f pass on the prod-like stack; review fixes; CI scope and ownership`.

## P8 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to `PayrollDbContext` after yours, remove yours and add them again on top. Rebuild, and rerun the unit, architecture and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you.

Commit (only if the merge needed fixes): `fix(pay056): main merged, conflicts resolved`.

## P9 — The demo server (P-15, P-16)

Record every server change and its undo command **as you make it**.

1. Check SSH and P-16's condition.
2. Take the backup, then **verify** it. Take the read-only picture (P-15), if P0 could not.
3. **Check the server's disk, build and deploy.** Run the build once, detached, on the final code, watching the disk. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`, again at once if it stops at `keycloak-reconcile`. Record the downtime and the time to all healthy.
4. **Then check**, with evidence in `artifacts/plan56/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them. The new checks go in an `AcPay56VpsChecks` class. Each writes realistic Arabic text, keeps its marker in a field the reader does not rely on, and leaves nothing open.
   1. **The migrations:** PAY056 recorded. Every line's `TotalAdjustments` filled; the lines' checksum as in the picture; the historical mismatches counted as in the picture, and none rewritten.
   2. **A rejection that reopens:** `e2e-payroll-processor` creates a run for a month no demo run covers, calculates, validates and submits it; `e2e-payroll-approver` rejects it with a note → `Draft`, `Revision` 2, its revision recorded. A rejection without a note → 400.
   3. **A cancellation:** the processor recalculates that run and cancels it with a reason → `Cancelled`. The free inputs counted before the calculation are free again, and the audit and the decision row are written. The run ends `Cancelled`.
      If the demo refuses to calculate a new run (its 425 profiles carry no legal regime: PLAN-55 §13), record the refusal and its message. The cancellation is then proved on a `Draft` run the check creates, and the rejection stays proved by AC-PAY-56b.
   4. **The guards:** `validate` on the demo's `Approved` run → 400, its state as it was.
   5. **A reversal request, refused:** `hradmin` requests the reversal of a demo posted run with a reason. His own decision → 400. `e2e-payroll-approver` rejects it with a note → `Rejected`, and the run and its batch are unchanged.
   6. **The screens** open in Arabic under the enforced CSP with no violation (PLAN-67's method, Chromium without QUIC, page by page): the run's page with its new acts, the reversal requests' list, a payslip with its adjustments, the adjustment's form. Names, not ids.
   7. **Everyone else is unchanged:** the PLAN-36..55 server checks pass, and `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass. The AC-WF-46 sweeps check is rerun alone after a sweep cycle.
5. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-16 says, prove the demo works, and record it.

Commit: `test(pay056): demo server with the new run acts, a refused reversal request and the net migration; PLAN-36..55 server checks still green`.

## P10 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/PAYROLL_RULES.md`: the net and its formula, the correction, the recovery of a negative difference, the run's states and who may move them;
   - `docs/PAY_001_APPROVAL_BYPASS.md`: the state diagram, as built;
   - `docs/PAYROLL_RUN_RESUME.md` (about `:31`): the cancellation, the restart and the release;
   - `docs/EVENT_PUBLISHER_INVENTORY.md`: `IPayrollReversed`; `docs/SOD_MATRIX.md`: the reversal's request and decision;
   - the «الحالة» lines of G-PAY-03, G-PAY-04 and G-PAY-06 in `docs/PAY_001_GAP_STUDY.md`, and G-PAY-07 «جزئي — PLAN-56» with what is left for PLAN-57 and wave 2;
   - `docs/CI_AND_TESTING.md`: the new class; `OPS_VPS_SINGLE_HOST.md`: what the demo users see, the backup location, the change table, the rollback note;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local gates, before any push:**
   - the full Testcontainers step on the host, with CI's filter (P7, if nothing it builds or tests changed since; otherwise again);
   - the local CI steps (P-2) on HEAD;
   - then `pwsh -NoProfile -File scripts/plan035-prepush.ps1`.

   All must be green, with every step not run named. The report is committed before them, because a commit after a green local run needs another run.
3. **`reports/plan56-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations (the HTTP answers, the cancel policy, the run with a partial batch, the fill of `TotalAdjustments`, the seeded construction, anything else). No deviation defers part of the plan.
   4. **The net:** `PayrollNet`; what each reader shows now; what the migration filled, and the historical lines whose stored net differs (left as posted).
   5. **The run's states:** the transitions table; who may do what; the rejection and its revision; the cancellation; the restart; the release.
   6. **The correction:** targeted adjustments, the validation, D-PAY-17, the recovery.
   7. **The reversal:** the request and the decision, D-PAY-16, the voided batch and the cancelled orders, the event and its consumer.
   8. The existing tests that changed, each with the meaning it keeps; the numbers that did not move.
   9. The guards and their own negative tests.
   10. Unit, architecture, integration and acceptance results; the PR run's artifact link as the evidence of record.
   11. **The server:** before and after, changes and their undo, the backup location, the rollback note.
   12. **Changes for API clients and operators:** the new API, the new columns of the files, the removed page, the reversal that is now a request.
   13. Items that other plans own (Scope boundaries), and defects for later plans.
   14. **Timing:**
       - total wall-clock;
       - active time, waiting for CI, and every stop with its cause;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `pwsh -NoProfile -File scripts/plan-timing.ps1 -From 25 -To 67`.
   15. **A readiness note** for PLAN-57..59 and PLAN-64:
       - for PLAN-57: the release and the reopen, and the attendance differences it hands over;
       - for PLAN-58: the voided batch, the cancelled orders, the holds after a reopen, and the run with a partial batch;
       - for PLAN-59: the cancellation that replaces the scripts' SQL;
       - for PLAN-64: the key of a `FinalSettlement` run.
4. **Merge (P-17), by you, in the same turn as the pre-push check:**
   1. Merge the feature branch into a new `release/plan-56` from `main` (`--no-ff`).
   2. Write `reports/plan56-pr.md` (short).
   3. `git push -u origin release/plan-56`
   4. `gh pr create --base main --head release/plan-56 --title "PLAN-56: the payroll run's state machine — one net, a rejection that reopens, a cancellation, and an approved reversal" --body-file reports/plan56-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then:
   - `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`;
   - record the PR link, each check result, the merge commit and the post-merge run in `reports/plan56-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`;
   - commit that note on a local branch `docs/plan-56-merge-note`, as the plans before did, so the next plan brings it in. **Do not open a second PR for it;**
   - stop the keep-awake process by its PID (P-2).

## Definition of done

- **A cycle with positive and negative adjustments validates and posts,** and what is posted equals what was approved. Every reader shows the same net.
- **A rejection reopens the run, and a cancellation frees its period and inputs,** each with its reason recorded.
- **No run changes state without a guard,** and the transitions matrix is covered by tests.
- **A reversal is a request decided by someone who is neither its requester nor the poster;** it voids only what has not left, and its event is in the outbox of its own transaction.
- **The correction passes the validation,** takes only targeted adjustments, taxes no difference, and recovers a negative one.
- **Every existing payroll result of a run with no adjustment is unchanged.**
- **Nothing in the plan was deferred to save time.**
- **The tests:** AC-PAY-56a..f = Pass (the PR run's artifact is the evidence of record); the four PostgreSQL integration tests green; the PLAN-36..55 suites green.
- **The server** runs this code; no demo run changed state, and no amount changed.
- **The documents** P10 lists are updated, among them the «الحالة» lines of G-PAY-03, G-PAY-04 and G-PAY-06, and G-PAY-07 as partial.
- **The merge** is done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed for payroll users, and what the owner will see on the demo;
- what the net migration found on the demo (lines filled, historical lines left as posted);
- how long the plan took, how long it waited for CI, and every stop with its cause;
- the PR link;
- the path of the final report.
