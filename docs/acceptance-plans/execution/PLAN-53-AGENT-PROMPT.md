# Task: PLAN-53 — Payroll write policies, and a salary change that another person approves (IAM-B)

The deliverables:
- **No payroll write works with the `payroll` role alone:**
  - each of the sixteen payroll writes names its policy, and so does every action this plan adds;
  - Payroll's ceiling list is empty, and `PLAN-53` leaves the guard's owners.
- **A salary or grade change is a transaction:**
  - a payroll processor requests it, and a different payroll approver approves it;
  - it takes effect from the first day of a month;
  - one applier creates every new pay profile version and carries every field of the previous one;
  - three events leave through the outbox;
  - a branch-restricted user sees and acts on his branch only.
- **Salary scales and grades change by request too,** approved by another person, and a grade in use cannot be deleted.
- **A stable contract** for PLAN-54..59, in `docs/PAYROLL_SALARY_CHANGE.md`.
- **Payroll runs compute in acceptance:** the seed's employees get a service regime (PLAN-54's step 0.7 moves here), so AC-IAM-53 and the payroll regression AC-12, AC-23 and AC-25 run, and stay required.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P9** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**Why this plan exists:**
- The IAM study (G-IAM-02): base salary, salary scales and grades change with the module role `payroll` alone, and no second person approves.
- The payroll study (G-PAY-10): sixteen payroll POST actions name no policy. PLAN-51 froze them in a ceiling list signed `PLAN-53`.
- The independent check of the IAM study (its note 3): `EmployeePayProfiles.Create` has no scope.

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder `D:\my projects\HRManagement`. There is no second clone and there are no locks. **PLAN-67 is merged** (Preconditions). PLAN-67 lost 7 h 24 min by starting before PLAN-52 had merged.

**Complete execution** (owner, 2026-11-12, standing since PLAN-51): the owner wants every plan carried out in full, however long it takes.
- Time is never a reason to skip, defer or allowlist anything.
- Only what the plan itself assigns elsewhere stays out: its «Out of scope».
- Where the plan speaks in general, this prompt makes it concrete, and all of it is in scope:
  - **all sixteen writes, and every new action,** with a named policy. Payroll's write list ends empty, and `PLAN-53` leaves `CeilingList.Owners`;
  - **every transition** of the plan's state table, `Superseded` and the activation job included, with the API, its PUT alias and the screens;
  - `ApplySystemChangeAsync`, tested for **every** automatic source;
  - **scales and grades** for create, update and delete, with the delete protection and the `Restrict` migration;
  - **the branch scope** in submit, lists, details and actions;
  - **every new controller declares its org-scope stance** (PLAN-52), and **every new view passes PLAN-67's interface guard** from its first commit: there is no list to add a row to;
  - **AC-IAM-53a..e,** positive and negative; the integration tests; and the payroll regression AC-12, AC-23 and AC-25 green, as the plan's definition of done says. That needs payroll runs to compute in acceptance, which they do not today (P-10).

**Standing owner instruction on merges** (PLAN-38 to PLAN-52, PLAN-66 and PLAN-67): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-18).
- **Never ask the owner whether to merge:** his instruction is the permission. Asking cost PLAN-51 2 h 09 min.

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Check formatting on the files you changed (see «Lessons»).
4. Commit, using the phase's commit message.
5. Update **`reports/plan53-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan53-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan53-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Record the time spent waiting for CI, and **any time the session stood still** (a stopped turn, the usage limit, the machine asleep, a full disk, a blocked port, GitHub), each with its cause.
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session in the Claude desktop app, at `xhigh`). PLAN-50 stopped once at the usage limit of the owner's plan. Economy is about tokens, never about doing less of the plan:
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only; root-causing separate failures.
- Run **one independent review**, before the server phase, so that the single server build carries its fixes. Use a refuter only for findings rated high.
- If the owner switches the session's multi-agent mode on, use it for one read-only verification of the review's fixes, two agents at a time.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan53/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.
- Build the acceptance images once per code state. Run the unit and integration filters before a stack run, and rerun only the classes that failed.

**Long commands.** A foreground command that outlives the tool's time limit ends the turn. So:
- Run anything that can outlive the limit in the background or detached, with its log under `artifacts/plan53/`, and poll it. That covers the unit suite, the Testcontainers step, an image build, a stack start, an acceptance chain, `ci-local.ps1` and the server build.
- Run **one heavy job at a time**: never an image build beside a host test run (P-2).

**Waiting — for CI and for your own work.**
- **Never end the turn to wait** for CI or for work you started (a review, a build, a long check). Wait for it in the turn, as PLAN-49 and PLAN-50 did.
- **For CI:** run `gh pr checks <n> --watch --interval 60` in the foreground, with the tool's longest time limit. When it returns because of the limit, run it again, until every check has finished. A call that returns at the limit is not a refusal: run it again.
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

You are a senior .NET engineer for payroll systems that must be correct, auditable and secure:
- ASP.NET Core MVC and Web API authorization: named policies, roles from configuration, checks on the resource;
- maker-checker (four-eyes) transactions: state machines, optimistic concurrency, filtered unique indexes;
- effective-dated records: versions, carrying fields forward, closing the previous version;
- EF Core 10 with PostgreSQL: migrations, row-level security, `xmin`;
- MassTransit with the transactional outbox;
- hosted services with leader election across tenants;
- architecture tests with ceiling lists that only shrink; acceptance tests on Docker Compose; PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev`). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..67 did.

**Where things stand** when this prompt was written (2026-11-15):
- **`main` is `d9552bad`:** PLAN-67 merged through PR #51 at 2026-11-15T12:00:18Z (run 43703933305).
  - PLAN-67's merge note is commit `cbbbeaab` on the local branch `docs/plan-67-merge-note`. PLAN-52's note is already in `main`.
  - Read PLAN-67's final report and merge note.
- **The demo server runs PLAN-67's tag `1ee455bf1a6f`** (`/opt/hrm/current-tag`): your rollback target. Its 22 containers were healthy, with about 51 GB free on its disk; its last backup is `/opt/hrm/backups/pre-plan67-20261115T063540Z`.
- **PLAN-67 took about 51 h 54 min to its merge,** of which about 37 h were stops:
  - 23 h 43 min: drive C full. Six image builds in a day grew Docker's build cache to 219 GB, and Docker stopped (P-2);
  - 7 h 24 min: it started while PLAN-52 was still running;
  - 5 h 54 min: port 22 blocked without the VPN;
  - on the server, the first build filled the disk to 98% and failed late (P-17);
  - its PR needed three CI runs over 2 h 20 min (two E2E fixes and an E2E flake rerun); one run was lost to a fix pushed without the unit suite.
