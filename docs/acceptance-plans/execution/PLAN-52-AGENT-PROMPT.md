# Task: PLAN-52 — Branch scope on everything a branch manager reaches, and a reflective BOLA test (IAM-D)

The deliverables:
- **A branch manager stays inside his branch on every screen he reaches** (`branch-manager` with `hr-core`, and `admin-logistics` in logistics): all of HR.Core, the hire wizard in HR.Contracts, Admin.Logistics, and the movement approval screens that a PLAN-50 delegation opens to him.
- **A record outside his scope does not exist for him:**
  - 404 for its details and for a POST by its id;
  - absent from every list, export and pick-list;
  - nothing changes in the database.

  He gets 403 when his scope is `Denied` or the screen is central.
- **One general mechanism** — a registry of scope keys and one action filter, declared on every controller — instead of checks written by hand. The unused `OrgScopeRequirement` is gone.
- **A reflective test (BOLA v2)** fails when a controller declares no scope stance, or when an action takes an id that has no key. The regex test (v1) is gone.
- **A transferred restricted user loses his old scope** (fail-closed) until a new one is assigned, and HR.Health's employee snapshot follows the transfer.
- **The demo server** runs it, with a branch-manager demo user that shows it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P9** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder `D:\my projects\HRManagement`. There is no second clone and there are no locks.

**Complete execution** (owner, 2026-11-12, standing since PLAN-51): the owner wants every plan carried out in full, however long it takes.
- Time is never a reason to skip, defer or allowlist anything.
- Only what the plan itself assigns elsewhere stays out: its «Out of scope», and the latent controllers that P-11's lists name.
- Where the plan speaks in general, this prompt makes it concrete, and all of it is in scope:
  - **every** controller of HR.Core.Web (about 80), HR.Contracts.Web (22) and Admin.Logistics.Web (8) declares its stance, not only the ones the plan names (P-4);
  - every list, export (direct or as a job) and pick-list of a scoped controller is filtered;
  - every id an action takes is checked: route, query, form, a bound model's property, a list of ids;
  - the integration tests reach every `[OrgScoped]` controller, generated from the registry, none skipped;
  - the delegation path that PLAN-50 opened counts as reach (P-9);
  - AC-02, AC-03, AC-06 and AC-32 pass, although CI marks them not required (P-13).

**Standing owner instruction on merges** (PLAN-38 to PLAN-51 and PLAN-66): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-18).
- **Never ask the owner whether to merge:** his instruction is the permission. Asking cost PLAN-51 2 h 09 min.

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Check formatting on the files you changed (see «Lessons»).
4. Commit, using the phase's commit message.
5. Update **`reports/plan52-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan52-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan52-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Record the time spent waiting for CI, and **any time the session stood still** (a stopped turn, the usage limit, the machine asleep, a blocked port, GitHub), each with its cause.
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session in the Claude desktop app, at `xhigh`). PLAN-50 stopped once at the usage limit of the owner's plan. Economy is about tokens, never about doing less of the plan:
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only; root-causing separate failures.
- Run **one independent review**, before the server phase, so that the single server build carries its fixes. Use a refuter only for findings rated high.
- If the owner switches the session's multi-agent mode on, use it for one read-only verification of the review's fixes, two agents at a time.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan52/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.
- Build the acceptance images once per code state. Run the unit and integration filters before a stack run, and rerun only the classes that failed.

**Long commands.** A foreground command that outlives the tool's time limit ends the turn. So:
- Run anything that can outlive the limit in the background or detached, with its log under `artifacts/plan52/`, and poll it. That covers the unit suite, the Testcontainers step, an image build, a stack start, an acceptance chain, `ci-local.ps1` and the server build.
- Run **one heavy job at a time**: never an image build beside a host test run (P-2).

**Waiting — for CI and for your own work.**
- **Never end the turn to wait** for CI or for work you started (a review, a build, a long check). Wait for it in the turn, as PLAN-49 and PLAN-50 did.
- **For CI:** run `gh pr checks <n> --watch --interval 60` in the foreground, with the tool's longest time limit. When it returns because of the limit, run it again, until every check has finished. A call that returns at the limit is not a refusal: run it again. A background `gh run watch <id> --exit-status` is fine too, if this session's background commands wake it when they end.
- **Then act at once:**
  - every check green, and the four non-required classes of P-13 passed in the run → merge (P-18), with no question to the owner;
  - a red check → read its log as soon as it fails, without waiting for the other checks; fix it on the release branch, push that branch only, and wait again the same way.
- **Only if this app truly cannot keep you in the turn.** PLAN-51 ended its turn after opening the PR, believing the app would not let it poll, and then asked the owner whether to merge: the merge waited 2 h 09 min. In that case:
  1. End the turn with `WAITING-CI:` (see below), and ask nothing.
  2. On the owner's next message, whatever it says, check **once** with `gh pr checks <n>`, and read the four non-required classes of P-13 in the run.
  3. Every check green and those four passed → merge at once. A check still running → wait for it in that turn, as above.
  4. Record the path you took, with the times.
  5. Do not use GitHub's auto-merge: it would merge without the four classes of P-13, which CI does not require.
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

You are a senior .NET engineer for authorization on multi-tenant platforms:
- object-level authorization (BOLA, IDOR) in ASP.NET Core MVC: action filters, model binding, filter order;
- EF Core expression trees that must translate to SQL on SQLite and PostgreSQL;
- hierarchical org scopes (materialized paths), and snapshots of another service's data kept up to date by messages;
- architecture tests by reflection, with ceiling lists that only shrink;
- EF Core migrations on PostgreSQL under forced row-level security, with data backfills a non-superuser can run;
- idempotent MassTransit consumers, and Keycloak's admin REST API;
- Docker Compose, PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev`). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..51 did.

**Where things stand** when this prompt was written (2026-11-12):
- **`main`** = `e6a93705`. It holds PLAN-51 (PR 49).
- **PLAN-51's merge note** is a single commit, `d43d9fa6`, on the local branch `docs/plan-51-merge-note`, not yet on `main`. The main folder is checked out on `main`.
- **The demo server** runs `d0e446fe3280`, the product code of `main`: only tests and documents changed after it.
- **PLAN-51 took 19 h 51 min,** of which about 6 h 38 min were active. Its stops:
  - SSH to the server was blocked for 3 h 20 min: the owner's network blocks outbound port 22, and only ProtonVPN let it through;
  - the session stood still for 7 h 44 min after the pre-push check, until the owner pressed «Try again»;
  - it ended its turn after opening the PR, and the merge waited 2 h 09 min for the owner's word.