- **PLAN-52 took 19 h 03 min to its merge;** its PR run took 52 min.
- **The side PRs** about the E2E Playwright install (#40, #42), if still open: never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI today:**
  - a PR run has 15 checks and took about 52 min in PLAN-52. One acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - the required checks on `main` are `CI`, `CodeQL`, `Gitleaks`, `E2E` and `Acceptance`. The last step of `Acceptance` («Every stage job succeeded») fails when the image build or any stage failed;
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - `acceptance-job.ps1` takes `-Classes` and `-Stage`;
  - a new acceptance class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs. The `prodlike` job runs the stages `journey`, `main`, `isolated` and `s2s42` on one stack;
  - the lab `main` group holds `Ac12PayrollSodTests`, `Ac20DatabaseIsolationTests`, `Ac21PayrollDeterminismTests`, `Ac23PayrollFreezeTests`, `Ac24PartialDisbursementTests` and `Ac25FraudHoldTests` as `required: false`;
  - **the build and test steps live in `scripts/ci/steps.json`,** which CI and `ci-local.ps1` both read; `CiStepsParityTests` keeps `ci.yml` in line with it.
- **From PLAN-51:**
  - `src/Tests/HRManagement.ArchitectureTests` holds `ActionCatalog`, `CeilingList`, `GuardSelfTests` and `WriteActionsHaveExplicitPolicyTests`;
  - a `CeilingList` row is `Key | reason`, and the reason must start with an owner in `CeilingList.Owners` = `PLAN-53`, `IAM-F`, `IAM-H`. A missing list file is an empty list;
  - `RolesPolicy(name, defaults)` reads its roles from `Authorization:Policies:{name}:Roles`; `Authorization:AdminExcludedPolicies` removes Admin from the policies it lists outside demo mode.
- **From PLAN-52:**
  - the org-scope registry and filter, and the stances `[OrgScoped(typeof(T))]`, `[OrgScopeCentralOnly]` and `[OrgScopeNotApplicable("…")]`. `OrgScopeCoverageTests` refuses a controller without a stance unless it has a row, and refuses new rows;
  - out of scope by id answers 404;
  - PLAN-52's report told this plan to key Payroll «through `EmployeeOrgUnitSnapshot`». **Payroll has no such table:** the key is the unit stored on the pay profile (`EmployeePayProfile.OrgUnitId`) and copied onto the request.
- **From PLAN-67:**
  - `InterfaceTextGuardTests` scans every view: Arabic labels, enums through `HrEnumDisplay`, people through `_UserDisplayName` or `data-hr-user-id`, employees through `data-hr-employee-id`, dates through `HrDate`/`HrDateTime`, `_HrEmptyState` under every table, `data-hr-confirm` on destructive forms, no inline handler and no `style` attribute, JSON only inside `_TechnicalJson`. Its lists are empty and take no row;
  - the prod-like and E2E stacks enforce the CSP, as Production does;
  - display time is `Asia/Damascus` from `Localization:TimeZone`; storage stays UTC; a calendar date is stored at 00:00 UTC and never shifted.
- **From PLAN-40:** the outbox rules R1–R4 (`OutboxPublishOrderingArchitectureTests`): publish, then one save; every published type has a row in `docs/EVENT_PUBLISHER_INVENTORY.md`.
- **The code today** (read on `d9552bad`; find code by its content if a line moved):
  - **The sixteen writes** (`tests/architecture/write-policy-allowlist/payroll.txt`, each row signed `PLAN-53: one of the sixteen payroll policies`):
    - CompensationTypes and DeductionTypes: Create, Edit, Delete;
    - SalaryScales and PayGrades: Create, Edit, Delete;
    - EmployeePayProfiles.Create, PayrollRuns.Create, PayrollReports.BulkPayslips, ExportJobs.Cancel.
    - Today each needs only `Module.Payroll` (Admin or `payroll`, `HrManagementAuthorizationExtensions.cs:40`) and the module filter's `Payroll.Write` (Admin or `payroll`, `:184`; `Payroll.Web/Program.cs:96-97`). Every payroll persona carries `payroll`.
  - **The payroll policies:**
    - `Payroll.Process`: Admin or `payroll-processor`, an assertion (`:214-216`);
    - `Payroll.Approve`: Admin or `payroll-approver` (`:218-220`);
    - `Payroll.Post` and `Payroll.Reverse`: `RolesPolicy` (`:223-225`). Payroll's Production and Staging files exclude Admin from these two outside demo mode;
    - the other `PayrollRunsController` actions already name `Payroll.Process`, and the other `PayrollReportsController` actions `Payroll.Post`. `ExportJobsController` is `[Authorize]`, and its `Cancel` (`:35-37`) checks the job's owner.
  - **The salary write path:**
    - `EmployeePayProfilesController` (class `:14`): its Create GET builds an entity (`:73-77`); its POST binds `EmployeePayProfile` and sends `UpsertEmployeePayProfileCommand` (`:79-101`, the send at `:89`);
    - `PayrollApiController`'s `PUT employees/{employeeId}/pay-profile` (`:138-177`): `Payroll.Process`, with the scope checked in the action through `OrgScopeResolver.GetAllowedOrgUnitIdsAsync` and the profile's unit. Its body is `UpsertEmployeePayProfileApiRequest` (`:410`);
    - the handler (`EmployeePayProfileHandlers.cs:20-51`) saves a history snapshot, sets the previous profile's `IsActive = false` and `EffectiveTo = request.EffectiveFrom` (the boundary day counts twice), and creates a profile with only `EmployeeId`, `BaseSalary`, `PayGradeId` and the dates. It drops `OrgUnitId`, `RegimeCode`, `BankAccountIban`, `BankName` and `BankAccountVerifiedAt` (`EmployeePayProfile.cs:33-60`). No approver, no bounds check.
  - **Scales and grades** change at once:
    - `SalaryScalesController.cs:35-67` (an in-place edit at `:60`) and `PayGradesController.cs:45-86` (the bounds edited in place at `:71`);
    - deleting a grade empties `PayGradeId` on every profile that uses it (`SetNull`, `PayrollDbContext.cs:288-291`), and deleting a scale deletes its grades (`Cascade`, `:265-268`);
    - HR.Core checks offers against grade bounds through `PayGradeLookupInternalController`.
  - **The other writers of `EmployeePayProfiles`,** which the plan keeps:
    - `HandleContractActivatedHandler` (`InboundEventHandlers.cs:29`): new profiles at `:53` and `:72`; it closes the previous one with `EffectiveTo = StartDate` (`:69-70`); the bounds rule at `:96-108`;
    - `HandleTransferExecutedHandler` (`:281`): unit, regime and `BaseSalary += salaryChange`, in place (`:304-313`);
    - `HandleEmployeeCreatedHandler` (`:632-645`, `BaseSalary` 0);
    - the seeders: `DemoPayrollSeeder.cs:185`, and `AcceptancePayrollSeeder`, which throws when it reapplies over a profile count it did not seed.
  - **The profile's rules:** the unique index `IX_EmployeePayProfiles_OneActive` filtered on `IsActive` (`PayrollDbContext.cs:284-287`) and `xmin` (`:292`). `EmployeePayProfileRules` holds `ResolveBaseSalary`, `CountEmployeeWorkingDays` (it prorates by the profile's dates, `:10-26`) and `CreateHistorySnapshot` (`:28-40`). `PayrollConfigurationVersioning` already closes a previous version at `from.AddDays(-1)`: follow it.
  - **Runs:**
    - a calculation takes the `IsActive` profiles only (`PayrollRunJobHandlers.cs:90-95`);
    - the statuses are Draft 0, Calculated 1, Posted 2, UnderReview 3, Approved 4, Reversed 5, Calculating 6, Validating 7 and Cancelled 8. `ResultsFrozen` is set when the results freeze (AC-23: calculate, validate, then `freeze-status`);
    - one live run per tenant, period and run type (`IX_PayrollRuns_LivePeriod`; Reversed and Cancelled do not count).
  - **Payroll's wiring:** MVC and the module filter at `Program.cs:96-97`; the outbox `AddPostgresOutbox<PayrollDbContext>(consumerOutbox: true)` at `:119`; `UseHrOutboxFlushGuard` at `:161`; the start-up tenant at `:130`. No hosted service, and no org-scope filter yet.
  - **Jobs and tenants:** Payroll has no tenant table. `AssetApprovalStartRetryJob.FindTenantsAsync` (Admin.Logistics, `:62-67`) finds tenants in the audit log, which stays outside row security (`TenantRlsMigrationHelper.cs:88-89, 145`), and runs each tenant in `TenantIdFlow`. The keys in `AdvisoryLockKeys.cs` end at `4_001_012`.
  - **Org scope:**
    - PLAN-52's helpers `AddOrgScopeFilter<TContext>` and `AddSnapshotOrgScopeProvider<TContext>` (`OrgScopeActionFilter.cs:277-291`), and the key pattern of `MovementsOrgScopeKeys`;
    - `tests/architecture/org-scope-allowlist/payroll.txt` holds 24 `IAM-H` rows, `EmployeePayProfiles` among them. `EssController` declares `[OrgScopeNotApplicable]`;
    - PLAN-52's HTTP scope tests: `Iam052ScopeHttp.cs` and `Iam052ServiceScopeHttpTests.cs`.
  - **Entity bindings:** `tests/architecture/entity-binding-allowlist/payroll.txt` (`IAM-F`) lists, among others, EmployeePayProfiles.Create, PayGrades.Create/Edit, SalaryScales.Create/Edit, CompensationTypes.Create/Edit and DeductionTypes.Create/Edit.
  - **Every place that names PLAN-53 in the guards:** `CeilingList.cs:15` (and its comments at `:7` and `:12`); the planted rows at `GuardSelfTests.cs:56`, `iam051-validate.ps1:202` and `ux067-validate.ps1:255`; `iam051-validate.ps1:9` and `:61-65`, which expect Payroll's list signed `PLAN-53`; the comment at `WriteActionsHaveExplicitPolicyTests.cs:17`.
  - **Payroll runs are refused in acceptance today:**
    - `PayrollRegimeGovernance.ValidateEmployeesAsync` refuses a whole Regular or final run when any employee in it is blocked;
    - HR.Core's `ServiceRegimePayrollBlockService` (`:22-52`) blocks an employee with an open `ServiceRegimeException`, or with a current employment record that has no `LegalRegimeId`;
    - `ServiceRegimes:BlocksFinalPayroll` (`PayrollRunParallelCalculator.cs:48`) defaults to true and is set nowhere;
    - the acceptance seed sets no regime (`AcceptanceDataSeeder.cs:332-351`). The national mandatory seed runs at every start, and its backfill opens an `UnclassifiedRegime` exception for each current record without one (`NationalSeedOrchestrator.cs:106` → `ServiceRegimeBackfillService`);
    - PLAN-67's lab run recorded it: «the lab seed's open service-regime exceptions refuse payroll» (AC-23, AC-24, AC-25). PLAN-54 planned the fix as its step 0.7; P-10 moves it here.
  - **Payroll runs the acceptance seed already holds:**
    - each agency has seven scenario runs, one a month from August 2026 to February 2027, of types Correction and FinalSettlement, with no salary lines (`AcceptancePayrollSeeder.cs:106-150`). **November 2026 is a FinalSettlement run UnderReview with frozen results, and December 2026 a Correction run Approved**;
    - and twelve Regular runs, August 2025 to July 2026, all Posted (`DemoPayrollSeeder.cs:343-375`).
    - P-6 decides which runs block a change.
  - **Acceptance and payroll:**
    - only lab classes compute runs: AC-12 (June 2027), AC-23 (February 2027), AC-24 (April 2027) and AC-25 (May 2027);
    - AC-23 (`Ac23PayrollFreezeTests.cs:59-69`) and AC-25 (`Ac25FraudHoldTests.cs:35-49`) write through the PUT; `Pay023FrozenRunIsolationTests.cs:58` sends the command;
    - `scripts/acceptance-verify.ps1:538` expects 1489 profiles (`DATA-PAYPROFILE`).
  - **Personas** (`testdata/acceptance/personas.json`, an embedded resource whose hash is part of the seed pack's fingerprint):
    - `payroll-processor` (`payroll`, `payroll-processor`; scenarios AC-12, AC-21, AC-22) and `payroll-approver` (`payroll`, `payroll-approver`; AC-12, AC-24, AC-25);
    - `a-payroll-poster`, `a-payroll-reverser`; `b-payroll-processor` and `b-payroll-approver` (agency B); `c-payroll` (agency C);
    - no payroll persona is restricted to a branch;
    - on the demo: `e2e-payroll-processor` and `e2e-payroll-approver` (`realm-manifest.json`).
  - **Events:**
    - contracts live in `src/Shared/HRManagement.MessageContracts/Events/` (`IContractActivated`: `Guid? TenantId => null`, `int SchemaVersion => 2`);
    - `Rel004CompatibilityTests` reads `docs/rel-compat/event-schema-inventory.json`;
    - AcMsg40 proves a publication by the `OutboxMessage` sequence, and its delivery by an empty table afterwards (`AcMsg40AtomicPublishingTests.cs:44-47`).
  - **The API's clients:** `docs/rel-compat/api-client-inventory.json` lists the gateway's payroll route as Tier-0. The OpenAPI baseline covers HR.Core only.
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43). Lift `FORCE` inside the migration, as `WF044` does. This plan's migration changes no data (P-12).
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role.
  - **A new PostgreSQL test uses the connection strings of `PostgreSqlFixture`,** whose idle connections close after seconds. Make sure CI's Testcontainers filter picks up your new classes.
  - **The prod-like stack runs `ASPNETCORE_ENVIRONMENT=Acceptance`,** so it does not load `appsettings.Production.json`. A value in a Production file must be mirrored in `docker-compose.acceptance-prodlike.yml`.
  - **Personas and users:**
    - a persona change needs `scripts/acceptance-reset.ps1` before the first start (the personas' hash is in the seed pack's fingerprint);
    - a new persona also changes `DATA-AUTH` in `scripts/acceptance-verify.ps1`;
    - a user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh`.
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - **Formatting:** before every commit, run `dotnet format whitespace HRManagement.slnx --verify-no-changes --no-restore --include <the files you changed>`. CI runs the full check.
  - **Acceptance tests:**
    - a test posts the URL and the token the page renders (PLAN-44);
    - policy API writes need the antiforgery token (`AcceptanceHarness.CreatePolicyLifecycleClientAsync`, PLAN-47).
  - **The machine:**
    - **Docker's build cache** grew to 219 GB in a day of image builds and filled drive C (PLAN-67: 23 h 43 min). P-2 checks it before every build;
    - after the host sleeps, the Docker VM's clock lags, and time-based tests fail until it catches up (PLAN-50);
    - **SSH and the VPN** (PLAN-48, PLAN-51, PLAN-67): the owner's network may block outbound port 22, and only ProtonVPN lets it through. With the VPN on, Testcontainers fail with "Failed to connect to 192.168.65.254", and downloads crawl (PLAN-67: 5 to 12 KB/s). So order the work (P-2).
  - **The session may stand still** (PLAN-51: 7 h 44 min after the pre-push check, until «Try again»). Keep the progress file's next step exact, and go from the pre-push check straight to the push and the PR, in the same turn.
  - **The server:**
    - **its disk:** PLAN-67's first build filled it to 98% and failed late. Check the free space before the build (P-17);
    - run the server build detached (PLAN-44), and the server suites with nothing heavy beside them (PLAN-45);
    - if the server build fails because the provider's resolvers answer SERVFAIL, wait and retry; never change the server's resolver configuration (PLAN-50);
    - the server suites write into the committed `acceptance-evidence/` and `reports/`: restore those files, or move them under `artifacts/plan53/vps/` (PLAN-48);
    - a server check cleans up what it leaves open, and decodes a page before it compares Arabic text (PLAN-49);
    - a user without HR.Core signs in through another page; a refusal of the gateway's own pages is a 302; the gateway audits only what it forwards (PLAN-51).
  - **Before the push, and before any fix pushed to the release branch:**
    - run the unit suite (its tests scan every source file) and the format check: PLAN-67 lost a CI run to a fix pushed without them;
    - compare the seeded counts of `scripts/acceptance-verify.ps1` on your local stack, by psql;
    - run the full Testcontainers step on the host with CI's filter, when `ci-local.ps1` runs with `-SkipTestcontainers`;
    - a log line that writes a value built from the request is CodeQL's `cs/log-forging`;
    - a test literal that looks like a key trips gitleaks' `generic-api-key`. Write it differently rather than allowlisting it.
  - **The E2E suite cannot run on the owner's machine** (it needs PowerShell 7 and Python): it runs in CI only.
  - **The architecture project's first run after a build** takes about 2 min 25 s, outside the tests (PLAN-51).
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools, or a script that reads and writes UTF-8 without a byte order mark.
  - The acceptance services have no container health check: readiness means `/health/ready`.

## Read first (mandatory)

1. **The specification:**
   - `docs/acceptance-plans/PLAN-53-iam-payroll-salary-change.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
   - `docs/PAY_001_GAP_STUDY.md` (G-PAY-10, and the rows of PAY-A) and `docs/IAM_001_SCOPE_GAP_STUDY.md` (G-IAM-02, G-IAM-08).
   - `docs/acceptance-plans/PLAN-54-pay-legal-framework-reference-data.md`: its 0.5 (the names it takes from this plan) and its 0.7 (which moves here).
2. **These reports:**
   - `reports/plan67-final-report.md` and its merge note;
   - `reports/plan52-final-report.md`, its readiness note;
   - `reports/plan51-final-report.md` §3 and §10;
   - the outbox parts of `reports/plan40-final-report.md`.
3. **These documents:** `docs/PAY_001_APPROVAL_BYPASS.md`, `docs/SOD_MATRIX.md`, `docs/EVENT_PUBLISHER_INVENTORY.md`, `docs/MESSAGE_EVENT_MATRIX.md`, `docs/PAYROLL_RULES.md`, `docs/PAYROLL_CONFIG_VERSIONING.md`, `docs/PRV_001_DPIA_PAYROLL.md`, `docs/REL_004_COMPATIBILITY_POLICY.md`, `docs/L10N_001_LOCALIZATION.md`, and the rows D-IAM-2, D-PAY-6 and D-53-1..5 of `docs/OWNER_DECISIONS_REGISTER.md`.
4. The code named in «The code today».
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **You are in `D:\my projects\HRManagement`,** on `main` or on `docs/plan-67-merge-note`, and no other plan session is running.
2. **`git fetch origin`**, and `origin/main` holds PLAN-67: the merge of `release/plan-67` is in `git log origin/main --merges --oneline -5`.
3. **The working tree is clean,** except for these, which may be present:
   - the owner's local `global.json` edit;
   - `tmp-vendors.html`;
   - git-ignored `artifacts/`;
   - untracked prompt files under `docs/acceptance-plans/execution/`, this one included.

   Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
4. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
   - **If port 22 is blocked** from the owner's network, this precondition does not stop P0. Record it, take the server's read-only picture at the start of P8, and go on with the local phases (P-2).
5. **`gh auth status`** shows `omardefdev` with the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

The owner accepted the plan's defaults on 2026-10-31 (the wave 1 survey; the rows of `OWNER_DECISIONS_REGISTER.md`). If the owner changed one in a message before P0 starts, his message wins; record it.