- **The side PRs** about the E2E Playwright install (#40, #42), if still open: never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI today:**
  - a PR run has 15 checks and took 59 min in PLAN-51: `CI` 41 min (the longest required check), `meta` 35 min, `prodlike-final` 31 min, `lab-main` 30 min, `prodlike` 29 min. One acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - the required checks on `main` are `CI`, `CodeQL`, `Gitleaks`, `E2E` and `Acceptance`;
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - `acceptance-job.ps1` takes `-Classes` and `-Stage`;
  - a new acceptance class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs. A class with `required: false` still runs, but its failure only shows and does not fail the job;
  - **the build and test steps live in `scripts/ci/steps.json`,** which CI and `ci-local.ps1` both read; `CiStepsParityTests` keeps `ci.yml` in line with it.
- **From PLAN-51** (its final report, §3, §9, §12):
  - **`src/Tests/HRManagement.ArchitectureTests`** holds `WebAssemblyCatalog` (fifteen web assemblies: fourteen services and the gateway), `ActionCatalog`, `CeilingList` and `GuardSelfTests`.
  - **A `CeilingList` row** is `Key | reason`, and its reason must start with an owner listed in `CeilingList.Owners` (today `PLAN-53` and `IAM-F`). The lists live under `tests/architecture/`, one file per service: `write-policy-allowlist/payroll.txt` (16 rows) and `entity-binding-allowlist/*.txt` (113 rows).
  - **Every write in every service names its policy,** except Payroll's 16 (PLAN-53). `[ModulePolicyOverride(policy)]` replaces the module policy for one action or controller.
  - **The gateway's own pages** answer a refusal with a 302 to `/Account/AccessDenied`, a page that does not exist (a defect for later). The gateway audits what it forwards, not its own pages.
- **From PLAN-50** (its final report, §5–§7):
  - **A delegation lets an employee act for an `hr-management-approver`:** `RoleOrDelegationRequirement` admits him to `HrManagement.Approve` and `HrMovements.ApprovalAccess`.
  - **The delegate is any employee of the directory,** matched by his token's `employee_id`.
  - **The delegation stores the principal's scope** (`PrincipalScopeOrgUnitId`) when the principal was branch-scoped. The inbox, `WorkflowActionActor`, and HR.Movements' transfers and loans apply both scopes, the delegate's own and the principal's.
- **The code today** (read on `e6a93705`). The plan's line numbers date from 2026-10-30 and have moved since PLAN-43..51: find the code by its content.
  - **The scope primitives** (`HRManagement.ServiceDefaults`):
    - `HrOrgScopeVisibility.IsBranchScoped(user)` is `branch-manager` and not Admin (D-IAM-3). `GetScopeClaimResult` reads the claim `scope_org_unit_id` and gives `NotScoped`, a valid root, or `InvalidClaim`;
    - `OrgAccessScope` is `Unrestricted`, `Restricted(ids)` or `Denied`, with `MustFilter`, `IsDenied` and `CanAccess`;
    - the resolvers: HR.Core's `OrgScopeService` (over its `OrgUnits`' `MaterializedPath`, cached), and, in the other services, `OrgScopeResolver.GetAllowedOrgUnitIdsAsync(user, units, …)` over the local `OrgUnitSnapshot`s;
    - the helpers (`OrgScopeControllerExtensions` and its neighbours): `ForbidIfOrgScopeDenied`, `ForbidIfOrgUnitOutOfScope`, `ForbidIfEmployeeOutOfScopeAsync`, `EmployeeIdsInScope`, and two `WhereInOrgScope<T>`. **They all answer 403.**
  - **The dead requirement:**
    - `OrgScopeRequirement` and its handler (`OrgScopeAuthorizationHandler.cs`) are registered by `AddOrgScopeAuthorization()` in the `Program.cs` of HR.Core and of HR.Movements, and no policy uses them;
    - `Iam002OrgScopeTests` holds five tests of the handler, `scripts/iam002-validate.ps1` requires the handler's file, and `docs/IAM_002_ORG_SCOPE.md` describes it.
  - **Recruitment:**
    - `[RecruitmentOrgScope]` (`RecruitmentOrgScopeFilter`, with `[RecruitmentScopeId]`) answers 403 for `Denied` and 404 out of scope. Its `NamedIds` are `requisitionId`, `applicationId`, `recruitmentApplicationId`, `offerId`, `sessionId` and `postingId`;
    - `PromoteFromCitizen(Guid citizenApplicationId)` and `PromoteFromInternal(Guid internalApplicationId)` (POST, `HrCore.Write`) check no scope;
    - neither does `TalentMarketplaceController`: `Index`, `Create`, `Edit(id)`, `Details(id)`, `ChangeApplicationStatus(id, vacancyId, status)`, `Delete(id)`;
    - the keys: `InternalVacancy.OrgUnitId?`, `InternalApplication.InternalVacancyId`, `CitizenApplication.PublicJobPostingId`.
  - **HR.Core's branch screens** (`OrgBranchControllers.cs` and its neighbours):
    - `DailyAttendanceRecords` is scoped by hand: 403 on `Details`, `Edit` and `Delete`, and its lists are filtered. But `Create` does not check `vm.EmployeeId`, and `Edit` does not check a changed employee;
    - `HrStatisticReports`, `FieldInspectionVisits`, `DiwanCorrespondences`, `Rosters`, `SuccessionPlans`, `KeyPositions` and `Shifts` check no scope;
    - the keys: `HrStatisticReport.OrgUnitId?`, `ShiftRoster.OrgUnitId?`, `KeyPosition.OrgUnitId?`, `SuccessionPlan.KeyPositionId`, `DailyAttendanceRecord.EmployeeId`, `Employee.PrimaryOrgUnitId?`. `DiwanCorrespondence` has only `RelatedEmployeeId?`, `FieldInspectionVisit` only `LeadInspectorId?`, and `Shift` nothing.
  - **Workflow instances:**
    - `WorkflowInstance.ScopeOrgUnitId` (PLAN-43) is set only by `ExternalApprovalService`, for the external types;
    - internal starts go through `WorkflowStarter.StartByWorkflowCodeAsync`, which `ApprovalStartCoordinator`, `CompetitionLifecycleService`, `CompetitionResultsService`, `RecruitmentAppealService` and `PolicyOrchestrator` call. It stamps no scope;
    - `WorkflowInboxQueryService` computes the unit per type: `RecruitmentRequisition` and `OfferLetter` from their `FundedPosition.OrgUnitId`, `SelectionCommitteeSession` from its requisition's, `RecruitmentAppeal` from its application's requisition's. `EstablishmentChangeRequest` has none;
    - `WorkflowInstancesController` (`api/workflow-instances`, class policy `HrCore.ReadOnly`) lists every instance, and gets any one with its history and its `ContextJson`;
    - the migrations `WF044_WorkflowInstanceIntegrity` and `HCM050_ApprovalDelegationScope` lift `FORCE ROW LEVEL SECURITY` on the tables they read, inside the migration, and then restore it.
  - **HR.Contracts:**
    - `HireWizardController` carries `[Authorize(HrContracts.HireView)]` and `[ModulePolicyOverride(HrContracts.HireView)]` on its class. `HireView` admits Admin, `hr-contracts`, `hr-core` and `hr-management-approver`;
    - its `Index` and `Continue(employeeId)` are scoped since QA-034 (403; an employee with no snapshot is outside). **Every action by `transactionId` checks nothing:** `Step2`, `CreatePendingContract`, `IssueAppointmentDecision`, `CompleteMedicalStep`, `Step2Confirm`, `Step3`, `Step3SubmitForApproval`, `Step3Approve`, `Step3Execute`, `Step4`, `Step4Complete`;
    - `ServiceTransaction` has only `EmployeeId`;
    - `ExportController` (class policy `Module.HrContracts`) has `LeaveRequestsCsv/Excel` and `AdvanceRequestsCsv/Excel`. With `async=true` each goes through `ContractsExportJobService` and `ExportJobRequestedConsumer`, **which run without the user**;
    - among the other controllers, `ServiceTransactions`, `SeparationWizard`, `AdministrativeDecisions`, `Contracts`, `LeaveBalances`, `LeaveRequests` and `AdvanceRequests` act by employee or by id.
  - **Admin.Logistics** has eight controllers: `AssetAssignments`, `Assets`, `Home`, `InventoryItems`, `MaintenanceRecords`, `ReceiptRecords`, `SupplyOrders`, `Vendors`. Assets and custody are scoped by hand (`AssetOrgScopeGuard`, 403; AC-32 pins it). The rest is not.
  - **The snapshots:**
    - `EmployeeOrgUnitSnapshot` lives in the databases of HR.Contracts, Admin.Logistics, HR.Health and HR.Training, fed by `EmployeeCreated` and `TransferExecuted` consumers. **HR.Health has no transfer consumer;**
    - HR.Movements has `OrgUnitSnapshots` but no employee snapshot;
    - consumers are registered by assembly scan.
  - **HR.Movements and delegation:**
    - `HrMovements.ApprovalAccess` admits Admin, `hr-movements`, `hr-management-approver`, or an active delegate of `hr-management-approver`. Through `[ModulePolicyOverride]` it opens `Index`, `Details` and `Approve` of the five movement types;
    - `TransferRequests` and `LoanAssignmentRequests` check the user's scope and the principal's, on both units (403);
    - `SecondmentRequests`, `PromotionRequests` and `DelegationRequests` carry only `EmployeeId`, and check no scope;
    - `DelegationsController.Create` accepts any employee of the directory as the delegate.
  - **Transfers:**
    - `ITransferExecuted` carries `TransferRequestId` (default `null`). HR.Movements' transfer saga publishes it;
    - Auth.Directory's `TransferExecutedConsumer` updates `DirectoryEntry` only. `AuthUser.ScopeOrgUnitId` exists, and `EmployeeCreatedConsumer` sets it to the new employee's unit;
    - `KeycloakUserProvisioningService` works only when `Keycloak:DirectorySync:Enabled` is true. It writes `scope_org_unit_id` when it creates a user; `TrySyncUserInfoAsync` is the pattern for a new call;
    - HR.Health has `IntegrationMessageReceipts` (PLAN-40's inbox, `IntegrationMessageInboxExtensions`); Auth.Directory has none;
    - counters live on the `Meter` of `OpenTelemetryExtensions` (`job_cycles_total` and the like).
  - **Tests and acceptance:**
    - `BolaArchitectureTests` (v1) is in `HRManagement.UnitTests/Architecture`;
    - AC-01..06 and AC-32 are lab classes with `required: false`. AC-02 and AC-03 accept 403 or 404. **AC-06 asserts 403** on `api/lookup/employees/{id}` for the other branch's employee, and **AC-32 asserts 403** on `Assets/Details` outside the scope;
    - `AcHcm29HireContractTests` (lab, required) drives hire flows with `br1-mgr`; `AcWf44` (44e) uses `br1-mgr`'s inbox as its control; `AcIam50` has `br1-mgr` approve step 1 of a RIF requisition.
  - **Personas** (67):
    - `br1-mgr` (RIF, `a0000001-0000-4000-8000-000000000102`) and `br2-mgr` (ALEP, `…0104`) hold `branch-manager`, `hr-core` and `admin-logistics`;
    - `a-homs-mgr`, `a-hama-mgr`, `a-lat-mgr`, `a-tar-mgr`, `b-branch-manager` and `c-branch-manager` hold `branch-manager` and `hr-core`;
    - `negative-invalid-scope` holds `branch-manager` with an invalid scope; `central-a` holds `hr-core`;
    - **no restricted persona has an employee.**
  - **The demo server:**
    - no demo user holds `branch-manager`;
    - demo users are declared in `docker/keycloak/realm-manifest.json`, which also maps `scope_org_unit_id` into the token;
    - they reach the live realm only through `scripts/vps/add-demo-user.sh`, which knows `--employee-id` and no scope.
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43). `WF044_WorkflowInstanceIntegrity` shows the way: lift `FORCE` on the tables it reads inside the migration's own transaction, then restore it.
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role (PLAN-43, PLAN-44, PLAN-47..51).
  - **A new PostgreSQL test uses the connection strings of `PostgreSqlFixture`,** whose idle connections close after seconds. A class that passes alone but fails in the full Testcontainers step is a real failure (PLAN-49, `docs/CI_AND_TESTING.md`). Make sure CI's Testcontainers filter picks up your new classes, as it does the earlier ones.
  - **The prod-like stack runs `ASPNETCORE_ENVIRONMENT=Acceptance`,** so it does not load `appsettings.Production.json`. A value in `appsettings.json` loads everywhere; a value in a Production file must be mirrored in `docker-compose.acceptance-prodlike.yml`.
  - **Personas and users:**
    - a persona change needs `scripts/acceptance-reset.ps1` before the first start (PLAN-43);
    - a new persona also changes `DATA-AUTH` in `scripts/acceptance-verify.ps1` (PLAN-48);
    - a user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh` (PLAN-45).
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - **Formatting:** before every commit, run `dotnet format whitespace HRManagement.slnx --verify-no-changes --no-restore --include <the files you changed>`. CI runs the full check (`dotnet format HRManagement.slnx --verify-no-changes --no-restore`), and in PLAN-50 it failed ci-local twice on whitespace, about 17 minutes each time.
  - **Acceptance tests:**
    - a test posts the URL and the token the page renders (PLAN-44);
    - policy API writes need the antiforgery token (`AcceptanceHarness.CreatePolicyLifecycleClientAsync`, PLAN-47).
  - **The machine:**
    - after the host sleeps, the Docker VM's clock lags, and time-based tests fail until it catches up (PLAN-50);
    - **SSH and the VPN** (PLAN-48, PLAN-51): the owner's network may block outbound port 22, and only ProtonVPN lets it through; but with the VPN on, Testcontainers fail with "Failed to connect to 192.168.65.254". So order the work (P-2): the full Testcontainers step on the host runs before the server phase, and `ci-local.ps1 -SkipTestcontainers` is allowed when the VPN is on, because Testcontainers run on ubuntu-latest in CI.
  - **The session may stand still** (PLAN-51: 7 h 44 min after the pre-push check, until «Try again»). Keep the progress file's next step exact, and go from the pre-push check straight to the push and the PR, in the same turn.
  - **The server:**
    - run the server build detached (PLAN-44), and the server suites with nothing heavy beside them (PLAN-45);
    - if the server build fails because the provider's resolvers answer SERVFAIL, wait and retry; never change the server's resolver configuration (PLAN-50);
    - the server suites write into the committed `acceptance-evidence/` and `reports/`: restore those files, or move them under `artifacts/plan52/vps/` (PLAN-48);
    - a server check cleans up what it leaves open, and decodes a page before it compares Arabic text (PLAN-49);
    - a user without HR.Core signs in through another page; a refusal of the gateway's own pages is a 302; the gateway audits only what it forwards. Write the checks for that (PLAN-51).
  - **Before the push:**
    - compare the seeded counts of `scripts/acceptance-verify.ps1` on your local stack, by psql (PLAN-48..51);
    - run the full Testcontainers step on the host with CI's filter, when `ci-local.ps1` runs with `-SkipTestcontainers` (PLAN-49);
    - a log line that writes a value built from the request is CodeQL's `cs/log-forging` (PLAN-48);
    - a test literal that looks like a key trips gitleaks' `generic-api-key`. Write it differently rather than allowlisting it, and never quote a flagged value in a report (PLAN-48, PLAN-50).
  - **The architecture project's first run after a build** takes about 2 min 25 s, outside the tests, while the host starts over the fresh assemblies (PLAN-51).
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools.
  - The acceptance services have no container health check: readiness means `/health/ready`.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-52-iam-branch-scope-bola.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
2. These reports:
   - `reports/plan51-final-report.md`, in full, and its merge note (`reports/plan51-progress.md` after you bring the note in: «Merge (P-15)» and «Timing (final)»);
   - `reports/plan50-final-report.md`: delegation, and the scopes it stores and applies;
   - `reports/plan34-final-report.md`: the twelve scoped lists and BOLA v1;
   - `reports/plan25-final-report.md` §11 (the recruitment follow-ups);
   - `reports/plan43-final-report.md` (workflow instances and `ScopeOrgUnitId`) and `reports/plan40-final-report.md` (the consumers' inbox).
3. These documents: `docs/IAM_001_SCOPE_GAP_STUDY.md` (G-IAM-04, G-IAM-11, §2.3), `docs/IAM_002_ORG_SCOPE.md`, `docs/IAM_005_IDENTITY_LIFECYCLE.md`, `docs/HIRE_RBAC_MATRIX.md`.
4. The code named in «The code today».
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.
6. `docs/OWNER_DECISIONS_REGISTER.md`: D-IAM-3, and D-52-1 to D-52-5.
7. Every file the plan cites.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **You are in `D:\my projects\HRManagement`,** on `main` or on `docs/plan-51-merge-note`, and no other plan session is running.
2. **`git fetch origin`**, and `origin/main` holds PLAN-51 (`e6a93705` or later).
3. **The working tree is clean,** except for these, which may be present:
   - the owner's local `global.json` edit;
   - `tmp-vendors.html`;
   - git-ignored `artifacts/`;
   - untracked prompt files under `docs/acceptance-plans/execution/`.

   Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
4. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
   - **If port 22 is blocked** from the owner's network, this precondition does not stop P0. Record it, take the server's read-only picture at the start of P8, and go on with the local phases (P-2).
5. **`gh auth status`** shows `omardefdev` with the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

Accepted by the owner on 2026-10-31 (wave-1 questionnaire, «accept the recommended» for every plan decision). The plan's defaults apply as written:

- **D-IAM-3, the scope model:** the scope stays tied to `branch-manager` (`HrOrgScopeVisibility`). The mechanism reads `OrgAccessScope`, never the role, so wave 2 (IAM-H) can change the model without touching it.
- **D-52-1, a record with no scope key** is central: a restricted user neither sees it nor writes it.
- **D-52-2, entities with no unit** (`DiwanCorrespondence`, `FieldInspectionVisit`):
  - they get an `OrgUnitId`;
  - at creation it comes from the linked employee, else from the restricted creator's scope, else the central user chooses it;
  - old rows take the linked employee's unit if there is one, and stay empty (central) otherwise.
- **D-52-3, reference data** (`InventoryItems`, `Vendors`, `Shifts`): everyone reads; only central users write (403 for a restricted user).
- **D-52-4, a transferred restricted user:**
  - `scope_org_unit_id` is removed from his account: locally, and in Keycloak when the sync runs;
  - his scope becomes `InvalidClaim`, so 403, until the directory admin assigns a new one;
  - `branch-manager` is not removed, because removing it alone would make an `hr-core` holder unrestricted.
- **D-52-5, the answer outside the scope:** 404 for details and for a POST by id; 403 when the scope is `Denied` or the controller is central — as `RecruitmentOrgScopeFilter` does.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-52-iam-branch-scope-bola` from `origin/main`, in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-51's merge note from `docs/plan-51-merge-note` (`d43d9fa6`);<br>&nbsp;&nbsp;– commit this prompt file;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **The machine: awake, Docker, the disk, the ports, the VPN.**<br>• **Keep the machine awake for the session, changing no setting.** At P0, start a hidden background PowerShell process that holds `SetThreadExecutionState(ES_CONTINUOUS \| ES_SYSTEM_REQUIRED)` and sleeps in a loop. Record its PID, and stop it at the end of the session. It does not stop a Windows Update restart; record one if it happens.<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• Before a Docker-heavy run, check `docker ps`. Remove only this repository's leftovers: compose projects `hrm-*`, Testcontainers, and untagged images of this repository's own builds. Never prune volumes or images of other projects.<br>• **The disk:** before every image build and every stack start, read the free space of the drive that holds Docker's data disk.<br>&nbsp;&nbsp;– Below 15 GB, remove this repository's leftovers first.<br>&nbsp;&nbsp;– If it is still below 15 GB, moving Docker's disk is a setting of the owner: `BLOCKED:`.<br>• **One heavy job at a time:** never an image build beside a host test run.<br>• **After an image build,** check that every service image was rebuilt from this tree. A failed build leaves stale images, and a `-NoBuild` run on them proves nothing.<br>• **Before each stack start,** compare the stack's host ports with `netsh interface ipv4 show excludedportrange protocol=tcp`. If a port the tests read is reserved, freeing it is a system change for the owner: `BLOCKED:` with the range.<br>• **The VPN and SSH** (Lessons): Testcontainers need the VPN off, and SSH may need it on.<br>&nbsp;&nbsp;– Run the full Testcontainers step on the host **before** P8, on the code the server will get.<br>&nbsp;&nbsp;– Check SSH again just before P8. If port 22 is blocked then, `BLOCKED:` with one line: the owner turns ProtonVPN on, and you continue on his next message.<br>&nbsp;&nbsp;– When `ci-local.ps1` runs with the VPN on, use `-SkipTestcontainers`, and name the run of the full step it relies on. |
| P-3 | **The reach matrix (plan 0.3).**<br>• **What counts as reach:**<br>&nbsp;&nbsp;– the gateway (`GatewayPathAuthorizationMiddleware`: `TryGetPolicy`, `TryGetFineGrainedPolicy`, PLAN-50's `GatewayHrCoreActionPolicies` and `Gateway.ApprovalReach`);<br>&nbsp;&nbsp;– the module filter and its overrides;<br>&nbsp;&nbsp;– the action and class policies;<br>&nbsp;&nbsp;– **an active delegation** (PLAN-50), for a restricted user who has an `employee_id`: every `RoleOrDelegationRequirement` policy is within his reach.<br>• **The users:**<br>&nbsp;&nbsp;– every restricted persona («The code today»);<br>&nbsp;&nbsp;– a synthetic restricted user with an `employee_id` and a delegation, because no persona has an employee;<br>&nbsp;&nbsp;– and a branch manager who is also an employee (`hr-employee`), for the self-service controllers.<br>• **Per controller, in all fifteen assemblies** of `WebAssemblyCatalog`, File.Storage and the gateway included: the plan's lists name neither.<br>&nbsp;&nbsp;– A controller is **actual** when some restricted user reaches one of its actions, with the path that decides it.<br>&nbsp;&nbsp;– It is **latent** when none does.<br>• **Output:** the table in `reports/plan52-baseline.md`, generated into `artifacts/plan52/reach-matrix.*`. It decides P-11's rows: an actual controller is never a row. |
| P-4 | **The mechanism (plan §1).**<br>• **The registry** (`OrgScope/OrgScopeKeyRegistry.cs`) gives each entity type an expression to its unit or units, which must translate to SQL on SQLite and on PostgreSQL:<br>&nbsp;&nbsp;– a direct unit (`OrgUnitId`);<br>&nbsp;&nbsp;– through an employee: `Employee.PrimaryOrgUnitId` in HR.Core, the local `EmployeeOrgUnitSnapshot` elsewhere;<br>&nbsp;&nbsp;– through a parent entity: `SuccessionPlan` → `KeyPosition`, `ReceiptRecord` → `SupplyOrder`, `MaintenanceRecord` → `Asset`;<br>&nbsp;&nbsp;– several units that must **all** be in the scope: a transfer's two sides, as FUN-003 does.<br>&nbsp;&nbsp;A null unit, or a missing snapshot, is no key: central (D-52-1).<br>• **The ids:**<br>&nbsp;&nbsp;– per controller, the registry maps the names of ids (`id`, `employeeId`, `rosterId`, `transactionId`, `keyPositionId`…) to entity types;<br>&nbsp;&nbsp;– `[OrgScopeId(typeof(T))]` on an action or a parameter changes that;<br>&nbsp;&nbsp;– `[NotOrgScopeKey("reason")]` marks an id that names no scoped record (a contract type, a leave type);<br>&nbsp;&nbsp;– a posted `OrgUnitId` is a key too: a restricted user cannot create a record in another branch, or move one there.<br>• **The filter** (`OrgScopeActionFilter<TDbContext>`) acts for a restricted user only (`MustFilter`, or `IsDenied`); an unrestricted user costs no query.<br>&nbsp;&nbsp;– `Denied`, or `[OrgScopeCentralOnly]` on the action or its class → 403.<br>&nbsp;&nbsp;– Every id of the action — route, query, form, a bound model's properties, each element of a list of ids — is resolved through the registry **before the action runs**: before any read in the action, and before any change in a POST. Out of scope, or no key → 404.<br>&nbsp;&nbsp;– Two ids that name a parent and a child (`id` and `vacancyId`, a roster and an employee) are both checked, and the action still checks that the child belongs to the parent.<br>&nbsp;&nbsp;– It runs before `[ApiController]`'s automatic 400 (an order below -2000), so a foreign id gets the same answer whatever the body.<br>• **Two answers, one filter.**<br>&nbsp;&nbsp;– The plan decides first: D-52-5 wherever its scenarios (AC-IAM-52a..e) name a screen, even where today's code answers 403 (the attendance details). An older test that pinned 403 there moves to 404, and the report lists it.<br>&nbsp;&nbsp;– Where the plan names nothing, the answers that its §2 rule 2 protects (employees, records, documents, recruitment), or that an older test pins (AC-06's lookup, AC-32's assets, PLAN-34's twelve lists, QA-034's `HireWizard.Index` and `Continue`), stay as they are. The attribute carries that answer (`OutOfScope = Forbid`), so no assertion changes.<br>&nbsp;&nbsp;– The report lists both groups.<br>• **Lists, exports, pick-lists:** in an `[OrgScoped]` controller, every `Index`, every export (direct, and the query a job runs) and every pick-list a form fills from the database goes through `WhereInOrgScope<T>(registry, scope)`. The filter does not touch lists; the integration tests prove each one.<br>• **Each controller's stance:** `[OrgScoped(typeof(T))]`, `[OrgScopeCentralOnly]`, `[OrgScopeNotApplicable("reason")]` (reference data, self-service by ownership, service-to-service, a page with no org records), or `[RecruitmentOrgScope]`. An attribute on an action overrides its class's: a reference-data controller has central writes.<br>• **`OrgScopeRequirement` goes:**<br>&nbsp;&nbsp;– its handler, `AddOrgScopeAuthorization()` and its two registrations;<br>&nbsp;&nbsp;– the five handler tests of `Iam002OrgScopeTests` (its scope tests stay);<br>&nbsp;&nbsp;– `scripts/iam002-validate.ps1` then checks the new mechanism instead of the handler: a check is replaced, never only removed;<br>&nbsp;&nbsp;– `IAM_002_ORG_SCOPE.md` describes the registry and the filter (P9).<br>• **D-IAM-3:** the filter and the registry read `OrgAccessScope` only, never a role name. |
| P-5 | **HR.Core (plan §2).**<br>• **Every controller declares its stance** (the plan's §2 rule 1).<br>• **The plan's items:**<br>&nbsp;&nbsp;– `DiwanCorrespondences` and `FieldInspectionVisits`, on their new `OrgUnitId`;<br>&nbsp;&nbsp;– `HrStatisticReports`;<br>&nbsp;&nbsp;– `DailyAttendanceRecords`: the employee of `Create`, and of `Edit` before and after the change;<br>&nbsp;&nbsp;– `Rosters`, with `AddAssignment`'s employee; `SuccessionPlans`, with `AddCandidate`'s employee; `KeyPositions`;<br>&nbsp;&nbsp;– `Shifts`: `[OrgScopeNotApplicable]` for reading, and central writes (D-52-3).<br>• **Stamping at creation (D-52-2):**<br>&nbsp;&nbsp;– a Diwan record takes its related employee's unit; otherwise the restricted creator's scope root; otherwise the central user's choice on the form (empty = central);<br>&nbsp;&nbsp;– a field inspection takes the restricted creator's root, or the central user's choice;<br>&nbsp;&nbsp;– a restricted user's choice outside his scope is refused, like any other key.<br>• **Workflow instances:**<br>&nbsp;&nbsp;– **one scope resolver per entity type,** extracted from `WorkflowInboxQueryService`. The inbox, `WorkflowStarter` and the migration's parity test (P-6) all use it;<br>&nbsp;&nbsp;– `WorkflowStarter` stamps `ScopeOrgUnitId` at the start of an internal type; a type with no key stays null (central);<br>&nbsp;&nbsp;– `List`, `Get` and `History` filter by it for a restricted user: null is central, so hidden from the list, and 404 for `Get` and `History`;<br>&nbsp;&nbsp;– inventory B (P0) lists every entity type that starts a workflow, internal or external, with its key or «none».<br>• **Recruitment** (PLAN-25's follow-ups):<br>&nbsp;&nbsp;– `RecruitmentOrgScopeFilter` stays as it is, and learns three kinds: `citizenApplicationId` (the unit of its posting's requisition), `internalApplicationId` and `vacancyId` (the internal vacancy's unit);<br>&nbsp;&nbsp;– `TalentMarketplaceController` carries `[RecruitmentOrgScope]` with the internal vacancy's kind;<br>&nbsp;&nbsp;– the requisition pick-list of `RecruitmentApplications.Create` is scoped.<br>• **Agency-wide catalogues:** D-52-3 names three. Inventory C (P0) lists every other catalogue a restricted user writes today. They get `[OrgScopeNotApplicable("reference data")]` and keep their write policies — which roles write them is RBAC, for IAM-H. The report lists them. |
| P-6 | **The migration `HCM052_OrgScopeKeys`.**<br>• **What it does:**<br>&nbsp;&nbsp;– a nullable `OrgUnitId`, with an index, on `DiwanCorrespondences` and `FieldInspectionVisits`;<br>&nbsp;&nbsp;– Diwan's old rows take their `RelatedEmployeeId`'s unit;<br>&nbsp;&nbsp;– old field inspections stay empty: the plan's integration test says so, and `LeadInspectorId` is the inspector, not the subject;<br>&nbsp;&nbsp;– `WorkflowInstances.ScopeOrgUnitId`, where it is null, is filled per type with the resolver's logic in SQL. The column already exists (PLAN-43).<br>• **Forced row security:** these are tenant tables. Lift `FORCE` on every table the migration reads or writes, inside its own transaction, and restore it, as `WF044` and `HCM050` do. No byte order mark.<br>• **Proof as a non-superuser** (`Iam052OrgScopeMigrationPostgreSqlTests`, with `PostgreSqlFixture`'s connection strings):<br>&nbsp;&nbsp;– the migration runs as a role like production's `*_migrator`, under `FORCE`, on two agencies;<br>&nbsp;&nbsp;– each type's backfilled value equals the resolver's;<br>&nbsp;&nbsp;– Diwan's rows without an employee, and every old inspection, stay empty;<br>&nbsp;&nbsp;– `Down` works.<br>• `dotnet ef migrations has-pending-model-changes` is clean for HR.Core.<br>• **The counts** (plan Risks, row 2): per entity, the records with no key, before and after the migration, from the migration test and from the server (P-16). `scripts/iam052-validate.ps1 -Counts` reads them; they go into the report. |
| P-7 | **HR.Contracts (plan §3).**<br>• **All 22 controllers declare their stance.** `ServiceTransaction`, `Contract`, `LeaveBalance`, `LeaveRequest`, `AdvanceRequest` and the other employee records are keyed through the employee to `EmployeeOrgUnitSnapshot`; no snapshot = central (D-52-1, the plan's §3 rule 2).<br>• **`HireWizard`:** `[OrgScoped(typeof(ServiceTransaction))]`; every action by `transactionId` answers 404 out of scope (AC-IAM-52b). `Index` and `Continue` keep QA-034's answer (P-4).<br>• **The plan's latent items:** `Contracts.Details`, `LeaveBalances.Create(vm.EmployeeId)`, and **every** export of `ExportController`: leave and advance requests, CSV and Excel.<br>• **Export jobs run without the user:**<br>&nbsp;&nbsp;– the job records the requester's scope (its root, or «unrestricted») when it is queued, and `ExportJobRequestedConsumer` applies it;<br>&nbsp;&nbsp;– a restricted requester's job with no recorded scope exports nothing (fail-closed);<br>&nbsp;&nbsp;– prove it with a job queued by a restricted user.<br>• No change to the wizard's role policies (the plan's §3 rule 1). |
| P-8 | **Admin.Logistics (plan §4).**<br>• **All eight controllers declare their stance.** The keys:<br>&nbsp;&nbsp;– `SupplyOrder` → its `OrgUnitId`;<br>&nbsp;&nbsp;– `ReceiptRecord` → its supply order;<br>&nbsp;&nbsp;– `MaintenanceRecord` → its asset, with the logic of `AssetOrgScopeGuard`;<br>&nbsp;&nbsp;– `Asset` and `AssetAssignment` as today.<br>• **`InventoryItems` and `Vendors`:** `[OrgScopeNotApplicable("reference data")]`, and central writes (D-52-3).<br>• **`Assets` and `AssetAssignments`** keep their 403 (AC-32, P-4). |
| P-9 | **The delegation path (plan 0.3, PLAN-50).**<br>• **The reach:** a restricted user who has an employee can be a delegate of an `hr-management-approver`, because any employee of the directory can. He then reaches `Index`, `Details` and `Approve` of the five movement types in HR.Movements. Expect P-3 to mark those five controllers actual. **They are closed here, not listed:**<br>&nbsp;&nbsp;– **`TransferRequests` and `LoanAssignmentRequests`:** `[OrgScoped]`, with HR.Movements keys that require both units in the scope, and today's 403 (P-4). PLAN-50's check of the principal's scope stays in the actions;<br>&nbsp;&nbsp;– **`SecondmentRequests`, `PromotionRequests` and `DelegationRequests`** carry only `EmployeeId`, and HR.Movements has no employee snapshot. So for a restricted user each of their records is central (D-52-1): `[OrgScopeCentralOnly]`, 403 on every action. Unrestricted users see no change.<br>• **For later:** an employee snapshot in HR.Movements, so that a branch delegate could act on his own branch's secondments, belongs to G-MOV-10 (wave 2). The report lists it under «Items that other plans own».<br>• HR.Core's approval paths already apply both scopes to a delegate (PLAN-50); P-3 confirms it.<br>• **Proof:** an integration test (`Iam052MovementsDelegateScopeHttpTests`) with a restricted user, his employee, and an active delegation for each type:<br>&nbsp;&nbsp;– 403 on the three employee-only types;<br>&nbsp;&nbsp;– on transfers and loans, only records with both units in his scope, and the principal's scope still applies.<br>• HR.Movements' other controllers stay latent (P-11). |
| P-10 | **Transfers (plan §5).**<br>• **Auth.Directory's consumer:** when the employee's `AuthUser` has a `ScopeOrgUnitId`, whatever his roles:<br>&nbsp;&nbsp;– clear it, and write an audit row with the old unit, the new one and the transfer;<br>&nbsp;&nbsp;– when `Keycloak:DirectorySync:Enabled` is true, call the new `TryClearScopeAttributeAsync` (the pattern of `TrySyncUserInfoAsync`), which removes `scope_org_unit_id` from the Keycloak user;<br>&nbsp;&nbsp;– otherwise log a warning and add one to `directory_scope_sync_skipped_total` (the `Meter` of `OpenTelemetryExtensions`);<br>&nbsp;&nbsp;– the local value is cleared in both cases, and `branch-manager` stays (D-52-4). The claim matters only with `branch-manager`, so clearing it never widens anyone's access.<br>• **HR.Health's new consumer** updates `EmployeeOrgUnitSnapshot.PrimaryOrgUnitId`, as HR.Contracts' does; the assembly scan registers it.<br>• **Idempotent** on `TransferRequestId`, or on the message id when that is null: a redelivery changes nothing and writes no second audit row.<br>&nbsp;&nbsp;– HR.Health uses its `IntegrationMessageReceipts` (PLAN-40).<br>&nbsp;&nbsp;– Auth.Directory either gets the same inbox (a migration) or proves its idempotency without it. Record which.<br>• **Tests:**<br>&nbsp;&nbsp;– `Iam052DirectoryTransferConsumerTests`, with a fake Keycloak (`HttpMessageHandler`): sync on → the attribute's removal is requested; sync off → the warning and the counter; the local scope is empty in both; a redelivery adds nothing;<br>&nbsp;&nbsp;– HR.Health's consumer: the snapshot moves, idempotently.<br>• Never execute a transfer on the demo server (P-16). |
| P-11 | **BOLA v2 and its lists (plan §6).**<br>• **`OrgScopeCoverageTests`** in `HRManagement.ArchitectureTests`, over the fifteen assemblies of `WebAssemblyCatalog`:<br>&nbsp;&nbsp;1. every controller declares its stance, or has a row in its service's list;<br>&nbsp;&nbsp;2. in an `[OrgScoped]` controller, **in GET and in POST alike**, every `Guid` or `Guid?` parameter or bound-model property named `id` or ending in `Id`, and every list of `Guid` named `ids` or ending in `Ids`, has a key in the service's registry or `[NotOrgScopeKey("reason")]`;<br>&nbsp;&nbsp;3. it calls each `{Service}OrgScopeKeys.Configure` for real; a text marker proves nothing.<br>• **The lists** use PLAN-51's `CeilingList`: `tests/architecture/org-scope-allowlist/<service>.txt`, one file per service as PLAN-51's are (the plan's single file is a deviation to record), and **one row per controller**. A new controller in a latent service then needs a stance, since the test refuses new rows.<br>&nbsp;&nbsp;– A latent row reads `Controller \| IAM-H: latent, no restricted role reaches it (D-IAM-3)`. Add `IAM-H` to `CeilingList.Owners`.<br>&nbsp;&nbsp;– In the end, HR.Core, HR.Contracts and Admin.Logistics have no list, and no controller that P-3 found actual has a row.<br>&nbsp;&nbsp;– Seed the lists in P2, with `PLAN-52:` rows for whatever is not declared yet; P3–P5 empty those rows. At the end, `PLAN-52` leaves `Owners`, as PLAN-51 left it.<br>• **Delete v1** (`BolaArchitectureTests.cs`) once v2 is green and covers PLAN-34's twelve: each is declared, or is a latent row with its reason. The report maps the twelve.<br>• **Each guard fails on a planted defect** (`GuardSelfTests`): a controller with no stance, an id with no key, a list of ids with no key, a row without an owner, a new row.<br>• **The expressions translate:** `Iam052OrgScopeKeysTranslateTests` runs every registry entry of every service against a real `DbContext`, on SQLite and on PostgreSQL (plan Risks, row 1). |
| P-12 | **Existing tests that the new rules change.**<br>• Only these may change, and never by weakening an assertion:<br>&nbsp;&nbsp;– the tests of the deleted handler;<br>&nbsp;&nbsp;– a test that pinned 403 where the plan's scenarios set 404 (P-4);<br>&nbsp;&nbsp;– a test that relied on a screen being unscoped;<br>&nbsp;&nbsp;– the test of `iam002-validate.ps1`.<br>• **Stay green unchanged:** AC-02, AC-03, AC-06 and AC-32 (their answers are kept, P-4), AC-HCM-25, AC-HCM-29, AC-IAM-50, AC-IAM-51, AC-WF-44, `Hcm025PanelServerParityTests`, and the scope tests of PLAN-34 and QA-034. |
| P-13 | **Acceptance placement (PLAN-41 and PLAN-66 rules).**<br>• **`AcIam52BranchScopeTests`** (AC-IAM-52a..e): `prodLike[]`, `required: true`, group `main`; `[RequiredAcceptance]` and `EvidenceRecorder("AC-IAM-52")`. It must not stop or recreate containers.<br>• **Its data comes from the test itself:** `br2-mgr` (ALEP) or `central-a` creates the foreign records, and `br1-mgr` (RIF) tries them. Every negative reads the database before and after. `negative-invalid-scope` gets 403.<br>• **No new persona:** the `scenarios` of `br1-mgr`, `br2-mgr` and `central-a` gain AC-IAM-52; `DATA-AUTH` stays 67.<br>• `ac-ownership.json`: `AC-IAM-52` (owner PLAN-52, `provesClaim: true`).<br>• **AC-02, AC-03, AC-06 and AC-32** keep `required: false` (their flags are PLAN-65's). But this plan treats a failure of theirs as red: locally, and in the PR run, where you read their results in the lab stage's log or artifact.<br>• **The evidence of record is the PR run's artifact,** not `acceptance-evidence/AC-IAM-52/` as the plan says. Copy what the report needs into `artifacts/plan52/`, then restore the committed evidence. |
| P-14 | **Local testing (owner's rule of 2026-11-06).** Run only the classes this plan touches, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`, and the overlay groups with `-Stage prodLike:<group>`. **The full suite runs once, in CI.** The list, at least:<br>• **prod-like:** `AcIam52BranchScopeTests`, `AcIam50ApproverReachDelegationTests`, `AcIam51ExplicitPolicyTests`, `AcHcm25ProductionApprovalTests`, `AcWf43DefinitionGovernanceTests`, `AcTen48NationalScopeTests`, `AcPol49EngineScopeTests`, the prod-like journey, and the `isolated` group (`AcInt37`, `AcMsg40`, `AcWf44`, `AcWf45`, `AcWf46`, `AcPol47`, `AcOps39`);<br>• **lab:** `Ac01` to `Ac06`, `Ac32LogisticsScopeLiveTests`, `Ac33AssetLossLiveTests`, `AcHcm21RequisitionApprovalUiTests`, `AcHcm22RecruitmentWorkflowsUiTests`, `AcHcm28CommitteeAppealTests`, `AcHcm29HireContractTests`, `AcHcm31CompetitionTests`, `AcEst24EstablishmentWorkflowUiTests`, `AcWf23WorkInboxUiTests`, the lab `isolated` group (`AcHcm26`, `AcHcm27`), and the lab journey;<br>• **leastpriv:** `AcOps38FirstBootTests`, `AcTen48LeastPrivilegeTests` — HCM052 runs there as a least-privileged migrator;<br>• and every class that inventory A (P0) finds using a restricted persona or a screen this plan scopes.<br>Then compare the seeded counts of `scripts/acceptance-verify.ps1` by psql. Known local-only failures of lab classes that need PowerShell 7 (PLAN-43 §6) are recorded, not chased — but AC-02, AC-03, AC-06 and AC-32 must pass (P-13). |
| P-15 | **PowerShell 5.1.**<br>• Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block.<br>• **`scripts/iam052-validate.ps1`** holds no Arabic literal and has a `-SelfTest` with planted defects, as `iam051-validate.ps1` does. It checks, at least:<br>&nbsp;&nbsp;– no `OrgScopeRequirement` and no handler;<br>&nbsp;&nbsp;– the registry, the attributes and the filter exist, and each of the three services calls its `Configure`;<br>&nbsp;&nbsp;– every list row is signed by `IAM-H` with a reason, and the three services have no list;<br>&nbsp;&nbsp;– `HCM052` is present, without a byte order mark;<br>&nbsp;&nbsp;– the two transfer consumers exist.<br>&nbsp;&nbsp;A `-Counts` mode reads the records with no key (P-6), read-only, through `docker exec … psql` locally and through `ssh` on the server.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile ProdLike`, not with the plan's raw `docker compose` lines. |
| P-16 | **The demo server facts this plan must respect.**<br>• It runs the Production environment in demo mode. **No demo user is restricted today,** so no demo user sees a change, except the new user below and the stamped values.<br>• **A branch-manager demo user,** so that the owner can see the scope: `e2e-branch-manager`, with `branch-manager`, `hr-core`, `admin-logistics`, and the `scope_org_unit_id` of a demo branch chosen in P0 (the branch with the most records, while another branch holds records for the negatives).<br>&nbsp;&nbsp;– Declare it in `docker/keycloak/realm-manifest.json`, with its attribute.<br>&nbsp;&nbsp;– `scripts/vps/add-demo-user.sh` learns `--scope-org-unit-id <guid>`: ASCII bash, idempotent, the same secret rules, and the attribute written the way `employee_id` is.<br>&nbsp;&nbsp;– Its password is the demo users' password, never printed.<br>&nbsp;&nbsp;– P0 checks, read-only, that the live realm maps `scope_org_unit_id` into the token, as the manifest says. If it does not, record it, and use only the manifest's own reconcile path; never edit the realm by hand.<br>• **P0 lists, read-only:**<br>&nbsp;&nbsp;– the demo tenant's org units, with per-branch counts of employees and records: attendance, rosters, Diwan, inspections, key positions, hire transactions, supply orders, maintenance, and workflow instances by type and by scope;<br>&nbsp;&nbsp;– the records with no key, per entity (P-6);<br>&nbsp;&nbsp;– `Keycloak:DirectorySync:Enabled` as the containers see it;<br>&nbsp;&nbsp;– the queues bound to `ITransferExecuted`.<br>• **The server checks leave nothing behind:** positives and negatives use existing records, one in the user's branch and one in another. Where no foreign record exists, the check creates one as `hradmin` and removes it at the end. A refused central write creates nothing.<br>• **Never execute a transfer on the demo server.** |
| P-17 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan52-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Before the deploy, read-only:** P-16's picture, with the counts before HCM052.<br>• **Build once,** on the final code, detached; then `scripts/vps/deploy.ps1 -Tag <sha12>`. HCM052 runs at start-up: record its counts after.<br>• **Then add the demo user** (P-16), and record the change with its undo (delete the user with `kcadm`).<br>• **Rollback safety:** find out what PLAN-51's tag (`d0e446fe3280`) does on a database that HCM052 changed:<br>&nbsp;&nbsp;– it does not map the new columns, and sees no pending migration;<br>&nbsp;&nbsp;– the rows it inserts get no unit, so they are central for a restricted user after a roll-forward.<br>&nbsp;&nbsp;Record it in `OPS_VPS_SINGLE_HOST.md`.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn:<br>&nbsp;&nbsp;– roll back to PLAN-51's tag, or fix forward;<br>&nbsp;&nbsp;– restore the backup only if data was damaged;<br>&nbsp;&nbsp;– prove the demo works, and record the downtime. |
| P-18 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P9, after local CI and the pre-push check are green, in the same turn as the pre-push check (Lessons).<br>2. `gh pr create`, then wait («Waiting»).<br>3. Only when every check is green, and AC-02, AC-03, AC-06 and AC-32 passed in the run (P-13): `gh pr merge <n> --merge`.<br>4. A red check, or a failure of one of those four: fix it on the release branch, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>`, and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-19 | **No regression of PLAN-25, PLAN-34, PLAN-36..51 and PLAN-66.** These stay green:<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests`;<br>• `AcIam50`, `AcIam51`, `AcWf43`..`AcWf46` with the orphan class, `AcPol47`, both `AcTen48` classes, both `AcPol49` classes, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, `AcHcm29`, and both recruitment journeys;<br>• the `Iam050*`, `Iam051*`, `Wf043*`, `Wf044*`, `S2s042*`, `Pol047*`, `Ten048*` and `Pol049*` tests, PLAN-51's architecture tests, `GatewayApproverReachTests`, `GatewayPathAuthorizationMiddlewareTests`, `ProductionWiringMatrixTests`, `CiStepsParityTests`, `WorkflowDefinitionsHaveHandlersTests`, `Qa066*`, and the scope tests of PLAN-34 and QA-034;<br>• `scripts/iam051-validate.ps1`, `scripts/iam050-validate.ps1`, `scripts/iam002-validate.ps1` (as P-4 changes it), `scripts/wf043-validate.ps1`, `scripts/hcm025-validate.ps1`, `scripts/pol047-validate.ps1`, `scripts/pol049-validate.ps1`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-51, done:** an explicit policy on every action. This plan adds the scope beside those policies and changes no role policy.
- **Wave 2, IAM-H:**
  - the scope as a property that restricts any role (D-IAM-3, G-IAM-14), the MSS role, and payroll and contracts staff limited to their governorate;
  - until then, the latent controllers keep their rows;
  - an employee snapshot in HR.Movements, so that a branch delegate can act on his branch's secondments, promotions and assignments (G-MOV-10, P-9).
- **PLAN-53:** the scope of `EmployeePayProfiles.Create` and of the salary-change transaction. The scope of the other payroll and movement screens (G-MOV-10) comes when restricted roles reach them, in wave 2.
- **PLAN-62:** turning the Keycloak sync on, and ending a leaver's sessions. The transferred user's session is wave 2 (IAM-G). PLAN-50 already fixed the name of `employee_id`.
- **PLAN-65 (G-IAM-17):**
  - rerunning AC-01..06 live, with the eight evidence items, on the current SHA, and correcting IAM-002's cards;
  - AC-06 live with a scope that changes within one token's lifetime;
  - the `required` flags of AC-01..06 and AC-32.
  This plan adds its own scenarios, and updates `IAM_002_ORG_SCOPE.md` for what changed only.
- **Wave 2, IAM-F (G-IAM-10):** the national number, and the branch manager.

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- another plan's untracked prompt file;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- any secret, token or password (templates keep their placeholders, `__SVC_SECRET__` or `${…}`).

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- touch the side PRs (#40, #42) or their branches;
- disable host-key checking;
- widen a policy, a scope or a role to make a page or a test work;
- give a persona or a demo user more roles, a wider scope, or an employee, to make a scenario pass;
- execute a transfer on the demo server;
- print a password, or rotate a secret that exists on the server;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** (port 22 at P0 excepted: Preconditions 4).
- **A legitimate flow of today** — a required acceptance class, or a demo user's work — cannot keep working without widening a scope, a policy or a role.
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning.
- **HCM052 cannot run as the least-privileged migrator** without a grant that the deployment does not give.
- **The space for Docker's disk stays below 15 GB, or a host port the tests read is reserved** (P-2).
- **Port 22 is still blocked when P8 starts** (P-2): one line asking the owner to turn ProtonVPN on.
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, keep-awake, preconditions, baseline, the reach matrix, inventories

1. Write the `Started (UTC)` line (see «Timing»). Start the keep-awake process (P-2). Check the Preconditions.
2. `git switch -c feature/plan-52-iam-branch-scope-bola origin/main`. Bring in PLAN-51's merge note and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan52-baseline.md`:
   - **0.2:** `dotnet build HRManagement.slnx`; `dotnet test src/Tests/HRManagement.ArchitectureTests`; the unit filter `FullyQualifiedName~Bola|FullyQualifiedName~OrgScope|FullyQualifiedName~Hcm025`;
   - **0.3, the reach matrix** (P-3);
   - **0.4, what PLAN-34 did,** with what QA-034 and PLAN-50 added since: recorded, not redone;
   - **inventory A, per controller** of the three services and of every controller P-3 found actual elsewhere: its stance to be, its entity type, every id with its key, the actions already scoped by hand with their answer (P-4), and its lists, exports and pick-lists;
   - **inventory B, workflow types:** every entity type that starts a workflow, internal or external, with its key or «none» (P-5);
   - **inventory C, catalogues:** every agency-wide catalogue a restricted user writes today (P-5).
4. **The server, read-only:** the running tag, and P-16's picture. If port 22 is blocked, record it and take the picture at the start of P8.

Commit: `chore(iam052): baseline, the reach matrix with delegation, per-controller ids and keys and answers, workflow types, catalogues, the server's picture`.

## P1 — The mechanism (plan §1, P-4)

- **The pieces:** the registry, the attributes, the filter and its order, `WhereInOrgScope<T>(registry, scope)`, and what the filter needs from each service's resolver (HR.Core's `OrgScopeService`, the snapshots elsewhere).
- **`OrgScopeRequirement` removed:** the handler, the registrations, its five tests; `scripts/iam002-validate.ps1` now checks the mechanism.
- **Tests:** `Iam052ScopeFilterTests`, one per rule of P-4, each also failing on a planted defect: `Denied`; central only; out of scope; no key; an id in the route, the query, the form, a model's property, a list of ids; a parent and a child; the kept answer; and an unrestricted user who costs no query.

Commit: `feat(iam052): one scope mechanism, a registry of keys with declared stances and one action filter; OrgScopeRequirement removed`.

## P2 — BOLA v2 (plan §6, P-11)

- **`OrgScopeCoverageTests`,** with the per-service lists seeded from P0's inventories: `IAM-H` rows for the latent controllers, `PLAN-52` rows for the rest. `CeilingList.Owners` gains both.
- **The planted defects** in `GuardSelfTests`.
- **`Iam052OrgScopeKeysTranslateTests`:** SQLite now, PostgreSQL in the Testcontainers filter.

Commit: `test(iam052): reflective scope coverage over the fifteen web assemblies, with per-controller lists that only shrink`.

## P3 — HR.Core (plan §2, P-5, P-6)

- **The controllers:** every one declared; the plan's items; the stamping; the workflow instances (the resolver, the stamping at start, the filtered `List`, `Get` and `History`); the recruitment kinds and the requisition pick-list; the catalogues.
- **`HCM052_OrgScopeKeys`,** and `Iam052OrgScopeMigrationPostgreSqlTests` as a non-superuser; `has-pending-model-changes` clean.
- **`Iam052HrCoreScopeHttpTests`,** generated from the registry over every `[OrgScoped]` controller:
  - each registered type comes with a test seed, beside the generator;
  - for each controller: its list, its details, and each POST by id, with a foreign record → 404 (or the kept 403), and nothing changed in the database;
  - a controller the generator cannot exercise fails the test.
- **HR.Core's `PLAN-52` rows** are gone.

Commit: `feat(iam052): HR.Core, every controller declares its scope; Diwan and inspections get a unit; workflow instances are stamped and filtered; the recruitment follow-ups are closed`.

## P4 — HR.Contracts, Admin.Logistics, the delegation path (plan §3, §4, P-7..P-9)

- **The controllers:** HR.Contracts' 22 and Admin.Logistics' 8 declared; the hire wizard; every export and its job; the logistics keys and D-52-3; HR.Movements' five approval controllers (P-9).
- **Tests:** `Iam052ContractsScopeHttpTests` and `Iam052LogisticsScopeHttpTests`, generated as in P3; the export job of a restricted user; `Iam052MovementsDelegateScopeHttpTests`.
- **Their `PLAN-52` rows** are gone.

Commit: `feat(iam052): the hire wizard, contracts and exports, logistics, and a delegate's movement approval screens stay inside the branch`.

## P5 — Transfers (plan §5, P-10), and v1 out

- **The two consumers,** `TryClearScopeAttributeAsync`, the counter, and idempotency.
- **Tests:** `Iam052DirectoryTransferConsumerTests`, and the test of HR.Health's consumer.
- **v1 out:** delete `BolaArchitectureTests` (P-11), and take `PLAN-52` out of `CeilingList.Owners`. Every list now holds only `IAM-H` rows.

Commit: `feat(iam052): a transferred restricted user loses his scope (fail-closed), HR.Health's snapshot follows the transfer; BOLA v1 replaced by v2`.

## P6 — Acceptance, then the independent review (plan Acceptance, P-12, P-13, P-14)

1. **The pieces of the acceptance run** (P-13): the class, the personas' `scenarios`, `ci-scope.json`, `ac-ownership.json`, `scripts/iam052-validate.ps1`.
2. **Acceptance.**
   1. Check the disk (P-2). Start the stack (`acceptance-up.ps1 -Profile ProdLike`), and record the time to ready.
   2. Run `AcIam52BranchScopeTests` (AC-IAM-52a..e), positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   3. Then run P-14's list, `scripts/iam052-validate.ps1` (and `-SelfTest`), and the seeded counts by psql. All required classes must pass, and AC-02, AC-03, AC-06 and AC-32 too.
   4. `down -v`. Restore the committed evidence.
3. **The full Testcontainers step on the host,** with CI's filter and the VPN off (P-2).
4. **Independent review** (one fresh subagent), before the server, so the single build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - a controller in the three services with no stance, or an id that a restricted user can pass unchecked (route, query, form, model, list);
   - a list, an export (direct or as a job) or a pick-list that shows another branch's record;
   - a POST that changes something before the filter runs, or that accepts an `OrgUnitId` outside the scope;
   - a registry expression that does not translate, or a missing snapshot that does not fall to central;
   - an answer changed where P-4 keeps it, or a 403 where the plan's scenarios set 404;
   - a path P-3 missed (a delegation, an override, a gateway exception) that lets a restricted user reach a latent controller;
   - HCM052 failing under `FORCE` as a non-superuser, or a backfill that differs from the resolver;
   - a transfer that leaves the old scope, or a redelivery that writes twice;
   - a list row that is not latent or has no owner, or a part of the plan deferred, skipped or listed to save time;
   - something PLAN-51's tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(iam052): AC-IAM-52a..e pass on the prod-like stack, AC-02/03/06/32 green; review fixes; CI scope and ownership updated`.

## P7 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to the same context after yours, remove yours and add them again on top. Rebuild, and rerun the unit, architecture and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you.

Commit (only if the merge needed fixes): `fix(iam052): main merged, conflicts resolved`.

## P8 — The demo server (P-16, P-17)

Record every server change and its undo command **as you make it**.

1. Check SSH (P-2) and P-17's condition.
2. Take the backup, then **verify** it. Take the read-only picture (P-16), with the counts before HCM052.
3. **Build and deploy.** Run the build once, detached, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`. If the deploy stops at `keycloak-reconcile` after PostgreSQL restarts, run the same command again (PLAN-40 incident). Record the downtime, the time to all healthy, and the counts after HCM052.
4. **Add `e2e-branch-manager`** (P-16), and check its token carries the scope.
5. **Then check**, with evidence in `artifacts/plan52/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them. The new checks go in an `AcIam52VpsChecks` class, and each one cleans up what it leaves open.
   1. **The branch screens,** as `e2e-branch-manager`: a record of his branch opens (200); each list holds only his branch's records, and its count matches psql; another branch's record answers 404 on its details and on a POST by its id, and nothing changes.
   2. **Workflow instances:** `api/workflow-instances` lists only his branch's instances; another branch's instance, or a central one, answers 404; `hradmin` still sees them all.
   3. **The hire wizard:** only his branch's transactions; another branch's `Step2` answers 404.
   4. **Logistics:** another branch's supply order answers 404; creating an inventory item or a vendor answers 403, and nothing is created.
   5. **Transfers, read-only:** HR.Health's queue for `ITransferExecuted` has a consumer, and `Keycloak:DirectorySync:Enabled` is as P0 recorded.
   6. **Everyone else is unchanged:** `hradmin` and the e2e users keep their work (the PLAN-36..51 server checks), and `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green.
   7. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass, with the PLAN-38..51 server checks. The AC-WF-46 sweeps check is rerun alone after a sweep cycle.
6. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-17 says, prove the demo works, and record it.

Commit: `test(iam052): demo server on branch scope (a branch-manager demo user stays in his branch), PLAN-36..51 server checks still green`.

## P9 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/IAM_002_ORG_SCOPE.md`: the registry, the stances, the filter, the two answers; no `OrgScopeRequirement`;
   - `docs/HIRE_RBAC_MATRIX.md`: the wizard's scope;
   - `docs/IAM_005_IDENTITY_LIFECYCLE.md`: a transfer is fail-closed, and the step that assigns the new scope;
   - `docs/CI_AND_TESTING.md`: `OrgScopeCoverageTests` and its lists;
   - `OPS_VPS_SINGLE_HOST.md`: the demo user, what changed, the backup location, the change table, the rollback note;
   - the «الحالة» lines for G-IAM-04 and for the transfer part of G-IAM-11;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local gates, before any push:**
   - the full Testcontainers step on the host, with CI's filter (P6, if nothing it builds or tests changed since; otherwise again, with the VPN off);
   - `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD (with `-SkipTestcontainers` when the VPN is on);
   - then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`.

   All must be green. The report is committed before them, because a commit after a green local run needs another run.
3. **`reports/plan52-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations. No deviation defers part of the plan.
   4. **The reach, before and after:** per restricted persona and for the delegate, the actual controllers per service, their stance and the path that reaches them; the latent rows per service.
   5. **The answers:** what answers 404, what keeps its 403 and why (P-4) — each line with the test that proves it.
   6. **The records with no key:** per entity, before and after HCM052, from the migration test and from the server.
   7. The guards and their own negative tests.
   8. Unit, architecture, integration and acceptance results; the PR run's artifact link as the evidence of record.
   9. **The server:** before and after, changes and their undo, the backup location, the rollback note.
   10. Items that other plans own: the `IAM-H` rows, G-MOV-10's snapshot in HR.Movements, PLAN-53, PLAN-62, PLAN-65.
   11. Defects for later plans.
   12. **Timing:**
       - total wall-clock;
       - active time, waiting for CI, and every stop with its cause;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 52`.
   13. **A readiness note:**
       - for PLAN-53: the payroll scope it owns, beside its frozen list;
       - for PLAN-62: with the sync on, a transfer clears the scope in Keycloak too;
       - for PLAN-65: AC-01..06 live;
       - for wave 2 (IAM-H): the latent rows, and HR.Movements' employee snapshot.
4. **Merge (P-18), by you, in the same turn as the pre-push check:**
   1. Merge the feature branch into a new `release/plan-52` from `main` (`--no-ff`).
   2. Write `reports/plan52-pr.md` (short).
   3. `git push -u origin release/plan-52`
   4. `gh pr create --base main --head release/plan-52 --title "PLAN-52: branch scope on everything a branch manager reaches, and a reflective BOLA test" --body-file reports/plan52-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green, and AC-02, AC-03, AC-06 and AC-32 passed in the run: `gh pr merge <n> --merge`.
   7. A red check, or a failure of one of those four: fix it on the release branch, push that branch only, and wait again.

   Then:
   - `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`;
   - record the PR link, each check result, the merge commit and the post-merge run in `reports/plan52-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`;
   - commit that note on a local branch `docs/plan-52-merge-note`, as the plans before did, so the next plan brings it in. **Do not open a second PR for it;**
   - stop the keep-awake process (P-2).

## Definition of done

- **Every controller of HR.Core.Web, HR.Contracts.Web and Admin.Logistics.Web declares its scope stance,** and so does every controller that P-3 found actual elsewhere.
- **`OrgScopeCoverageTests` is green:** the lists hold only latent controllers, each row signed by `IAM-H` with its reason; there is no list for the three services.
- **No list, details, export, pick-list or POST by id** crosses a restricted user's scope on the actual surface, the delegation path included.
- **The answers:** 404 where the plan's scenarios say so, 403 for `Denied` and central screens, and the kept 403s listed with their reasons.
- **`OrgScopeRequirement` and `BolaArchitectureTests` v1 are deleted,** v2 covering PLAN-34's twelve.
- **HCM052** stamps Diwan records and workflow instances as the resolver does, proven as a non-superuser.
- **A transfer clears a restricted user's scope,** and HR.Health's snapshot follows it.
- **Nothing in the plan was deferred to save time.**
- **The tests:** AC-IAM-52a..e = Pass (the PR run's artifact is the evidence of record); AC-02, AC-03, AC-06, AC-32, AC-HCM-25, AC-HCM-29, AC-IAM-50, AC-IAM-51 and the PLAN-36..51 suites are green.
- **The server** runs this code, with `e2e-branch-manager`.
- **The documents** P9 lists are updated.
- **The merge** is done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed;
- what changed on the demo server, and how the owner can see the scope with `e2e-branch-manager`;
- how long the plan took, how long it waited for CI, and every stop with its cause;
- the PR link;
- the path of the final report.