- **D-IAM-2, who changes a salary:** a transaction. The requester holds `Payroll.Process` (`payroll-processor`); the approver holds `Payroll.Approve` (`payroll-approver`) and is not the requester. It takes effect after approval. Scales and grades the same way, with a legal reference.
- **D-PAY-6, the source of a salary change:** the administrative decision (contract, promotion, transfer) is the natural source. A manual request is the exception: a mandatory reason, a mandatory legal reference (`Payroll:SalaryChange:RequireLegalReference=true`) and a second person. Automatic sources are recorded in the same entity as `Applied`.
- **D-53-1, the effective date:** the first day of a month (`Payroll:SalaryChange:RequireMonthStart=true`), not before the current month (refused at the request). Never applied in a month whose run is frozen, approved or posted (refused at approval and at activation). P-6 makes «a month whose run» precise.
- **D-53-2, a salary outside the grade's bounds:** refused at the request (`Payroll:SalaryChange:AllowOutOfGradeBounds=false`), with the rule the contract consumer applies (`InboundEventHandlers.cs:96-108`).
- **D-53-3, open requests:** one per employee (`PendingApproval` or `Scheduled`), by a filtered unique index.
- **D-53-4, reference data:** `SalaryScales` and `PayGrades` change by a request that `Payroll.Approve` approves (`Payroll:MasterData:RequireApproval=true`). `CompensationTypes` and `DeductionTypes` take the `Payroll.MasterData` policy only.
- **D-53-5, `PayrollReports.BulkPayslips`:** `Payroll.Process`.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-53-iam-payroll-salary-change` from `origin/main` (plan 0.1), in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-67's merge note (`cbbbeaab` on `docs/plan-67-merge-note`);<br>&nbsp;&nbsp;– commit this prompt file;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **The machine: awake, Docker, the disk, the ports, the VPN.**<br>• **Keep the machine awake for the session, changing no setting.** At P0, start a hidden background PowerShell process that holds `SetThreadExecutionState(ES_CONTINUOUS \| ES_SYSTEM_REQUIRED)` and sleeps in a loop. Record its PID, and stop it at the end of the session. It does not stop a Windows Update restart; record one if it happens.<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• Before a Docker-heavy run, check `docker ps`. Remove only this repository's leftovers: compose projects `hrm-*`, Testcontainers, and untagged images of this repository's own builds. Never prune volumes or images of other projects.<br>• **The disk and the build cache (PLAN-67 stopped 23 h 43 min):** before every image build and every stack start, read the free space of drive C (Docker's data disk) and `docker system df`.<br>&nbsp;&nbsp;– If the build cache is above 60 GB, or the free space below 40 GB: run `docker builder prune -f` (cache only; the next build is slower) and remove this repository's untagged images. Record each prune.<br>&nbsp;&nbsp;– If the free space is still below 15 GB, moving Docker's disk is a setting of the owner: `BLOCKED:`.<br>• **One heavy job at a time:** never an image build beside a host test run.<br>• **After an image build,** check that every service image was rebuilt from this tree. A failed build leaves stale images, and a `-NoBuild` run on them proves nothing.<br>• **Before each stack start,** compare the stack's host ports with `netsh interface ipv4 show excludedportrange protocol=tcp`. If a port the tests read is reserved, freeing it is a system change for the owner: `BLOCKED:` with the range.<br>• **The VPN and SSH** (Lessons): Testcontainers and image builds need the VPN off; SSH may need it on.<br>&nbsp;&nbsp;– At P0, check both at once. If SSH and Testcontainers both work (the owner may route only the server's address through the VPN), no ordering is needed: record it.<br>&nbsp;&nbsp;– Otherwise run the full Testcontainers step on the host **before** P8, on the code the server will get. Check SSH again just before P8. If port 22 is blocked then, `BLOCKED:` with one line: the owner turns ProtonVPN on, and you continue on his next message.<br>&nbsp;&nbsp;– When `ci-local.ps1` runs with the VPN on, use `-SkipTestcontainers`, and name the run of the full step it relies on. |
| P-3 | **The baseline (plan 0.2–0.4),** into `reports/plan53-baseline.md`.<br>• **0.2:** `dotnet build HRManagement.slnx`; `dotnet test src/Tests/HRManagement.ArchitectureTests`; the unit tests filtered on `Pay0` and `Payroll`. Green, or a list.<br>• **0.3, the inventory,** generated by search, never typed, into `artifacts/plan53/inventory.*`, and summarised:<br>&nbsp;&nbsp;– the sixteen rows, each with its controller, action and the policies that guard it today;<br>&nbsp;&nbsp;– every caller of `UpsertEmployeePayProfileCommand` and of `PUT api/payroll/employees/{id}/pay-profile`: code, tests, acceptance, scripts, E2E and docs;<br>&nbsp;&nbsp;– every code that adds, changes or removes an `EmployeePayProfile`, a `PayGrade` or a `SalaryScale`, in every project (seeders and tests included);<br>&nbsp;&nbsp;– every reader of the active profile: runs, reports, self-service, the internal lookups;<br>&nbsp;&nbsp;– the entity-binding and org-scope rows of Payroll that this plan removes (P-4, P-9).<br>• **0.4, the outbox:** the registration (`Program.cs:119`, `:161`), PLAN-40's rule (publish, then one save), and the existing Payroll publisher whose pattern you copy.<br>• **Two more, from this prompt:** the payroll gate in acceptance, read from the code (P-10); and the demo's read-only picture (P-16). |
| P-4 | **Explicit policies and the empty list (plan §1).**<br>• **`Payroll.MasterData`:** a constant in `HrManagementPolicies`, and a `RolesPolicy` beside the other payroll policies, default roles Admin and `payroll-processor`.<br>&nbsp;&nbsp;– Its roles go into Payroll's `appsettings.json`, `appsettings.Production.json` and `appsettings.Staging.json`, and the Production value is mirrored in `docker-compose.acceptance-prodlike.yml` (Lessons), the way PLAN-51's policies are.<br>&nbsp;&nbsp;– Admin is not excluded from it.<br>• **The sixteen:**<br>&nbsp;&nbsp;– CompensationTypes and DeductionTypes, Create/Edit/Delete → `Payroll.MasterData` (D-53-4);<br>&nbsp;&nbsp;– SalaryScales and PayGrades, Create/Edit/Delete → `Payroll.MasterData`: they create requests (P-8);<br>&nbsp;&nbsp;– EmployeePayProfiles.Create → `Payroll.Process`: it submits a request (P-5);<br>&nbsp;&nbsp;– PayrollRuns.Create → `Payroll.Process`; PayrollReports.BulkPayslips → `Payroll.Process` (D-53-5);<br>&nbsp;&nbsp;– ExportJobs.Cancel → `Module.Payroll`, named on the action; its owner check stays.<br>• **Every new action names its policy:**<br>&nbsp;&nbsp;– submit (form and API) → `Payroll.Process`; a reference change request → `Payroll.MasterData`;<br>&nbsp;&nbsp;– approve, reject, `activate-due`, and the reference decisions → `Payroll.Approve`;<br>&nbsp;&nbsp;– cancel → one policy that admits `Payroll.Process` or `Payroll.Approve`, defined once beside them. The handler lets a processor cancel his own request only;<br>&nbsp;&nbsp;– lists and details → `Module.Payroll`.<br>• **Separation of duties lives in the handler:** the decider's `sub` must differ from `RequestedBy`, for approve and for reject. Admin holds both policies: two admins can request and approve, one admin cannot decide his own request.<br>• **The lists:**<br>&nbsp;&nbsp;– `write-policy-allowlist/payroll.txt` ends empty: delete the file (a missing file is an empty list);<br>&nbsp;&nbsp;– `PayrollWriteActionsTests` (plan deliverable) asserts no row, and no Payroll write without a named policy. Then `WriteActionsHaveExplicitPolicyTests` accepts no row in any service; update its comment;<br>&nbsp;&nbsp;– `PLAN-53` leaves `CeilingList.Owners`, and the comments that name it are updated;<br>&nbsp;&nbsp;– each planted row that used `PLAN-53` moves to an owner still listed, keeping its self-test's meaning: `GuardSelfTests.cs:56`, `iam051-validate.ps1:202` (with its `-SelfTest`), `ux067-validate.ps1:255`;<br>&nbsp;&nbsp;– `iam051-validate.ps1:9` and `:61-65` now expect no write list and no sensitive list at all;<br>&nbsp;&nbsp;– the entity-binding rows of EmployeePayProfiles.Create, PayGrades.Create/Edit and SalaryScales.Create/Edit leave with their view models (P-5, P-8). CompensationTypes and DeductionTypes keep theirs (`IAM-F`, out of scope).<br>• **The gateway** stays on `Module.Payroll` for payroll writes; its comment becomes true. No gateway change. |
| P-5 | **The salary change contract (plan §2, §3).**<br>• **The entity** `PayProfileChangeRequest`, with the plan's fields: `BaseEntity` with its tenant, `ConfigureOptimisticConcurrency`, `[AuditRedact]` on every amount.<br>&nbsp;&nbsp;– The enums `PayProfileChangeKind`, `PayProfileChangeSource` and `PayProfileChangeState` (PendingApproval, Scheduled, Applied, Rejected, Cancelled, Superseded) give every value an Arabic `[Display(Name = "…")]` (PLAN-67's guard).<br>• **`EmployeePayProfileVersioning` and the applier** (`IPayProfileChangeApplier` / `PayProfileChangeApplier`), the only creator of a version for a request:<br>&nbsp;&nbsp;– it closes the previous version: `IsActive = false`, `EffectiveTo` = the day before `EffectiveFrom`;<br>&nbsp;&nbsp;– it saves the history snapshot with `CreateHistorySnapshot`;<br>&nbsp;&nbsp;– the new version copies **every** field of the previous one, then applies the proposal. `Iam053ApplierCopyForwardTests` enumerates the properties by reflection and fails on a property that is neither copied nor named as deliberately not copied (the id, the audit fields, the version);<br>&nbsp;&nbsp;– it sets the request `Applied`, with `AppliedAt` and `AppliedProfileId`, publishes `IPayProfileChangeApplied`, and saves once.<br>• **`ApplySystemChangeAsync(…)`** for the automatic sources:<br>&nbsp;&nbsp;– it records a request `Applied` at once: the source is not Manual or Api, `DecidedBy` stays empty, and the original decision goes into `SourceReference`;<br>&nbsp;&nbsp;– it supersedes an open request of the same employee (`Superseded`, `SupersededByRequestId`);<br>&nbsp;&nbsp;– a frozen month does not refuse it: the run's snapshot isolates it, as Pay023 proves (P-11);<br>&nbsp;&nbsp;– no consumer calls it in this plan (plan §3).<br>• **The commands** Submit, Decide (approve or reject) and Cancel return `(Success, RequestId, State, Error)`. **The HTTP answers,** the same everywhere:<br>&nbsp;&nbsp;– a broken input rule (bounds, date, reason, reference, no active profile) → 400;<br>&nbsp;&nbsp;– a state conflict (a second open request, a concurrent decision, a frozen month, a wrong state) → 409, never a 500;<br>&nbsp;&nbsp;– deciding one's own request → 403 «لا يجوز اعتماد الطلب من طالبه»;<br>&nbsp;&nbsp;– outside the scope or in another agency → 404.<br>&nbsp;&nbsp;A form shows the same Arabic message, and nothing changes.<br>• **The rules:**<br>&nbsp;&nbsp;– D-53-1: P-6;<br>&nbsp;&nbsp;– D-53-2: the grade is the proposed one, else the current one; its minimum and maximum when set; the message names both bounds. Checked at submit, again at approval and at activation;<br>&nbsp;&nbsp;– D-53-3: a filtered unique index on the employee, per tenant, where the state is PendingApproval or Scheduled;<br>&nbsp;&nbsp;– D-PAY-6: the reason (500) is mandatory; the legal reference is mandatory for Manual and Api while `Payroll:SalaryChange:RequireLegalReference` is true;<br>&nbsp;&nbsp;– the `Current…` fields are the active profile's at submit.<br>• **The API:** the plan's endpoints, in a new API controller with a declared stance (P-9).<br>&nbsp;&nbsp;– **The PUT alias** maps the old body: `changeReason` → the reason; a new optional `legalReference`; `effectiveFrom` → its calendar date; `effectiveTo` and `isActive` ignored. It answers 202 with `requestId` and `state`, and keeps its scope check.<br>&nbsp;&nbsp;– This changes the behaviour of a Tier-0 route: record it in `PAYROLL_SALARY_CHANGE.md` and in the report, under the changes for API clients.<br>• **The screens:**<br>&nbsp;&nbsp;– `PayProfileChangesController` «طلبات تغيير الراتب»: a list filtered by state and employee («بانتظار الاعتماد» for approvers; a scheduled request with `LastApplyError` stands out); details comparing current and proposed, with the decision history; the actions the user's policies allow, with `data-hr-confirm` on reject and cancel;<br>&nbsp;&nbsp;– `EmployeePayProfiles/Create` becomes the request form, with `PayProfileChangeRequestViewModel` (an allowlist of fields); the profile's page links to its requests;<br>&nbsp;&nbsp;– the payroll dashboard shows the pending and the blocked counts (through the scope, P-9), and the menu shows each link to those whose policy allows it;<br>&nbsp;&nbsp;– every view meets the guard (PLAN-67), and works under the enforced CSP.<br>• **Delete** `UpsertEmployeePayProfileCommand` and its handler.<br>• **The events:** the three contracts in `MessageContracts/Events/`, in `IContractActivated`'s pattern with `int SchemaVersion => 1`, and the plan's fields (the submitted event carries no amount).<br>&nbsp;&nbsp;– Published through the outbox before the one save, as the Payroll publisher found in 0.4.<br>&nbsp;&nbsp;– Rows in `EVENT_PUBLISHER_INVENTORY.md` (the SingleSave pattern, R4) and `MESSAGE_EVENT_MATRIX.md` (publisher Payroll; no consumer yet), and entries in `docs/rel-compat/event-schema-inventory.json` (additive).<br>• **No log line writes an amount, a reason or a legal reference.**<br>• **`PayProfileWritersTests`** (architecture): in `Payroll.Web` and `Payroll.Application`, only the applier and the named consumers (contract activation at two places, the transfer in place, employee creation) add, change or remove a pay profile, each named with its owner (PLAN-55, PAY-G). A new writer fails it. Any other writer the inventory finds stays, named with its owner, and goes into the report.<br>• **A limit that this plan does not fix:** a run takes the active profile only and prorates by its dates (`PayrollRunJobHandlers.cs:90-95`, `EmployeePayProfileRules.cs:10-26`). So a month calculated after a later month's change has applied — October calculated on 2 November, after a change from 1 November — reads the newer version, whose dates leave October no day.<br>&nbsp;&nbsp;– Reproduce it once (a scratch test you do not commit, or a calculation from the rules).<br>&nbsp;&nbsp;– Write it in `PAYROLL_SALARY_CHANGE.md` («حدود معروفة») and in the report, as a defect for PLAN-56.<br>&nbsp;&nbsp;– Do not change the run engine: the plan keeps it as it is (§2, rule 2). |
| P-6 | **Dates and months.**<br>• **«Today» and «the current month» are Damascus dates:** from an injected `TimeProvider` and `Localization:TimeZone` (PLAN-67's setting), through the zone helper the views use. Never `DateTime.UtcNow.Date` for these rules.<br>• `EffectiveFrom` is a `DateOnly`. The profile's `DateTime` fields get that date at 00:00 UTC (PLAN-67's rule for calendar dates), and the previous version ends the day before.<br>• **At submit** (D-53-1): `EffectiveFrom` is the first day of a month, and not before the current month.<br>• **At approval and at activation,** «a month whose run is frozen, approved or posted» means a run whose period contains `EffectiveFrom`, that covers this employee, and that is frozen (`ResultsFrozen`), Approved or Posted, or a Regular run being calculated or validated. Reversed and Cancelled runs do not count.<br>&nbsp;&nbsp;– A Regular run covers every employee; a run of another type covers the employees on its salary lines.<br>&nbsp;&nbsp;– Why: a change contradicts only a frozen result that holds this employee. And the acceptance seed holds frozen FinalSettlement and Correction runs with no lines in November and December 2026 («The code today»): a broader rule would refuse every change of those months, and AC-IAM-53a with them.<br>&nbsp;&nbsp;– The refusal is a 409 with an Arabic message naming the month; the state does not change. Record this reading in the report and in `PAYROLL_SALARY_CHANGE.md`.<br>• **Approve:** `Applied` if `EffectiveFrom` is on or before today, otherwise `Scheduled`.<br>• **Tests:** the last day of a month after 21:00 UTC (already the first in Damascus), the first of a month at 00:30 Damascus time, December to January, February; and each run kind of the rule above (Regular frozen, Regular calculating, another type with and without the employee's line, Reversed). |
| P-7 | **The activation job.**<br>• **`PayProfileChangeActivationJob`,** a `BackgroundService` in `Payroll.Infrastructure`, registered by Payroll.Web.<br>&nbsp;&nbsp;– It runs every `Payroll:SalaryChange:Activation:IntervalMinutes` (default 60), after a staggered first pass, as the repository's other jobs do.<br>&nbsp;&nbsp;– The plan's `ActivationCron` is replaced: the repository has no cron library, and this plan adds none. A deviation; record it.<br>• **One replica runs a cycle:** `AdvisoryLeaderElection.TryRunAsLeaderAsync`, with a new key `AdvisoryLockKeys.PayProfileChangeActivation = 4_001_013`.<br>• **The tenants:** found as `AssetApprovalStartRetryJob.FindTenantsAsync` does, from the audit log rows of `PayProfileChangeRequest`. First prove that Payroll's audit writes those rows in the same save. Each tenant runs in its own scope with `TenantIdFlow`; one tenant's failure is logged and does not hide the others.<br>• **Each due `Scheduled` request** (`EffectiveFrom` on or before today) goes through the applier, as the system actor («النظام»).<br>&nbsp;&nbsp;– A refusal (a frozen month, the bounds) leaves it `Scheduled` with an Arabic `LastApplyError`. The gauge `payroll_salary_change_apply_blocked` counts them per tenant (`AssetApprovalMetrics`' pattern), and the list and the dashboard show them.<br>• **Idempotent:** the state check and `xmin`; two cycles apply a request once.<br>• **`POST api/payroll/pay-profile-changes/activate-due`** (`Payroll.Approve`) runs the same code for the caller's tenant, and returns the counts.<br>• **`Iam053ActivationJobTests`,** with a controlled clock: a due request is applied once, even when the job runs twice; a request not yet due is untouched; a blocked one stays `Scheduled` with its error and the gauge; another tenant's request is applied in its own tenant only. |
| P-8 | **Scales and grades by request (plan §4).**<br>• **`PayrollReferenceChangeRequest`,** with the plan's fields; its enums with Arabic `[Display]`. `ProposedJson` and `CurrentJson` come from the allowlisted fields of the view models.<br>• **The commands** in `Features/ReferenceChanges/`:<br>&nbsp;&nbsp;– submit (`Payroll.MasterData`; reason and legal reference mandatory);<br>&nbsp;&nbsp;– decide (`Payroll.Approve`, not the requester; the change is applied in the approval's own save);<br>&nbsp;&nbsp;– cancel (the requester or an approver).<br>• **The controllers:** `SalaryScales` and `PayGrades` Create/Edit/Delete create requests, with `SalaryScaleChangeViewModel` and `PayGradeChangeViewModel`. The in-place edits go. A details page shows the open request on its record.<br>• **Validation,** at submit and again at approval: minimum ≤ base ≤ maximum; the scale exists; the codes are unique (a unique violation answers an Arabic 409, never a 500).<br>• **No delete of what is in use:** a grade that any pay profile (active or closed) or an open salary change request names, or a scale that has grades, is refused at submit and at approval, with an Arabic message; the record stays.<br>&nbsp;&nbsp;– The migration turns the profile's grade relation from `SetNull` to `Restrict` (plan).<br>&nbsp;&nbsp;– The scale's grades relation is `Cascade`: deleting a scale deletes its grades. The same migration turns it to `Restrict` — the plan's rule one level up; record it. First find who deletes scales or grades today (seeders, tests).<br>• **«طلبات البيانات المرجعية»** (`PayrollReferenceChangesController`, `[OrgScopeNotApplicable("…")]`): a list; details as a readable before-and-after table with Arabic field names, the JSON only inside `_TechnicalJson`; approve, reject (a note is mandatory) and cancel.<br>• **`Payroll:MasterData:RequireApproval`** is true. False is honoured in Development only; elsewhere it is ignored, with a warning at start. When false, the request is applied at submit by the same code.<br>• **Rule 1:** new bounds change no profile. A profile outside them shows in the existing exceptions report: find that report, and prove it in a test. HR.Core's offer check reads approved values only.<br>• **`Iam053ReferenceChangeTests`** (unit): create, update and delete for both types; deciding one's own request refused; the delete protection; the setting honoured in Development only. |
| P-9 | **The branch scope (plan §2, rule 3; note 3).**<br>• **Wire Payroll:**<br>&nbsp;&nbsp;– `PayrollOrgScopeKeys` in `Payroll.Infrastructure/OrgScope`, in `MovementsOrgScopeKeys`' pattern: keys on `PayProfileChangeRequest.OrgUnitId` and `EmployeePayProfile.OrgUnitId`, and every id parameter of a scoped action declared (a key, an id, or not a key with its reason);<br>&nbsp;&nbsp;– `.AddOrgScopeFilter<PayrollDbContext>(…)` chained at `Program.cs:96-97`, and `AddSnapshotOrgScopeProvider<PayrollDbContext>()`. Payroll has `OrgUnitSnapshots`: check that the provider reads them.<br>• **The stances:**<br>&nbsp;&nbsp;– `PayProfileChangesController` and the new API controller: `[OrgScoped(typeof(PayProfileChangeRequest))]`;<br>&nbsp;&nbsp;– `EmployeePayProfilesController`: `[OrgScoped(typeof(EmployeePayProfile))]`; its `IAM-H` row leaves;<br>&nbsp;&nbsp;– `PayrollReferenceChangesController`: `[OrgScopeNotApplicable("…")]`, since scales and grades belong to the agency;<br>&nbsp;&nbsp;– the other 23 Payroll rows stay (`IAM-H`).<br>• **Submit** names an employee, and Payroll has no employee snapshot. The handler checks the active profile's unit against the caller's scope, as the PUT does today, and answers 404 outside it. The request copies the unit. A profile with no unit follows what the registry does with an empty key, the same way in submit, lists and details.<br>• **Lists** go through `WhereInOrgScope`; details and actions by id go through the filter (404 outside).<br>• **Proof:** HTTP tests in the pattern of `Iam052ScopeHttp`. A restricted payroll principal submits, lists, opens and decides in his unit, and finds another unit's requests absent and answering 404; a central user sees every unit.<br>• **No new acceptance persona:** the plan has none, and a branch-restricted payroll role is wave 2. |
| P-10 | **Payroll runs in acceptance (PLAN-54's step 0.7, moved here).**<br>• **Why here:** AC-IAM-53a calculates a run and AC-IAM-53d needs it frozen, and the plan's definition of done wants AC-12, AC-23 and AC-25 green. Today every Regular run of the acceptance seed is refused («The code today»).<br>• **Measure first,** on the lab stack you start in P5: per agency, the current employment records without a regime, and the open regime exceptions by type.<br>• **`AcceptancePayrollRegimePatch`** in `HR.Core.Infrastructure/Seeding`, called by `AcceptanceSeedOrchestrator` (the Acceptance profile only, never the demo):<br>&nbsp;&nbsp;– idempotent; each agency in its own tenant (`TenantIdFlow`), as the other patches;<br>&nbsp;&nbsp;– it sets `law50` on the current records without a regime;<br>&nbsp;&nbsp;– it resolves their open `UnclassifiedRegime` exceptions through `ServiceRegimeExceptionService.ResolveAsync`, or exactly the fields it sets, with an Arabic resolution («تصنيف بذرة القبول: القانون 50/2004»). It never deletes one. `ResolveAsync` applies the regime through the transition service itself: never apply it twice. If that path publishes integration events, set exactly the fields it sets without publishing, and record why;<br>&nbsp;&nbsp;– every other exception type stays. If one still blocks agency A's runs: no class reads it → resolve it the same way, and record it; a class reads it → `BLOCKED:` with the class and the counts;<br>&nbsp;&nbsp;– a patch runs at every start, so it needs no new seed version.<br>• **Before you commit it,** every acceptance class that reads or sets a regime keeps its meaning: `AcHcm29`, `AcS2s42`, `AcMsg40`, `AcIam50`, `AcInt37` (its `EmployeeAsync(s, "law50")` now finds the BR1 head first), `AcQa34`, and any other your search finds.<br>• **Prove it:** a Regular run for agency A on the lab stack passes the regime gate.<br>• **The other gates** — `PayrollNationalScopeGuard` (the prod-like stack hard-enforces the national ceilings), `PayrollLegalControlGuard` — are product rules. If one refuses AC-IAM-53a's run, find why; never loosen one to pass a test: `BLOCKED:` if the run cannot pass otherwise.<br>• **Then:** a line under PLAN-54's 0.7 («نُفّذت في PLAN-53»), and the status of «PLAN-58/59: payroll on the lab seed (AC-23..25)» in `PROGRAM_COMPLETION_ROADMAP.md`. |
| P-11 | **Existing tests that the new rules change.** Only these may change, and never weakened:<br>• **`Pay023FrozenRunIsolationTests`** (integration): after the freeze, change the profile through `ApplySystemChangeAsync`, and prove the snapshot isolates the run exactly as before. Add the manual path: an approval in the frozen month is refused.<br>• **`Ac23PayrollFreezeTests`** (lab, February 2027): the processor requests a raise from 2027-02-01 inside the grade's bounds; the approver's approval is refused (a frozen month); the state stays PendingApproval; recalculation is refused, and the net is unchanged. Its meaning — a frozen run is isolated from a pay change — holds, proven twice. It now needs `payroll-approver`.<br>• **`Ac25FraudHoldTests`** (lab): its fixture is a profile from 2027-05-01 for a May 2027 run. After this plan an approved future change is `Scheduled`, and the May run would not see it.<br>&nbsp;&nbsp;– Move the fixture and the run to the current month (Damascus): the change from its first day, approved → `Applied`; the run for the current month.<br>&nbsp;&nbsp;– Check that no other lab class uses the current month, and keep every assertion.<br>• **AC-12 and AC-24** do not change; they must pass with the patch. If AC-24 fails for a cause outside this plan, record it with the log and its owner.<br>• **Required:** once AC-12, AC-23 and AC-25 pass on your lab stack, set them `required: true` in `ci-scope.json`, and AC-24 too if it passes — the plan's definition of done, made durable. Update whatever baseline the meta classes keep of the required set. AC-20 and AC-21 do not change.<br>• **Personas:** the `scenarios` gain AC-IAM-53 for `payroll-processor`, `payroll-approver`, `a-payroll-poster` and `b-payroll-approver`, and AC-23 for `payroll-approver`.<br>• **`DATA-PAYPROFILE`** (1489): check that it still counts the seed as before. |
| P-12 | **The migration `PAY053_SalaryChangeRequests`.**<br>• **One migration** in `Payroll.Infrastructure`:<br>&nbsp;&nbsp;– the two tables, with `TenantId` and their indexes, the filtered unique one among them;<br>&nbsp;&nbsp;– the two relation changes of P-8;<br>&nbsp;&nbsp;– row security on the new tables through `TenantRlsMigrationHelper`, and the grants, as Payroll's latest migrations give them.<br>• **No data change,** so no `FORCE` to lift; no byte order mark; `dotnet ef migrations has-pending-model-changes -p src/Services/Payroll/Payroll.Infrastructure -s src/Services/Payroll/Payroll.Web` reports none.<br>• **Prove row security as a non-superuser** (the role the leastpriv stack uses): agency B sees no row of agency A. The leastpriv stage passes with the migration.<br>• **`Iam053PayProfileChangePostgreSqlTests`** (the plan's four cases) on PostgreSQL through `PostgreSqlFixture`; make sure CI's Testcontainers filter picks it up. |
| P-13 | **Acceptance placement (PLAN-41's rules).**<br>• **`AcIam53SalaryChangeTests`** (AC-IAM-53a..e): `prodLike[]`, `required: true`, group `main`; `[RequiredAcceptance]` and `EvidenceRecorder("AC-IAM-53")`.<br>&nbsp;&nbsp;– It must not stop or recreate containers, and it uses the stack's defaults. The plan's `--force-recreate payroll-web` line is a local convenience, not a test step.<br>&nbsp;&nbsp;– Its steps run in order (53a before 53d), as `AcIam52`'s do.<br>&nbsp;&nbsp;– Its dates are Damascus dates (P-6), so a run near midnight UTC at a month's end does not flake.<br>&nbsp;&nbsp;– Its employees are ones no other prod-like class touches (search the classes for the ids they use).<br>• **53a:** an agency-A employee with an active profile and a grade whose bounds allow the raise. `before.json` records that the current month has no run covering him (P-6). Then:<br>&nbsp;&nbsp;– request → PendingApproval, the profile unchanged; approve → `Applied`;<br>&nbsp;&nbsp;– the new version carries the unit, the regime and the bank fields; a history row exists;<br>&nbsp;&nbsp;– payroll_db's `OutboxMessage` sequence advanced, and no row is left after delivery (AcMsg40's method);<br>&nbsp;&nbsp;– a Regular run for the current month (create, calculate, validate → frozen) holds the new salary on his line, for the whole month.<br>&nbsp;&nbsp;– Negative: the processor approving his own request → 403, and the state unchanged.<br>• **53b..53e** as the plan writes them. D-53-3 counts `Scheduled` as open: use different employees, or cancel between steps.<br>• **A rerun on the same stack** finds the month frozen: its precondition fails with a clear message, and a rerun needs a fresh stack. Record it.<br>• **The negative form POSTs:** prove that authorization answers before the antiforgery check (403, not 400), or take the token from a page the user can open (PLAN-44).<br>• **`ac-ownership.json`:** `AC-IAM-53` (owner PLAN-53, `provesClaim: true`, and its reason).<br>• **The evidence of record is the PR run's artifact,** not `acceptance-evidence/AC-IAM-53/`. Copy what the report needs into `artifacts/plan53/`, then restore the committed evidence. |
| P-14 | **Local testing (owner's rule of 2026-11-06).** Run only the classes this plan touches, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`, and the overlay groups with `-Stage prodLike:<group>`. **The full suite runs once, in CI.**<br>• **prod-like:** `AcIam53SalaryChangeTests`, `AcIam50ApproverReachDelegationTests`, `AcIam51ExplicitPolicyTests`, `AcIam52BranchScopeTests`, `AcUx67ReadableInterfaceTests`, `AcHcm25ProductionApprovalTests`, the prod-like journey, and the `isolated` group (`AcInt37`, `AcMsg40`, `AcWf44`, `AcWf45`, `AcWf46`, `AcPol47`, `AcOps39`);<br>• **lab:** `Ac12PayrollSodTests`, `Ac23PayrollFreezeTests`, `Ac24PartialDisbursementTests`, `Ac25FraudHoldTests`, `AcHcm29HireContractTests`, the lab journey, and every class P-10 names;<br>• **overlays:** the `s2s42` stage (`AcS2s42TrustBoundaryTests` sets a regime);<br>• **leastpriv:** `AcOps38FirstBootTests` and `AcTen48LeastPrivilegeTests` (the new migration runs there);<br>• **E2E:** in CI only (Lessons).<br>Then compare the seeded counts of `scripts/acceptance-verify.ps1` by psql. Known local-only failures of lab classes that need PowerShell 7 (PLAN-43 §6) are recorded, not chased. |
| P-15 | **PowerShell 5.1.**<br>• Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. The plan's `pwsh scripts/iam053-validate.ps1` becomes that.<br>• **`scripts/iam053-validate.ps1`** has a `-SelfTest` with planted defects, as `iam051-validate.ps1` does. It checks, at least:<br>&nbsp;&nbsp;– no write-policy list, and no `PLAN-53` in `CeilingList.Owners`;<br>&nbsp;&nbsp;– no `UpsertEmployeePayProfile` anywhere;<br>&nbsp;&nbsp;– the `Payroll:SalaryChange:*` and `Payroll:MasterData:*` keys and the `Payroll.MasterData` roles in Payroll's settings, mirrored in the prod-like compose file;<br>&nbsp;&nbsp;– the migration has no byte order mark, and holds the filtered index and both `Restrict` relations;<br>&nbsp;&nbsp;– the three events in `EVENT_PUBLISHER_INVENTORY.md`, `MESSAGE_EVENT_MATRIX.md` and `event-schema-inventory.json`;<br>&nbsp;&nbsp;– the regime patch in `AcceptanceSeedOrchestrator`;<br>&nbsp;&nbsp;– `AcIam53SalaryChangeTests` required in `prodLike[]`, and AC-12, AC-23 and AC-25 required.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile <ProdLike or Lab>`. |
| P-16 | **The demo server facts this plan must respect.**<br>• **It runs Production in demo mode** (`Deployment__Mode=demo`), so `hradmin` keeps the payroll policies (AC-IAM-51's d-iam-4).<br>• **The users** `e2e-payroll-processor` and `e2e-payroll-approver` are in the realm manifest. P0 checks that both sign in, read-only; a user missing from the live realm is added through `scripts/vps/add-demo-user.sh`.<br>• **P0's read-only picture:**<br>&nbsp;&nbsp;– the running tag and the free disk;<br>&nbsp;&nbsp;– active profiles, grades in use, scales, and runs per status and month;<br>&nbsp;&nbsp;– per tenant, the current records without a regime and the open regime exceptions: would a Regular run be refused on the demo? A finding for the report. No run and no regime change on the demo.<br>• **What the owner will see change:**<br>&nbsp;&nbsp;– a user with `payroll` alone can no longer edit pay types, grades or scales;<br>&nbsp;&nbsp;– a salary change goes through «طلبات تغيير الراتب» and another person's approval;<br>&nbsp;&nbsp;– a scale or grade change goes through «طلبات البيانات المرجعية»;<br>&nbsp;&nbsp;– a grade in use cannot be deleted. |
| P-17 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan53-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Before the deploy, read-only:** P-16's picture.<br>• **The disk (PLAN-67):** at least 30 GB free before the build. Otherwise remove the server's build cache and the local images of older releases. They stay in the loopback registry `127.0.0.1:5050` (undo: `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>`). Record each removal.<br>• **Build once,** on the final code, detached; then `scripts/vps/deploy.ps1 -Tag <sha12>`. The migration runs at start-up.<br>• **The demo's pay data does not change:** every request the server checks open ends `Cancelled` or `Rejected`, and no run is created.<br>• **Rollback safety:** find out what PLAN-67's tag does with this plan's data, and record it in `OPS_VPS_SINGLE_HOST.md`:<br>&nbsp;&nbsp;– it never reads the two new tables;<br>&nbsp;&nbsp;– the `Restrict` relations turn its silent delete of a used grade into an error;<br>&nbsp;&nbsp;– its direct salary write comes back;<br>&nbsp;&nbsp;– scheduled requests wait for this plan's job.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn:<br>&nbsp;&nbsp;– roll back to PLAN-67's tag, or fix forward;<br>&nbsp;&nbsp;– restore the backup only if data was damaged;<br>&nbsp;&nbsp;– prove the demo works, and record the downtime. |
| P-18 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P9, after local CI and the pre-push check are green, in the same turn as the pre-push check (Lessons).<br>2. `gh pr create`, then wait («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, run the unit suite and the format check, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>`, and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-19 | **No regression of PLAN-25, PLAN-36..52, PLAN-66 and PLAN-67.** These stay green:<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests`;<br>• `AcIam50`, `AcIam51`, `AcIam52`, `AcUx67`, `AcWf43`..`AcWf46` with the orphan class, `AcPol47`, both `AcTen48` classes, both `AcPol49` classes, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, `AcHcm29`, and both recruitment journeys;<br>• the architecture tests of PLAN-51, PLAN-52 and PLAN-67 (`OrgScopeCoverageTests`, `InterfaceTextGuardTests`, `GuardSelfTests`), `OutboxPublishOrderingArchitectureTests`, `Rel004CompatibilityTests`, `GatewayPathAuthorizationMiddlewareTests`, `ProductionWiringMatrixTests`, `CiStepsParityTests`, and the payroll unit tests (the plan's filters `Pay0` and `PayrollEngine`);<br>• `scripts/iam052-validate.ps1`, `scripts/iam051-validate.ps1`, `scripts/iam050-validate.ps1`, `scripts/ux067-validate.ps1`, `scripts/pol047-validate.ps1`, `scripts/pol049-validate.ps1`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-54:** the policy and approval of manual inputs (allowances, deductions, adjustments); statutory deductions and holidays inside the framework version. The rest of G-PAY-10.
- **PLAN-55:** the bank account entity and its checker; moving the contract activation and employee creation consumers onto the applier (and with that, the boundary day of `InboundEventHandlers.cs:69-70`).
- **PLAN-56:** the run's state machine and freezing. A request reads the run's state as it is. P-5's known limit is reported to it.
- **Wave 2:** PAY-G (salary changes from transfers and promotions, the in-place transfer write), PAY-H (retroactivity and proration inside a month), WF-F (delegating `payroll-approver`), IAM-F (view models for the other payroll controllers), IAM-H (the scope of the other payroll screens).

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- another plan's untracked prompt file;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- a screenshot or any other image;
- any secret, token or password (templates keep their placeholders, `__SVC_SECRET__` or `${…}`).

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- touch the side PRs (#40, #42) or their branches;
- disable host-key checking;
- widen a policy, a scope or a role to make a page or a test work, or give the `payroll` role alone any write;
- add a row to any ceiling list: they only shrink;
- change the run engine's profile selection, a consumer's behaviour, or a payroll guard;
- delete a pay profile, a request, a regime exception or demo data, or change pay data on the demo;
- write an amount, a reason or a legal reference into a log;
- print a password, or rotate a secret that exists on the server;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** (port 22 at P0 excepted: Preconditions 4).
- **A payroll guard refuses AC-IAM-53a's run** and the run cannot pass without loosening it, **or a regime blocker that a class reads** still blocks agency A (P-10).
- **The plan would need a consumer or the run engine changed** to work (Scope boundaries).
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning.
- **The space for Docker's disk stays below 15 GB, or a host port the tests read is reserved** (P-2).
- **Port 22 is still blocked when P8 starts** (P-2): one line asking the owner to turn ProtonVPN on.
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, keep-awake, preconditions, baseline

1. Write the `Started (UTC)` line (see «Timing»). Start the keep-awake process (P-2). Check the Preconditions, and SSH beside Testcontainers (P-2).
2. `git switch -c feature/plan-53-iam-payroll-salary-change origin/main`. Bring in PLAN-67's merge note, and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan53-baseline.md` (P-3): 0.2, 0.3, 0.4, the payroll gate read from the code, and the demo's picture (P-16).
4. If port 22 is blocked, record it, and take the demo's picture at the start of P8.

Commit: `chore(iam053): baseline, the payroll writes and pay profile writers, the payroll gate in acceptance, the demo's payroll picture`.

## P1 — Explicit policies, the empty list (plan §1, P-4)

- **`Payroll.MasterData`,** its settings and its mirror; the sixteen attributes; the cancel policy.
- **The lists and the guards:** the Payroll list gone, `PLAN-53` out of the owners, the planted rows and `iam051-validate.ps1` updated.
- **Tests:**
  - `PayrollWriteActionsTests`;
  - per action: a user with `payroll` alone is refused each of the sixteen; the processor, the approver and the poster each get exactly what the mapping gives them; Admin; the `Payroll.MasterData` roles read from configuration;
  - `GuardSelfTests`; `iam051-validate.ps1` and its `-SelfTest`; `ux067-validate.ps1 -SelfTest`.

Commit: `feat(iam053): every payroll write names its policy; Payroll.MasterData; Payroll's ceiling list is empty and PLAN-53 leaves the guard's owners`.

## P2 — The requests, the versions, the migration (plan §2 and §4 entities, P-12)

- **The two entities and their enums,** their configuration, the filtered index, the two relations, and `PAY053_SalaryChangeRequests`.
- **`EmployeePayProfileVersioning`,** with its copy-forward and `Iam053ApplierCopyForwardTests`.
- **Row security** proved as a non-superuser.

Commit: `feat(iam053): salary and reference change requests, versions that carry every field, and migration PAY053 with row security and restricted deletes of grades and scales`.

## P3 — The salary change transaction (plan §2, §3, P-5, P-6, P-7, P-9)

- **The commands, the applier and `ApplySystemChangeAsync`, the events through the outbox.**
- **The API and its PUT alias; the screens, the dashboard and the menu.**
- **The scope:** Payroll's keys, the filter and the stances.
- **The activation job,** its endpoint and its gauge.
- **`UpsertEmployeePayProfileCommand` deleted;** `PayProfileWritersTests`.
- **The contract written down:** `docs/PAYROLL_SALARY_CHANGE.md`, and the event rows in the two inventories and the schema inventory.
- **Tests:**
  - `Iam053SalaryChangeStateMachineTests`: every transition, and every refusal with the state unchanged;
  - `ApplySystemChangeAsync` for each automatic source, with `Superseded`;
  - P-6's dates and runs;
  - `Iam053PayProfileChangePostgreSqlTests` (four cases), `Iam053ActivationJobTests`, the scope's HTTP tests;
  - `OutboxPublishOrderingArchitectureTests`, `Rel004CompatibilityTests`, `InterfaceTextGuardTests`, `OrgScopeCoverageTests`.

Commit: `feat(iam053): a salary change is requested by a processor and approved by another person, applied by one applier that carries every field, scheduled by month, scoped by branch, and published through the outbox`.

## P4 — Scales and grades by request (plan §4, P-8)

- **The commands, the two controllers' requests, «طلبات البيانات المرجعية», the delete protection, the setting.**
- **Tests:** `Iam053ReferenceChangeTests`; the exceptions report shows a profile outside new bounds; the scope stance; the guard.

Commit: `feat(iam053): salary scales and grades change by a request another person approves; a grade in use or a scale with grades cannot be deleted`.

## P5 — Payroll in acceptance, and the existing tests (P-10, P-11)

1. **The lab stack:** check the disk (P-2), reset it (the personas change), build, and start it.
2. **Measure** the regime blockers (P-10). Write the patch, check the classes that read regimes, and prove that a Regular run for agency A passes the gate.
3. **The existing tests:** `Pay023FrozenRunIsolationTests`, AC-23, AC-25, the personas' scenarios.
4. **Run** AC-12, AC-23, AC-24, AC-25, `AcHcm29` and the lab journey. Once AC-12, AC-23 and AC-25 pass, set them required (P-11). `down -v`.

Commit: `test(iam053): acceptance payroll runs pass the service-regime gate; AC-23, AC-25 and Pay023 use the request and its approval; AC-12, AC-23 and AC-25 required`.

## P6 — Acceptance, then the independent review (plan Acceptance, P-13, P-14)

1. **The pieces:** `AcIam53SalaryChangeTests`, `ci-scope.json`, `ac-ownership.json`, `scripts/iam053-validate.ps1`.
2. **Acceptance.**
   1. Check the disk (P-2). Start the prod-like stack (`acceptance-up.ps1 -Profile ProdLike`), and record the time to ready.
   2. Run `AcIam53SalaryChangeTests` (AC-IAM-53a..e), positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   3. Then P-14's lists, `scripts/iam053-validate.ps1` (and `-SelfTest`), and the seeded counts by psql. All required classes must pass.
   4. `down -v`. Restore the committed evidence.
3. **The full Testcontainers step on the host,** with CI's filter and the VPN off (P-2).
4. **Independent review** (one fresh subagent), before the server, so the single build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - a payroll write, old or new, that `payroll` alone can reach, or a policy wider than the mapping;
   - a path in a form, the API, a job or a seeder that changes a salary or a grade without a request another person approved, other than the named consumers;
   - a field the applier drops, a version that overlaps or leaves a gap, two active profiles, a boundary day counted twice;
   - a state that changes on a refusal, a refusal that answers 500, a concurrent decision both applied;
   - a request or a count visible outside the caller's branch or agency;
   - an event published outside the one save, an amount in the submitted event, an amount, a reason or a reference in a log;
   - a month rule computed in UTC, or a run kind P-6 misses;
   - the activation job applying twice, missing a tenant, or applying in the wrong tenant;
   - a grade or scale deletable while in use;
   - a test changed to pass rather than to keep its meaning, or a part of the plan deferred, skipped or listed to save time;
   - something PLAN-67's tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(iam053): AC-IAM-53a..e pass on the prod-like stack; review fixes; CI scope and ownership updated`.

## P7 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to `PayrollDbContext` after yours, remove yours and add it again on top. Rebuild, and rerun the unit, architecture and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you.

Commit (only if the merge needed fixes): `fix(iam053): main merged, conflicts resolved`.

## P8 — The demo server (P-16, P-17)

Record every server change and its undo command **as you make it**.

1. Check SSH (P-2) and P-17's condition.
2. Take the backup, then **verify** it. Take the read-only picture (P-16), if P0 could not.
3. **Check the server's disk, build and deploy.** Run the build once, detached, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`. If the deploy stops at `keycloak-reconcile` after PostgreSQL restarts, run the same command again (PLAN-40 incident). Record the downtime and the time to all healthy.
4. **Then check**, with evidence in `artifacts/plan53/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them. The new checks go in an `AcIam53VpsChecks` class; each one writes realistic Arabic reasons and legal references, keeps its marker in `SourceReference`, and leaves nothing open.
   1. **The policies:** as `e2e-payroll-approver`, `EmployeePayProfiles/Create`, `DeductionTypes/Edit` and `PayGrades/Delete` answer 403, and no row is written.
   2. **A salary change:** as `e2e-payroll-processor`, a request from the first day of next month (Damascus) for a demo employee, inside the bounds → PendingApproval; the processor's own approval → 403; `e2e-payroll-approver` approves → `Scheduled`, the active profile unchanged, the outbox sequence advanced; then cancelled → `Cancelled`.
   3. **A reference change:** the processor requests a grade's new maximum; the approver rejects it with a note → `Rejected`; the grade unchanged.
   4. **A grade in use** cannot be deleted; it stays.
   5. **The screens** open in Arabic under the enforced CSP with no violation (PLAN-67's method), with names, not ids.
   6. **After the checks:** no request they opened is still open, and the active profiles' count and a checksum of their salaries match the picture before (print the checksum, never an amount).
   7. **Everyone else is unchanged:** the PLAN-36..67 server checks pass, and `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass. The AC-WF-46 sweeps check is rerun alone after a sweep cycle.
5. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-17 says, prove the demo works, and record it.

Commit: `test(iam053): demo server with payroll write policies and salary and reference change requests; PLAN-36..67 server checks still green`.

## P9 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/PAYROLL_SALARY_CHANGE.md` (new): the contract — the entity, the states and transitions, the HTTP answers, the events, the applier, the activation job, the settings, the PUT alias, P-6's reading of D-53-1, the known limit (P-5), and how PLAN-54..59 extend it;
   - `docs/MESSAGE_EVENT_MATRIX.md`, `docs/EVENT_PUBLISHER_INVENTORY.md`, `docs/rel-compat/event-schema-inventory.json`;
   - `docs/PAY_001_APPROVAL_BYPASS.md`, `docs/SOD_MATRIX.md` (the salary change and the reference changes), and `docs/PRV_001_DPIA_PAYROLL.md` if it lists where salary amounts flow;
   - the «الحالة» lines: G-IAM-02 and the payroll part of G-IAM-08 in `docs/IAM_001_SCOPE_GAP_STUDY.md`; G-PAY-10 in `docs/PAY_001_GAP_STUDY.md` («جزئي: السياسات والراتب — PLAN-53»);
   - PLAN-54's 0.7 line, and `docs/PROGRAM_COMPLETION_ROADMAP.md` (P-10);
   - `docs/CI_AND_TESTING.md`: `PayrollWriteActionsTests`, `PayProfileWritersTests`, and the payroll classes now required;
   - `OPS_VPS_SINGLE_HOST.md`: what the demo users see, the backup location, the change table, the rollback note;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local gates, before any push:**
   - the full Testcontainers step on the host, with CI's filter (P6, if nothing it builds or tests changed since; otherwise again, with the VPN off);
   - `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD (with `-SkipTestcontainers` when the VPN is on);
   - then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`.

   All must be green. The report is committed before them, because a commit after a green local run needs another run.
3. **`reports/plan53-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations (the interval instead of the cron string, P-6's reading, the scale relation). No deviation defers part of the plan.
   4. **The policies, before and after:** each of the sixteen writes with its old and new guard; every new action with its policy.
   5. **The salary change:** the states and transitions as built, the HTTP answers, the applier's copied fields, the job's results.
   6. **Scales and grades:** the requests, the delete protection, the relations.
   7. **The scope:** the stances, the keys, the rows that left the lists.
   8. **Payroll in acceptance:** the blockers measured per agency, what the patch changed, and the run that passed the gate.
   9. **The existing tests that changed,** each with the meaning it keeps.
   10. The guards and their own negative tests.
   11. Unit, architecture, integration and acceptance results; the PR run's artifact link as the evidence of record.
   12. **The server:** before and after, changes and their undo, the backup location, the rollback note, and whether a Regular run would be refused on the demo (P-16).
   13. **Changes for API clients:** the PUT alias.
   14. Items that other plans own (Scope boundaries), and defects for later plans (P-5's limit for PLAN-56 among them).
   15. **Timing:**
       - total wall-clock;
       - active time, waiting for CI, and every stop with its cause;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 67`.
   16. **A readiness note** for PLAN-54..59: the contract they extend, the names they take, the applier's entry for the consumers (PLAN-55), and the run limit (PLAN-56).
4. **Merge (P-18), by you, in the same turn as the pre-push check:**
   1. Merge the feature branch into a new `release/plan-53` from `main` (`--no-ff`).
   2. Write `reports/plan53-pr.md` (short).
   3. `git push -u origin release/plan-53`
   4. `gh pr create --base main --head release/plan-53 --title "PLAN-53: payroll write policies, and a salary change approved by another person" --body-file reports/plan53-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then:
   - `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`;
   - record the PR link, each check result, the merge commit and the post-merge run in `reports/plan53-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`;
   - commit that note on a local branch `docs/plan-53-merge-note`, as the plans before did, so the next plan brings it in. **Do not open a second PR for it;**
   - stop the keep-awake process (P-2).

## Definition of done

- **No payroll write works with `payroll` alone;** every write in Payroll.Web names its policy; Payroll's ceiling list is gone, and `PLAN-53` is out of the guard's owners.
- **No form, API, job or seeder path changes a salary or a grade** without a request that another person approved, except the named consumers; `UpsertEmployeePayProfileCommand` is deleted.
- **A new version carries every field** of the previous one, which closes the day before.
- **Scales and grades change by request and approval;** a grade in use or a scale with grades cannot be deleted.
- **A restricted user** sees and acts on his branch's requests only.
- **The contract** is in `docs/PAYROLL_SALARY_CHANGE.md`, and the three events are in the matrix and the inventories.
- **Payroll runs compute in acceptance,** and AC-12, AC-23 and AC-25 are green and required.
- **Nothing in the plan was deferred to save time.**
- **The tests:** AC-IAM-53a..e = Pass (the PR run's artifact is the evidence of record), and the PLAN-36..67 suites are green.
- **The server** runs this code, with its pay data unchanged.
- **The documents** P9 lists are updated.
- **The merge** is done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed for payroll users, and what the owner will see on the demo;
- what the acceptance seed's regime fix opened (the payroll classes now running and required);
- how long the plan took, how long it waited for CI, and every stop with its cause;
- the PR link;
- the path of the final report.
