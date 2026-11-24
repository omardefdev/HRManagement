# Task: PLAN-55 — The employee's bank account, and pay profile versions that keep the unit and the regime (PAY-B)

The deliverables:
- **Every employee can have a bank account that a second person verifies:**
  - a payroll processor registers it, and a payroll approver who is not that processor verifies it against the bank's confirmation;
  - payment orders and the treasury file then carry a real destination.
- **A run pins the account at the moment of its snapshot:** changing the account after the calculation never moves the money of that run.
- **No new pay profile version loses the organisational unit or the legal regime,** whatever made it: a contract activation, a salary change, a new employee. The contract activation goes through PLAN-53's applier.
- **The old data is repaired** by a script with a dry run first.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P9** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**Why this plan exists** (`docs/PAY_001_GAP_STUDY.md`, G-PAY-02):
- The bank fields on `EmployeePayProfile` are set by no code. So the treasury file refuses every run, payment orders carry no IBAN, and the fraud rule FR-04 (one account shared by several employees) sees nothing.
- A contract activation creates a pay profile version without the unit and the regime. The employee then drops out of a branch user's pay profile list, and the first activation after `IEmployeeCreated` ignores the profile that carried them.

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder. There is no second clone and there are no locks.
- **The main folder** is `/home/omar/HRManagement` on the owner's Linux notebook, where PLAN-53 and PLAN-54 ran. If the owner opened this session elsewhere, that folder is the main folder: record it. A folder's path is never a reason to stop.
- **PLAN-54 is merged** (Preconditions).

**Complete execution** (owner, 2026-11-12, standing since PLAN-51): the owner wants every plan carried out in full, however long it takes.
- Time is never a reason to skip, defer or allowlist anything.
- Only what the plan itself assigns elsewhere stays out: its «Out of scope».
- Where the plan speaks in general, this prompt makes it concrete, and all of it is in scope:
  - **the account's whole life:** register, verify, reject, retire, replace; through the API and the screens; with its audit, its masking and its five rules;
  - **the pinning:** the snapshot, the salary line, the payment orders, the treasury file, the readiness report, FR-04 and the DSAR answer;
  - **every creation of a pay profile version through `CreateNextVersion`,** the contract activation through `ApplySystemChangeAsync`, a late `IEmployeeCreated`, and the writers' guard;
  - **the repair:** the endpoint and the script, with a dry run;
  - **every new action names its policy, every new controller declares its org-scope stance** (PLAN-52), **every new view passes PLAN-67's interface guard,** and **every new API write validates an antiforgery token** — each from its first commit;
  - **AC-PAY-55a..e,** positive and negative, and the three PostgreSQL integration tests.

**Standing owner instruction on merges** (PLAN-38 to PLAN-54, PLAN-66 and PLAN-67): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-17).
- **Never ask the owner whether to merge:** his instruction is the permission. Asking cost PLAN-51 2 h 09 min.

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Check formatting on the files you changed (see «Lessons»).
4. Commit, using the phase's commit message.
5. Update **`reports/plan55-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan55-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan55-progress.md`. Its first line is:
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
- Write long outputs (inventories, logs, trx files) under `artifacts/plan55/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.
- Build the acceptance images once per code state. Run the unit and integration filters before a stack run, and rerun only the classes that failed.

**Long commands.** A foreground command that outlives the tool's time limit ends the turn. So:
- Run anything that can outlive the limit in the background or detached, with its log under `artifacts/plan55/`, and poll it. That covers the unit suite, the Testcontainers step, an image build, a stack start, an acceptance chain, the local CI steps and the server build.
- Run **one heavy job at a time**: never an image build beside a host test run (P-2).
- Stop a process **by its id**, never by a pattern: PLAN-53's watch loop killed its own shell with a pattern that matched itself, and a build filled the disk.

**Waiting — for CI and for your own work.**
- **Never end the turn to wait** for CI or for work you started (a review, a build, a long check). Wait for it in the turn, as PLAN-49 and PLAN-50 did.
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
- bank account data: IBAN (ISO 13616, mod 97), local account formats, masking, audit with redaction;
- maker-checker: a second person verifies, filtered unique indexes, optimistic concurrency;
- pinning a fact at the snapshot of a calculation, so that a later change does not move a settled payment;
- effective-dated records whose new versions carry every field;
- MassTransit consumers that must not lose a message, nor loop on one;
- EF Core 10 with PostgreSQL: migrations under forced row security, a data move as the migrator;
- ASP.NET Core MVC and Web API: named policies, branch scope, antiforgery on API writes;
- architecture tests with ceiling lists that only shrink; acceptance tests on Docker Compose; PowerShell (ASCII, 5.1-compatible syntax, run by `pwsh` 7 here) and bash.

You work on the `HRManagement` repository on the owner's Linux notebook (P-2), signed in to GitHub with push rights, and you operate the demo server through `ssh hrm-vps`, as PLAN-36..54 did.

**Where things stand** when this prompt was written (2026-11-24):
- **`main` is `12b83970`:** PLAN-54 merged through PR #54 at 2026-11-24T03:19:07Z. Its merge note is on the local branch `docs/plan-54-merge-note` of the owner's machine. Read it, and PLAN-54's final report.
- **The demo server runs PLAN-54's last tag `014db7e3a01a`** (`/opt/hrm/current-tag`; deployed 2026-11-24T00:54:47Z, previous `83b99786a420`): your rollback target.
  - 22 containers; 25 GB free after that build; its last backup is `/opt/hrm/backups/pre-plan54-20261123T170449Z`.
  - PLAN-54's checks left two versions `LAW_50_2004_CHK54` (approved, attested, unpublished) and two rejected allowances: records, not to touch.
- **PLAN-54 took 19 h 52 min to its merge** (started 2026-11-23T07:26:43Z):
  - about 3 h 21 min of stops: the owner stopped two turns, the machine restarted once, and a turn ended on its own while it waited;
  - **its PR's first CI run failed in the prod-like stage:** the classes of a group run in an order that changes from run to run. `AcIam50`'s 50b adds an employment record with no service regime, which blocks every later payroll run of agency A. The fix: `AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync` before each calculation (Lessons).
- **The owner's machine** (PLAN-53 and PLAN-54 progress files):
  - Linux 6.14, 15 GB RAM; PowerShell 7.7 at `~/bin/pwsh`, with `~/bin/powershell` linked to it as CI has; .NET SDK 10.0.400;
  - `/` is 233 GB, with 9 to 50 GB free across the two plans. Docker also holds other projects' images, containers and volumes, and the owner's dev containers: never touch them;
  - the link was slow in PLAN-53 (14 KB/s to 750 KB/s) and fast in PLAN-54. Images are built one service at a time; **the migrator image through `docker compose build`** (no buildx here, so the job's `docker build` cannot read `COPY --exclude`);
  - **`scripts/ci-local.ps1` cannot run there:** the same `scripts/ci/steps.json` runs natively with `scripts/ci/run-steps.sh --all` in a fresh clone of HEAD, every step not run named, and `scripts/plan035-prepush.ps1` accepts those by name;
  - **its compiler servers go stale** (Razor parse errors on unchanged files): run the local CI steps with `UseSharedCompilation=false`, `UseRazorBuildServer=false`, `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0`, and `dotnet build-server shutdown` after such a failure;
  - **the owner's dev container `hrm-keycloak` restarts about every minute:** Chromium then meets `ERR_NETWORK_CHANGED`, and stalls over HTTP/3. The browser checks launch Chromium without QUIC and probe page by page (`Ux067PageProbe.ProbeEachAsync`);
  - port 22 reached the server without the VPN; `kubectl` is absent (`Ac42i` fails locally only); the job's Chromium step uses `--with-deps` (sudo): use the Chromium in `~/.cache/ms-playwright`;
  - a full unit run was once aborted by a test host crash while the owner's containers and a browser were up: rerun it.
- **The side PRs** about the E2E Playwright install (#40, #42), if still open: never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI today:**
  - a PR run has 15 checks (PLAN-54's first run: `CI` 42 min 24 s, `E2E` 13 min 20 s, the whole about 62 min). One acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - the required checks on `main` are `CI`, `CodeQL`, `Gitleaks`, `E2E` and `Acceptance`. The last step of `Acceptance` («Every stage job succeeded») fails when the image build or any stage failed;
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - a new acceptance class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs. The `prodlike` job runs the stages `journey`, `main`, `isolated` and `s2s42` on one stack;
  - AC-23, AC-24 and AC-25 are required (lab); `AcIam53SalaryChangeTests` and `AcPay54LegalFrameworkTests` are required (prod-like);
  - **the committed evidence of AC-23..25 is still the «Fail» of the run before PLAN-53** (D-QA-20: nothing of a local run is committed). A narrowed local run that leaves them out copies that evidence in and its guard names them: include them, or read the run by its class table;
  - **the build and test steps live in `scripts/ci/steps.json`;** `CiStepsParityTests` keeps `ci.yml` in line with it.
- **From PLAN-53 and PLAN-54** (`docs/PAYROLL_SALARY_CHANGE.md`, `docs/PAYROLL_CONFIG_VERSIONING.md`, and their reports' readiness notes):
  - the policies `Payroll.Process`, `Payroll.Approve`, `Payroll.MasterData`, `Payroll.ChangeCancel`, `Payroll.LegalAttest`, with their rules in `PayrollPolicyRules`; `PayrollWriteActionsTests` fixes each Payroll write's policy and who reaches it, role by role;
  - `IPayrollCalendar` for «today» (Damascus); `HrInvariantDecimalModelBinder`; `PayProfileChangeResult` for the HTTP answer;
  - **every API write validates an antiforgery token:** a `GET …/antiforgery` endpoint per API controller (`MessagingRepairAntiforgery.Issue`, as `PayrollFrameworksApiController` has), and `AcceptanceHarness.CreatePayrollClientAsync` for tests (its token, fetched from one Payroll endpoint, is accepted by every Payroll API write: AcPay54's imports use it);
  - Payroll's org scope is wired (`PayrollOrgScopeKeys`, the filter); `OrgScopeCoverageTests` refuses new rows;
  - `InterfaceTextGuardTests` scans every view; the prod-like and E2E stacks enforce the CSP;
  - **a calculation path** passes `PayrollInterimGate.RefusalForRun`. A run in a month an interim v2 version governs stops after 2027-01-31 (PLAN-54 §15).
- **The code today** (read on `12b83970`; find code by its content if a line moved):
  - **The bank fields on the profile** (`EmployeePayProfile.cs`: `BankAccountIban` `:47`, `BankName` `:51`, `BankAccountVerifiedAt` `:60`) are written by no code but PLAN-53's copy-forward (`EmployeePayProfileVersioning.cs:79-81`), and no seed sets them: the demo and the acceptance stacks hold none. They are read by:
    - `PaymentOrderService.EnsureOrdersForBatchAsync` (`:11-63`): the active profile's verified IBAN (`:22-29`) becomes `BeneficiaryIban` (`:52`);
    - `PayrollExternalExportService` (`:49-97`): the same, and the treasury file is refused with a count, «لا يمكن تصدير ملف الصرف: N موظف بلا حساب مصرفي محقَّق» (`:141-145`);
    - `FraudScanInputBuilder` (`:25`): FR-04's IBAN;
    - `DsarInternalController` (`:27-28`): `bankAccountPresent` and `bankAccountVerified`.
  - **PLAN-53's versioning** (`EmployeePayProfileVersioning`): `NextVersion` copies every field of the previous version, then the change; `Close` ends the previous one the day before; `StartsBefore`; `Iam053ApplierCopyForwardTests` reads the entity by reflection (`DeliberatelyNotCopied`, `SetByTheChange`).
  - **PLAN-53's applier** (`PayProfileChangeApplier.ApplySystemChangeAsync(employeeId, kind, source, sourceReference, proposedBaseSalary, proposedPayGradeId, effectiveFrom)`):
    - it refuses a source that is not automatic, an employee with no active profile, a date before the current version, a salary not above 0, and an unknown grade;
    - an automatic source skips the grade bounds and the frozen-month check; it supersedes the employee's open requests;
    - it writes one `Applied` request row and the version, and publishes `IPayProfileChangeApplied`, in one save;
    - no consumer calls it yet.
  - **The consumers** (`InboundEventHandlers.cs`):
    - `HandleContractActivatedHandler` (`:30` to about `:110`): with no active profile it creates a profile without unit, regime or bank fields, and ignores the inactive one `IEmployeeCreated` wrote. Otherwise it saves a history snapshot, ends the previous version **on** the start date (the boundary day counted twice), and creates a version without them. It writes a zero-amount `PayrollAdjustment`. **PAY-029:** no salary → an unpriced profile (inactive under `Payroll:RequireBaseSalaryOnActivation`) and a review hold `PAY029-NOSAL`; a salary outside the grade → the profile anyway, and a review hold `PAY029-RANGE` (`PaymentHold`, `Manual`, the rule code at the start of `Reason`);
    - `HandleEmployeeCreatedHandler` (`:635-653`): returns when the employee has any profile; otherwise an inactive, unpriced profile with the unit and the regime, `EffectiveFrom = DateTime.UtcNow` (not a calendar date);
    - `HandleTransferExecutedHandler` (`:284` on): the unit, the regime and the salary difference, in place (PAY-G).
  - **`PayProfileWritersTests.AllowedWriters`** (seven, never more): `PayProfileChangeApplier`, `HandleContractActivatedHandler` and `HandleEmployeeCreatedHandler` (both «PLAN-55»), `HandleTransferExecutedHandler` (PAY-G), `HandleSeparationCompletedHandler` and `HandleDisciplinaryActionHandler` (PLAN-64), `DemoPayrollSeeder`.
  - **The scope reads the plan names are already right:** `ArrearsController.cs:129-133` reads the active profile, and the PUT alias of `PayrollApiController` goes through PLAN-53's submit. Verify, and record.
  - **Snapshots and lines:** `PayrollRunJobHandlers.cs:167-179` writes one `PayrollRunInputSnapshot` per profile (profile id, `ProfileVersion`, `InputFingerprint`, the configuration's id and content hash). `ComputeProfileVersion` covers no bank field. `PayrollEmployeeCalculationUnit.Build` makes the line. `SalaryLine` has no account.
  - **Holds:** `PaymentHold` (`EmployeeId`, `PayrollRunId`, `Reason`, `HoldType` `Fraud`/`ProofOfLife`/`Manual`, `Status`, the release fields). `SecurityAudit.EventTypes.PayrollOverride` exists (manual inputs and runs write it).
  - **Names:** Payroll stores no employee name. A screen shows one through `HrEmployeeName` / `_EmployeeDisplayName` (`data-hr-employee-id`, filled by `display-names.js`).
  - **An administrators' repair endpoint already exists in HR.Core:** `RecruitmentAdminController` (`api/recruitment/admin`, `[OrgScopeCentralOnly]`, `POST repair-approvals?dryRun=true`, driven by `scripts/hcm025-repair-stuck-approvals.ps1`).
  - **Branch scope and personas:** no persona holds the `payroll` role and a branch. The branch managers carry `branch-manager` and `hr-core` (and `admin-logistics`), never `payroll`. The `IAM-H` rows of Payroll's controllers rest on that («no restricted role reaches it», D-IAM-3).
  - **The prod-like stack's payroll months:** AC-IAM-53a takes the current month; AC-PAY-54 takes 2031-01 onwards (its code governs from 2031, interim); the seed's `LAW_50_2004` v1 governs every month before 2031.
  - **Outside this plan, known:** a run that holds a non-zero `PayrollAdjustment` fails its own reconciliation (PLAN-54 §14, PLAN-56's).
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43). This plan's migration moves data (P-10): lift `FORCE` inside it, as `WF044` does, and prove it as the migrator.
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role (PLAN-54's `Pay054MigrationPostgreSqlTests` is the pattern).
  - **A new PostgreSQL test uses the connection strings of `PostgreSqlFixture`,** whose idle connections close after seconds. Make sure CI's Testcontainers filter picks up your new classes.
  - **The prod-like stack runs `ASPNETCORE_ENVIRONMENT=Acceptance`,** so it does not load `appsettings.Production.json`. A value in a Production file must be mirrored in `docker-compose.acceptance-prodlike.yml`.
  - **Personas:**
    - a persona change needs `scripts/acceptance-reset.ps1` before the first start (the personas' hash is in the seed pack's fingerprint);
    - **never write `personas.json` while an image is building** (PLAN-53 built one image again for it);
    - a new persona also changes `DATA-AUTH`; a user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh`.
  - **The classes of an acceptance group run in an order that changes from run to run.** Before any payroll calculation on the prod-like stack, call `AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync` (PLAN-54's first CI run failed without it). A class never relies on what another class did or did not do.
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - **Formatting:** before every commit, run `dotnet format whitespace HRManagement.slnx --verify-no-changes --no-restore --include <the files you changed>`. CI runs the full check.
  - **Acceptance tests:** a test posts the URL and the token the page renders (PLAN-44); an API write needs the antiforgery token (PLAN-47, PLAN-53).
  - **CodeQL runs in CI only:** a new API `POST` or `PUT` without `[ValidateAntiForgeryToken]` fails `cs/web/missing-token-validation` (PLAN-53's first CI run). Never answer it with a baseline entry or an ignore attribute.
  - **`-warnaserror` turns a use of an `[Obsolete]` member into an error** (P-6).
  - **The machine** (P-2): the disk (PLAN-53 filled it once); stale compiler servers; never delete build outputs while a step uses them; helpers outside the tree; never change `~/.ssh/config` in the middle of a run; after the host sleeps, the Docker VM's clock may lag (PLAN-50).
  - **The session may stand still** (PLAN-51: 7 h 44 min after the pre-push check, until «Try again»). Keep the progress file's next step exact, and go from the pre-push check straight to the push and the PR, in the same turn.
  - **The server:**
    - **its disk:** check the free space before the build (P-16);
    - run the server build detached (PLAN-44, PLAN-53), and the server suites with nothing heavy beside them (PLAN-45);
    - **a deploy may stop at `keycloak-reconcile` after PostgreSQL is recreated** (PLAN-40, and again in PLAN-54), leaving the application containers created and not started: run the same deploy again at once;
    - if the server build fails because the provider's resolvers answer SERVFAIL, wait and retry; never change the server's resolver configuration (PLAN-50);
    - the server suites write into the committed `acceptance-evidence/` and `reports/`: restore those files, or move them under `artifacts/plan55/vps/` (PLAN-48);
    - a server check cleans up what it leaves open, and decodes a page before it compares Arabic text (PLAN-49);
    - two older server checks fail from this machine only: `AC_POL_47_vps_the_migration…` reads a «before» file of the formatted Windows machine, and a long browser tour meets the restarting dev container. Record them; do not chase them.
  - **Before the push, and before any fix pushed to the release branch:**
    - run the unit suite (its tests scan every source file) and the format check;
    - compare the seeded counts of `scripts/acceptance-verify.ps1` on your local stack, by psql;
    - run the full Testcontainers step on the host with CI's filter;
    - a log line that writes a value built from the request is CodeQL's `cs/log-forging`;
    - a test literal that looks like a key trips gitleaks' `generic-api-key`. Write it differently rather than allowlisting it.
  - **The E2E suite runs in CI only.**
  - **The architecture project's first run after a build** takes about 2 min 25 s, outside the tests (PLAN-51).
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools, or a script that reads and writes UTF-8 without a byte order mark.
  - The acceptance services have no container health check: readiness means `/health/ready`.

## Read first (mandatory)

1. **The specification:**
   - `docs/acceptance-plans/PLAN-55-pay-bank-account-pay-profile.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
   - `docs/PAY_001_GAP_STUDY.md`: G-PAY-02, and the PAY-B row.
2. **These reports:**
   - `reports/plan54-final-report.md` (§3, §11, §12, §14, §17) and its merge note;
   - `reports/plan53-final-report.md` (§16) and `docs/PAYROLL_SALARY_CHANGE.md`;
   - `reports/plan54-progress.md`, for the machine and the CI failure of its first run.
3. **These documents:** `docs/PAYROLL_RULES.md`, `docs/PRV_001_DPIA_PAYROLL.md`, `docs/SOD_MATRIX.md`, `docs/FRD_001*` (FR-04) if present, and the rows D-PAY-11, D-PAY-14 and D-PAY-15 of `docs/OWNER_DECISIONS_REGISTER.md`.
4. The code named in «The code today».
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **No other plan session is running,** and you are in the main folder (see «How to run»), on `main` or on `docs/plan-54-merge-note`.
2. **`git fetch origin`**, and `origin/main` holds PLAN-54: the merge of `release/plan-54` is in `git log origin/main --merges --oneline -5`.
3. **The working tree is clean,** except for these, which may be present:
   - the owner's local edits to `global.json` and `tmp-vendors.html`, if any;
   - git-ignored files (`artifacts/`, `.env`, the local key rings of the dev scripts);
   - untracked prompt files under `docs/acceptance-plans/execution/`, this one included.

   Anything else uncommitted: leave it untouched, record it, and go on if it does not touch this plan's files. `BLOCKED:` only if it does. Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
4. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
   - **If port 22 is blocked** from the owner's network, this precondition does not stop P0. Record it, take the server's read-only picture at the start of P8, and go on with the local phases.
5. **`gh auth status`** shows an account with push rights to `omardefdev/HRManagement` and the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

The owner accepted the plan's defaults on 2026-10-31 (the wave 1 survey; the rows of `OWNER_DECISIONS_REGISTER.md`). If the owner changed one in a message before P0 starts, his message wins; record it.

- **D-PAY-11, the account's identifier:** an IBAN is always accepted after the ISO 13616 check (mod 97). A local account number is accepted only for a bank defined in `Payroll:Bank:Banks` (`code`, `nameAr`, `accountRegex`). The list stays empty in production until the bank file is adopted (SYD-014).
- **D-PAY-14, who registers and who verifies:** the holder of the processing policy registers; the holder of the approval policy, who is not the registrar, verifies, with the bank's confirmation reference (mandatory text). Self-service by the employee is wave 2.
- **D-PAY-15, when a run pins the account:** at the run's snapshot (`PayrollRunInputSnapshot`). An account that changes after the calculation applies from the next cycle.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-55-pay-bank-account-pay-profile` from `origin/main` (plan 0.1), in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-54's merge note from `docs/plan-54-merge-note`. If that branch is gone, rebuild the record from GitHub (the PR, its runs, the merge commit), and say so;<br>&nbsp;&nbsp;– commit this prompt file. If the owner pasted it into the session rather than saving it, write it from the session's text first, as PLAN-54 did;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **The machine (Linux): awake, the disk, Docker, the ports.**<br>• **Keep the machine awake for the session, changing no setting:** `systemd-inhibit --what=sleep:idle --who=plan55 --why="PLAN-55" sleep infinity`, detached. Record its PID in `artifacts/plan55/keepawake.pid`, and stop it **by that PID** at the end. If the machine restarts, start it again and record it.<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• **Remove only this repository's leftovers:** compose projects `hrm-*`, Testcontainers, this plan's images and this repository's build cache. Never prune, stop or remove another project's images, containers or volumes, or the owner's dev containers.<br>• **The disk:** read `df -h /` and `docker system df` before every image build and every stack start.<br>&nbsp;&nbsp;– Build an image only with at least 12 GB free. Below that, prune this repository's build cache and remove this plan's unused images first;<br>&nbsp;&nbsp;– a watch that stops a build near 2 GB free stops it **by its PID**;<br>&nbsp;&nbsp;– if 12 GB cannot be reached without touching other projects: `BLOCKED:`, one line — the owner frees space.<br>• **The images:** one service at a time from the local base images (`acceptance-up.ps1 -UseLocalBaseImages`, and the helpers PLAN-53 and PLAN-54 kept outside the tree); the migrator through `docker compose build`. Never commit a helper.<br>• **One heavy job at a time:** never an image build beside a host test run. Close what you opened before a full unit run (a browser, an idle stack).<br>• **After an image build,** check that every service image was rebuilt from this tree. A failed build leaves stale images, and a `-NoBuild` run on them proves nothing.<br>• **Before each stack start,** check the stack's host ports with `ss -ltn` and `docker ps`. A port held by something that is not this repository's: `BLOCKED:` with the port.<br>• **The local CI steps:** `scripts/ci/run-steps.sh --all` natively in a fresh clone of HEAD, with the compiler servers off (see «Where things stand»), naming each step not run and why.<br>• **If this session runs on Windows again,** use PLAN-67's forms of these checks, and record it. |
| P-3 | **The baseline (plan 0.2–0.3),** into `reports/plan55-baseline.md`.<br>• **0.2:** `dotnet build HRManagement.slnx`; the unit tests filtered on `Pay029`, `ContractActivated` and `PaymentOrder` (the plan's), then `Frd001` (FR-04), `Iam053` and `Pay054`. Green, or a list.<br>• **0.3, the inventory,** generated by search, never typed, into `artifacts/plan55/inventory.*`, and summarised:<br>&nbsp;&nbsp;– every reader and writer of `BankAccountIban`, `BankName` and `BankAccountVerifiedAt`, in code, tests, scripts and docs;<br>&nbsp;&nbsp;– every `new EmployeePayProfile`, and every other write of a profile (in place, closing, deactivating), in every project, seeds and tests included;<br>&nbsp;&nbsp;– every read of «the employee's profile» for a scope or a unit (which profile it takes);<br>&nbsp;&nbsp;– every creator and reader of `PaymentOrder`, `PaymentHold` and the treasury file, and what «disbursed» means in the code (the statuses of orders and batches);<br>&nbsp;&nbsp;– the tests that pin PAY-029 and FR-04.<br>• **One more, from this prompt:** the demo's picture (P-15). |
| P-4 | **Policies, stances and the API's surface.**<br>• **Every new action names its policy:**<br>&nbsp;&nbsp;– register → `Payroll.Process`;<br>&nbsp;&nbsp;– verify, reject, retire → `Payroll.Approve`. Retiring an account in force can leave an employee with no destination, so it takes the approval policy, with a mandatory reason;<br>&nbsp;&nbsp;– the readiness report and the lists → `Module.Payroll`;<br>&nbsp;&nbsp;– the repair (P-8) → administrators only: an existing administrators' policy that fits Payroll, as `RecruitmentAdminController` uses `HrCoreDelete`, else a `RolesPolicy` `Payroll.Repair` with default `hr-admin`.<br>&nbsp;&nbsp;`PayrollWriteActionsTests` gains each one, with who reaches it role by role.<br>• **The separation of duties** lives in the handlers: the verifier is not the registrar. The command sets `CreatedBy` from its actor, as the manual inputs do (`ManualInputCommands`). The audit interceptor fills it only when empty, and only from the HTTP context, so the check must never meet an empty registrar: an act with no known actor is refused.<br>• **The stances:**<br>&nbsp;&nbsp;– `EmployeeBankAccountsApiController` and `EmployeeBankAccountsController` scope by the employee's active profile (P-9);<br>&nbsp;&nbsp;– the repair's controller declares `[OrgScopeCentralOnly]`.<br>• **The API** (`api/payroll/employees/{employeeId}/bank-accounts`): `GET`, `POST`, `POST {id}/verify`, `POST {id}/reject`, `POST {id}/retire`, the readiness `GET api/payroll/runs/{id}/bank-readiness`, and a `GET …/antiforgery` endpoint. **Every `POST` carries `[ValidateAntiForgeryToken]`** from its first commit; so does the repair's.<br>• **The HTTP answers,** one rule everywhere:<br>&nbsp;&nbsp;– a broken input rule (an IBAN or a local number that fails its check, an unknown bank, a missing reference or reason) → 400, no row;<br>&nbsp;&nbsp;– the registrar verifying his own registration → 400 (the plan's AC-PAY-55a);<br>&nbsp;&nbsp;– a second pending account, a concurrent decision, a wrong state → 409, never a 500;<br>&nbsp;&nbsp;– an employee or account outside the caller's scope or agency → 404 (P-9).<br>&nbsp;&nbsp;Every refusal is an Arabic message, and changes nothing. |
| P-5 | **The account (plan §1).**<br>• **`EmployeeBankAccount`,** with the plan's fields, `AuditableEntity`, `ConfigureOptimisticConcurrency`, `[AuditRedact]` on the number. Filtered unique indexes: one `Active` and one `PendingVerification` per `(TenantId, EmployeeId)`.<br>• **`BankAccountNumberValidator`:** removes spaces and upper-cases; an IBAN by ISO 13616's form and mod 97; a local number by its bank's `accountRegex`.<br>&nbsp;&nbsp;– Syria is outside the IBAN registry (the plan's risk), so no length table per country is assumed: say so in the code and the report.<br>&nbsp;&nbsp;– Test IBANs are made by a test helper that computes the check digits, never a real person's account.<br>• **The commands** (`Features/BankAccounts/`): request, verify (the bank's reference mandatory; the account in force becomes `Retired`), reject (a reason), retire (a reason).<br>• **The five rules,** as the plan writes them:<br>&nbsp;&nbsp;– no `Active` account without a second person and a reference;<br>&nbsp;&nbsp;– a change is a new request, and the account in force stays in force until its replacement is verified;<br>&nbsp;&nbsp;– a bad number or an unknown bank → 400, no row;<br>&nbsp;&nbsp;– every act in `EntityAuditLog` (the number redacted) and in `SecurityAudit` as `PayrollOverride`, with no number in either;<br>&nbsp;&nbsp;– **retiring an account pinned to a run line not yet disbursed** puts a `PaymentHold` on that employee in that run, `ACCOUNT-RETIRED` at the start of its reason (PAY-029's pattern). «Disbursed» is what the inventory found in the orders' statuses: write the rule in the code and the report.<br>• **A retirement is a retirement, whatever caused it:** verifying a replacement retires the account in force, so a line pinned to that account gets the same hold. The money then waits for a person (the hold's existing release), never goes to an account the employee withdrew. Prove both ways.<br>• **The number on screen and in the API** is masked to its last four characters. Only the approver's verification page shows it whole, for comparing with the bank's confirmation. No log line, audit text or error message writes a number.<br>• **The screens:** an employee's accounts (`Index`), the accounts waiting for verification (`Pending`), the registration form (`Create`); verify, reject and retire with `data-hr-confirm` on reject and retire; names through `HrEmployeeName` and `_UserDisplayName`; the guard. |
| P-6 | **The account pinned in the run (plan §2).**<br>• **`PayrollRunInputSnapshot.BankAccountId`:** the employee's `Active` account at the snapshot (`PayrollRunJobHandlers.cs:167-179`). **`SalaryLine.BankAccountId`:** copied from the snapshot when the line is built.<br>• **The account is not an input of the amount:** it stays out of `InputFingerprint` and `ProfileVersion`, so the determinism tests and AC-21 do not move.<br>• **The readers** take the line's account, never the profile and never «the current account»:<br>&nbsp;&nbsp;– `PaymentOrderService` (`BeneficiaryIban` = the pinned account's number; a local number goes there too, recorded);<br>&nbsp;&nbsp;– the treasury file of `PayrollExternalExportService`, whose refusal now lists the employees without a pinned account (their ids in the API; their names on the screen), not a count. A small, temporary change: PLAN-58 replaces this export with a file built from the orders;<br>&nbsp;&nbsp;– FR-04 (`FraudScanInputBuilder`);<br>&nbsp;&nbsp;– the DSAR answer (`DsarInternalController`): present and verified, from the account entity.<br>• **The readiness report** (`GET api/payroll/runs/{id}/bank-readiness`, and a card on the run's page): the lines with a pinned account, and the lines without one, by employee.<br>• **A line without an account** blocks neither the calculation nor the approval (the plan's §2, rule 2): the readiness report shows it, and the disbursement path is PLAN-58's.<br>• **The profile's three bank fields become `[Obsolete]`** and nothing reads them. Under `-warnaserror` a use of an obsolete member is an error, so nothing but the entity names them: EF maps them by convention, the migration moves their data in SQL, and the copy-forward stops carrying them (they move to `DeliberatelyNotCopied`, with the reason). **The columns stay:** PLAN-54's tag reads them after a rollback. |
| P-7 | **Pay profile versions that keep the unit and the regime (plan §3).**<br>• **`EmployeePayProfileVersioning.CreateNextVersion`** (PLAN-53's helper, extended, never rebuilt) is the only place a new profile is made:<br>&nbsp;&nbsp;– it starts from the active profile, else the employee's latest profile, active or not (the new part); with none, it makes the first one;<br>&nbsp;&nbsp;– it carries the unit and the regime unless the source gives new ones, then every other field (PLAN-53's copy-forward);<br>&nbsp;&nbsp;– it closes the previous version the day before (never on the start date), and writes the history snapshot;<br>&nbsp;&nbsp;– calendar dates are stored at 00:00 UTC and taken from `IPayrollCalendar`, never `DateTime.UtcNow` (PLAN-67's rule).<br>• **`HandleContractActivatedHandler` goes through `ApplySystemChangeAsync`** (`Source=ContractActivated`, the contract's id in `SourceReference`), so each activation is an `Applied` request row. What the applier needs for it, for this source only:<br>&nbsp;&nbsp;– **a first activation:** no active profile, but the inactive, unpriced one `IEmployeeCreated` wrote, or none. That placeholder never held a day, so the activation replaces it whatever its date;<br>&nbsp;&nbsp;– **PAY-029 stays exactly as it is,** its tests unchanged: an unpriced profile when no salary is known (inactive under `RequireBaseSalaryOnActivation`), a salary outside the grade accepted, each with its review hold; the zero-amount adjustment as before;<br>&nbsp;&nbsp;– **one save** for the version, the request row, the event, the adjustment and any hold (PLAN-40);<br>&nbsp;&nbsp;– **a refusal never loses the message and never loops on it:** a date before the current version, say, raises a review hold of PAY-029's pattern with the reason and writes nothing else, and the message is consumed, since no retry changes the answer. Record each refusal kind;<br>&nbsp;&nbsp;– a message delivered twice changes nothing the second time. Prove it.<br>• **`HandleEmployeeCreatedHandler`:** its first, inactive, unpriced profile goes through `CreateNextVersion`. When the event arrives after the activation, it fills the active profile's unit and regime **if they are empty,** and never overwrites a value a later event set.<br>• **The transfer** stays in place, as today (the plan's §3, rule 2; its double financial effect is PAY-G's).<br>• **`PayProfileWritersTests`** (no second architecture test, the plan says):<br>&nbsp;&nbsp;– no `new EmployeePayProfile` in `Payroll.Web`, `Payroll.Application` or `Payroll.Infrastructure` outside `CreateNextVersion` and the seeds;<br>&nbsp;&nbsp;– `HandleContractActivatedHandler` leaves `AllowedWriters`; `HandleEmployeeCreatedHandler`'s reason becomes what it does now. The list only shrinks.<br>• **The scope reads** (the plan's two are already right): every read the inventory finds of «the employee's unit» takes the active profile. |
| P-8 | **The repair of old profiles.**<br>• `POST api/payroll/admin/backfill-profile-orgunit?dryRun=true`, and `scripts/pay055-backfill-profile-orgunit.ps1`, in the manner of `RecruitmentAdminController` and `scripts/hcm025-repair-stuck-approvals.ps1` (credentials from parameters or the environment, never stored or printed; the antiforgery token fetched first).<br>• **What it does:** for each active profile whose unit or regime is empty, it copies the value from the employee's latest profile that carries it. It never overwrites a value, and touches nothing else.<br>• **`dryRun` is the default:** it reports the counts and the profile ids and changes nothing. A real run changes exactly what the dry run listed, writes one audit entry, and is idempotent (a second run finds nothing).<br>• **One agency per call:** the caller's. |
| P-9 | **Branch scope, with no branch-restricted payroll role.**<br>• No persona holds `payroll` and a branch (D-IAM-3), and **this plan adds none:** one would make every `IAM-H` row of Payroll untrue at once.<br>• **The plan's branch parts** — 55b's «the branch manager sees it in the pay profile list», and 55e's «an employee outside the user's branch» — are proven by HTTP tests with a restricted principal, as PLAN-52 and PLAN-53 did (`Iam052ScopeHttp`'s pattern): in scope, allowed; outside, absent and 404; a central user sees every unit.<br>• **The answer outside the scope is 404,** PLAN-52's rule, not the plan's 403: an account's existence is not revealed. Record the deviation.<br>• **Acceptance proves what real people can:** the version carries the unit and the regime; another agency's processor gets 404. |
| P-10 | **The migration `PAY055_EmployeeBankAccounts`.**<br>• **One migration** in `Payroll.Infrastructure`: the table with `TenantId` and its indexes (its concurrency token is PostgreSQL's `xmin`, through `ConfigureOptimisticConcurrency`: no column), row security through `TenantRlsMigrationHelper` and the grants (as `PAY054`); `BankAccountId` on the snapshots and the lines, with a `Restrict` relation.<br>• **The data move:** every profile with a non-empty `BankAccountIban` gets one account `PendingVerification` (never an implicit verification), with its bank's name. That `INSERT … SELECT` reads a tenant table under forced row security, so **lift `FORCE` inside the migration as `WF044` does,** or it moves nothing and says nothing.<br>• No byte order mark; `dotnet ef migrations has-pending-model-changes -p src/Services/Payroll/Payroll.Infrastructure -s src/Services/Payroll/Payroll.Web` reports none.<br>• **`Pay055MigrationPostgreSqlTests`** (PLAN-54's `Pay054MigrationPostgreSqlTests` is the pattern): as the migrator, a non-superuser under forced row security, a planted IBAN in two agencies moves to one pending account each, in its own agency; another agency sees nothing. |
| P-11 | **The bank list `Payroll:Bank:Banks`.** Empty in Production and Staging (D-PAY-11), mirrored as empty in `docker-compose.acceptance-prodlike.yml`. The local path is proven by unit tests with a configured list. On the demo and the prod-like stack only an IBAN is accepted, and a local number answers 400 with an Arabic message that its bank is not defined (the plan's §1, rule 3). |
| P-12 | **Acceptance placement (PLAN-41's rules).**<br>• **`AcPay55BankAccountTests`** (AC-PAY-55a..e): `prodLike[]`, `required: true`, group `main`; `[RequiredAcceptance]` and `EvidenceRecorder("AC-PAY-55")`. It does not stop or recreate containers, and uses the stack's defaults (the plan's «after recreating payroll-web» is a local convenience).<br>• **Its steps run in order,** as `AcIam53`'s do. Its employees are ones no other prod-like class touches (search the classes for the ids they use).<br>• **Its runs:** two consecutive months the seed's v1 version governs, before 2031 and not the current month (so the interim gate and 2027-01-31 never reach them, and no other class takes them), with no non-zero adjustment in them. `ClassifyRecordsAddedSinceTheSeedAsync` before each calculation.<br>• **55a:** a valid IBAN by the processor → `PendingVerification`; the approver verifies with a reference → `Active`. Negative: the registrar verifies → 400; a wrong check digit → 400 and no row; a second pending account → 409.<br>• **55b:** a salary change through PLAN-53's request and approval → the new active version has the unit and the regime of the previous one (P-9 for the branch list).<br>• **55c:** a calculation → the line carries `BankAccountId`, and the readiness report counts it. Negative: a replacement verified after the calculation → the line keeps the first account (and gets P-5's hold); the next month's run takes the new one.<br>• **55d:** retiring the pinned account before disbursement → an `ACCOUNT-RETIRED` hold on that employee in that run.<br>• **55e:** an employee without an account is listed in the readiness report (by id; on the run's page, by name). Negative: agency B's processor asking for agency A's employee's accounts → 404 (P-9).<br>• **A rerun on the same stack** may find its months taken: its precondition fails with a clear message, and a rerun needs a fresh stack.<br>• **`ac-ownership.json`:** `AC-PAY-55` (owner PLAN-55, `provesClaim: true`, and its reason). The scenarios of the personas it uses gain AC-PAY-55 (Lessons: reset).<br>• **The evidence of record is the PR run's artifact,** not `acceptance-evidence/AC-PAY-55/`. Copy what the report needs into `artifacts/plan55/`, then restore the committed evidence. |
| P-13 | **Local testing (owner's rule of 2026-11-06).** Run only the classes this plan touches, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`, and the overlay groups with `-Stage prodLike:<group>`. **The full suite runs once, in CI.**<br>• **prod-like:** `AcPay55BankAccountTests`, `AcIam53SalaryChangeTests`, `AcPay54LegalFrameworkTests`, `AcIam51ExplicitPolicyTests`, `AcIam52BranchScopeTests`, `AcUx67ReadableInterfaceTests` (with the accounts' pages and the run's readiness card added to its key pages), `AcHcm25ProductionApprovalTests`, the prod-like journey, and the `isolated` group;<br>• **lab:** `Ac21PayrollDeterminismTests`, `Ac23PayrollFreezeTests`, `Ac24PartialDisbursementTests`, `Ac25FraudHoldTests` (FR-04 and the holds), `AcHcm29HireContractTests` (a hire's contract activation now goes through the applier), the lab journey;<br>• **leastpriv:** `AcOps38FirstBootTests` and `AcTen48LeastPrivilegeTests` (the migration);<br>• **E2E:** in CI only.<br>Then compare the seeded counts of `scripts/acceptance-verify.ps1` by psql. Known local-only failures (`Ac42i` without `kubectl`) are recorded, not chased. |
| P-14 | **Scripts.**<br>• Every new or changed `.ps1` is ASCII-only, 5.1-compatible in syntax, and runs here with `pwsh -NoProfile -File …`.<br>• **`scripts/pay055-validate.ps1`** has a `-SelfTest` with planted defects, as `pay054-validate.ps1` does. It checks, at least:<br>&nbsp;&nbsp;– no reader of the three obsolete profile fields outside the entity;<br>&nbsp;&nbsp;– no `new EmployeePayProfile` outside `CreateNextVersion` and the seeds;<br>&nbsp;&nbsp;– the migration has no byte order mark, lifts `FORCE` around its data move, and makes moved accounts pending;<br>&nbsp;&nbsp;– `Payroll:Bank:Banks` empty in Production and Staging and in the prod-like mirror;<br>&nbsp;&nbsp;– every `POST` of the new controllers with `[ValidateAntiForgeryToken]`;<br>&nbsp;&nbsp;– `AcPay55BankAccountTests` required in `prodLike[]`.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile <ProdLike or Lab> -UseLocalBaseImages`. |
| P-15 | **The demo server facts this plan must respect.**<br>• **It runs Production in demo mode** (`Deployment__Mode=demo`): `hradmin` keeps the payroll policies; `e2e-payroll-processor` and `e2e-payroll-approver` are the two people of a server check.<br>• **P0's read-only picture:**<br>&nbsp;&nbsp;– the running tag and the free disk;<br>&nbsp;&nbsp;– the profiles with a bank field (expected none), the active profiles with an empty unit or regime (the count the repair will meet), the payment orders and their IBANs, the holds per type;<br>&nbsp;&nbsp;– a checksum of the active profiles (id, unit, regime, salary, grade), never printing an amount.<br>• **What the owner will see change:**<br>&nbsp;&nbsp;– a bank accounts screen per employee and a list waiting for verification; numbers masked;<br>&nbsp;&nbsp;– a run's page shows its bank readiness;<br>&nbsp;&nbsp;– a contract activation's new pay profile keeps its unit and regime;<br>&nbsp;&nbsp;– nothing else moves: no account is verified on the demo, and no pay datum changes but the repair's filled units. |
| P-16 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan55-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Before the deploy, read-only:** P-15's picture.<br>• **The disk:** at least 30 GB free before the build. Otherwise remove the server's build cache and the local images of older releases. They stay in the loopback registry `127.0.0.1:5050` (undo: `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>`). Record each removal.<br>• **Build once,** on the final code, detached; then `scripts/vps/deploy.ps1 -Tag <sha12>` (run it again at once if it stops at `keycloak-reconcile`). The migration runs at start-up.<br>• **The repair on the demo:** the dry run first, recorded; then the real run, only for what the dry run listed. Save the changed profile ids with their former (empty) values beside the backup: that list is the undo.<br>• **No account the checks make stays usable:** each one ends `Rejected` or `Retired`, and no run or order of the demo is touched.<br>• **Rollback safety:** find out what PLAN-54's tag does with this plan's data, and record it in `OPS_VPS_SINGLE_HOST.md`:<br>&nbsp;&nbsp;– it never reads the accounts table or the new columns;<br>&nbsp;&nbsp;– it reads the profile's bank fields again (empty), as before;<br>&nbsp;&nbsp;– its contract activation writes versions without unit and regime again.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn:<br>&nbsp;&nbsp;– roll back to PLAN-54's tag, or fix forward;<br>&nbsp;&nbsp;– restore the backup only if data was damaged;<br>&nbsp;&nbsp;– prove the demo works, and record the downtime. |
| P-17 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P9, after the local CI steps and the pre-push check are green, in the same turn as the pre-push check (Lessons).<br>2. `gh pr create`, then wait («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, run the unit suite and the format check, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>`, and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-18 | **No regression of PLAN-25, PLAN-36..54, PLAN-66 and PLAN-67.** These stay green:<br>• **every existing payroll result, unit for unit:** the golden pack tests (`Gov002LegalGoldenPackTests`), `PayrollEnginePipelineTests`, the determinism tests, `Pay029*`, `Pay023FrozenRunIsolationTests`, AC-21, AC-23, AC-24 and AC-25;<br>• the fraud tests `Frd001*`, whose FR-04 now reads the pinned account;<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests`;<br>• `AcIam50`..`AcIam53`, `AcPay54`, `AcUx67`, `AcWf43`..`AcWf46` with the orphan class, `AcPol47`, both `AcTen48` classes, both `AcPol49` classes, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, `AcHcm29`, and both recruitment journeys;<br>• the architecture tests (`PayrollWriteActionsTests`, `PayProfileWritersTests`, `PayrollReferenceWritersTests`, `Pay054FrameworkWritersTests`, `OrgScopeCoverageTests`, `InterfaceTextGuardTests`, `GuardSelfTests`), `Iam053ApplierCopyForwardTests`, `OutboxPublishOrderingArchitectureTests`, `Rel004CompatibilityTests`, `ProductionWiringMatrixTests`, `CiStepsParityTests`;<br>• `scripts/pay054-validate.ps1`, `iam053`, `iam052`, `iam051`, `iam050`, `ux067`, `pol047`, `pol049` validate scripts, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-58:** leaving an employee without an account out of the batch and the file, and the unified disbursement path.
- **PLAN-53, done:** the salary change transaction. This plan only uses its applier.
- **Wave 2:** the employee registering his own account (self-service) and automatic verification with the bank (PAY-I, PAY-J, D-PAY-9); encrypting the account number at rest (SOV); the double financial effect of a transfer or a secondment (PAY-G, G-MOV-04).
- **`IAM-H`:** a branch-restricted payroll role, and the branch scope of the payroll screens that do not have one.
- **PLAN-56:** the reconciliation of a run with a non-zero adjustment.

**Never commit:**
- the owner's local edits (`global.json`, `tmp-vendors.html`) — check `git diff --cached --name-only` before every commit;
- `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- another plan's untracked prompt file, or a helper of the local image builds;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- a screenshot or any other image;
- any secret, token, password, or a real person's account number (templates keep their placeholders, `__SVC_SECRET__` or `${…}`).

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- touch the side PRs (#40, #42) or their branches;
- disable host-key checking;
- widen a policy, a scope or a role to make a page or a test work, or add a branch-restricted payroll persona;
- add a row to any ceiling list: they only shrink;
- verify an account implicitly (in a migration, a seed, a repair), or let one person register and verify;
- write an account number into a log, an audit text, an error message or a URL;
- drop the profile's bank columns;
- change a payroll amount or a PAY-029 behaviour;
- verify an account on the demo, or change its pay data beyond what the repair's dry run listed;
- touch another project's Docker images, containers or volumes, or the owner's dev containers;
- print a password, or rotate a secret that exists on the server;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** (port 22 at P0 excepted: Preconditions 4).
- **PAY-029's behaviour, or an existing payroll result, would change** to put the contract activation through the applier, and the cause cannot be removed without changing the plan's design.
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning.
- **12 GB free for an image build cannot be reached** without touching other projects, **or a port the tests read is held** by something else (P-2).
- **Port 22 is still blocked when P8 starts:** one line asking the owner to restore it.
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, keep-awake, preconditions, baseline

1. Write the `Started (UTC)` line (see «Timing»). Start the keep-awake process (P-2). Check the Preconditions.
2. `git switch -c feature/plan-55-pay-bank-account-pay-profile origin/main`. Bring in PLAN-54's merge note, and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan55-baseline.md` (P-3), and the demo's picture (P-15).
4. If port 22 is blocked, record it, and take the demo's picture at the start of P8.

Commit: `chore(pay055): baseline, the readers of the profile's bank fields, every pay profile writer, the demo's picture`.

## P1 — The account, its migration, and its rules (plan §1, P-4, P-5, P-10, P-11)

- **The entity, the validator, the commands, the migration** with its data move.
- **The API** with its antiforgery endpoint, **the screens,** the audit and the masking.
- **Tests:** `Pay055BankAccountNumberValidatorTests` (IBAN check digits, spaces and case, the local regex, an unknown bank); `Pay055BankAccountTests` (every rule and refusal, the state unchanged on each); `Pay055MigrationPostgreSqlTests`; the policies in `PayrollWriteActionsTests`; the antiforgery answers; the interface guard; `OrgScopeCoverageTests`.

Commit: `feat(pay055): an employee's bank account is registered by one person and verified by another against the bank's reference; migration PAY055 moves old account numbers as pending`.

## P2 — The account pinned in the run (plan §2, P-6)

- **The snapshot and the line** carry the account; **the orders, the treasury file, FR-04 and the DSAR** read it; the readiness report and card; the holds of P-5.
- **The profile's bank fields** become obsolete and unread.
- **Tests:** a replacement after the snapshot leaves the line and its orders on the first account; the treasury file of a posted run carries the pinned account (`Pay055BankAccountPostgreSqlTests`, one of its three); FR-04 sees a shared account; the holds both ways; the determinism tests unchanged.

Commit: `feat(pay055): a run pins each employee's bank account at its snapshot; payment orders, the treasury file and FR-04 read the pinned account`.

## P3 — Versions that keep the unit and the regime (plan §3, P-7)

- **`CreateNextVersion`;** the contract activation through `ApplySystemChangeAsync`; a late `IEmployeeCreated`; `PayProfileWritersTests`.
- **Tests:** `Pay055ProfileVersioningTests` (first version, latest inactive, carried unit and regime, the closing date, the placeholder); the PAY-029 tests unchanged; a refusal consumed with its hold; a message delivered twice; in `Pay055BankAccountPostgreSqlTests`, `IEmployeeCreated` then `IContractActivated`, and the reverse order, each leaving an active profile with the unit and the regime; the scope HTTP tests (P-9).

Commit: `feat(pay055): every new pay profile version keeps the unit and the regime; the contract activation goes through PLAN-53's applier`.

## P4 — The repair (P-8)

- **The endpoint and the script,** dry run first.
- **Tests:** a dry run changes nothing and lists what a real run then changes, exactly; nothing is overwritten; a second run finds nothing; administrators only.

Commit: `feat(pay055): a dry-run-first repair fills the empty unit and regime of active pay profiles from the employee's own history`.

## P5 — Concurrency and the PostgreSQL proofs (plan's integration tests)

- **`Pay055BankAccountPostgreSqlTests`** complete: two concurrent verifications of two accounts of one employee (one wins, the other 409, through the filtered index and `xmin`); the activation orders; the treasury file of a posted run.
- **The full integration filter** on PostgreSQL, CI's filter.

Commit: `test(pay055): PostgreSQL proofs of concurrent verification, of event order on pay profiles, and of the pinned account in the treasury file`.

## P6 — Acceptance, then the independent review (plan Acceptance, P-12, P-13, P-14)

1. **The pieces:** `AcPay55BankAccountTests`, `ci-scope.json`, `ac-ownership.json`, the personas' scenarios, `scripts/pay055-validate.ps1`.
2. **Acceptance.**
   1. Check the disk (P-2). Reset the local stacks (the scenarios). Build the images, one service at a time. Start the prod-like stack, and record the time to ready.
   2. Run `AcPay55BankAccountTests` (AC-PAY-55a..e), positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   3. Then P-13's lists, `scripts/pay055-validate.ps1` (and `-SelfTest`), and the seeded counts by psql. All required classes must pass.
   4. `down -v`. Restore the committed evidence.
3. **The full Testcontainers step on the host,** with CI's filter.
4. **Independent review** (one fresh subagent), before the server, so the server build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - an account made `Active` without a second person and a reference, or by a migration, a seed or a repair;
   - a payment order, a treasury row or a fraud check that reads anything but the line's pinned account;
   - a change of account after the snapshot that moves a run's money, or a retired account that can still be paid without a hold;
   - an account number in a log, an audit text, an error, a URL or an unmasked page;
   - a pay profile made outside `CreateNextVersion`, a version without the unit or the regime when one was known, a day counted twice;
   - a consumer that can lose an activation or loop on one, or a PAY-029 behaviour that changed;
   - a request or account visible outside the caller's scope or agency, an API write without an antiforgery token;
   - a migration that moves nothing under forced row security;
   - a test changed to pass rather than to keep its meaning, or a part of the plan deferred, skipped or listed to save time;
   - something PLAN-54's tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(pay055): AC-PAY-55a..e pass on the prod-like stack; review fixes; CI scope and ownership`.

## P7 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to `PayrollDbContext` after yours, remove yours and add it again on top. Rebuild, and rerun the unit, architecture and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you.

Commit (only if the merge needed fixes): `fix(pay055): main merged, conflicts resolved`.

## P8 — The demo server (P-15, P-16)

Record every server change and its undo command **as you make it**.

1. Check SSH and P-16's condition.
2. Take the backup, then **verify** it. Take the read-only picture (P-15), if P0 could not.
3. **Check the server's disk, build and deploy.** Run the build once, detached, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`, again at once if it stops at `keycloak-reconcile`. Record the downtime and the time to all healthy.
4. **The repair:** its dry run, recorded; then the real run of exactly that list, with the undo list saved (P-16).
5. **Then check**, with evidence in `artifacts/plan55/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them. The new checks go in an `AcPay55VpsChecks` class; each writes realistic Arabic text, keeps its marker in a field the reader does not rely on, and leaves nothing open.
   1. **The migration:** PAY055 recorded; the table under forced row security; no account moved (the demo had no number); the profiles' checksum as in the picture, but for the units and regimes the repair filled.
   2. **An account:** `e2e-payroll-processor` registers a test IBAN for a demo employee → pending, masked; his own verification → 400; a bad check digit → 400, no row; `e2e-payroll-approver` verifies it with a reference → `Active`, then retires it with a reason → `Retired`. No run pinned it.
   3. **A second account** registered and then rejected with a reason → `Rejected`.
   4. **A run's readiness report** for an existing run of the demo: every line listed as without an account. Read-only.
   5. **The screens** open in Arabic under the enforced CSP with no violation (PLAN-67's method, Chromium without QUIC, page by page), with names, not ids, and the number masked.
   6. **Everyone else is unchanged:** the PLAN-36..54 server checks pass, and `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass. The AC-WF-46 sweeps check is rerun alone after a sweep cycle.
6. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-16 says, prove the demo works, and record it.

Commit: `test(pay055): demo server with bank accounts, the pinned readiness and the profile repair; PLAN-36..54 server checks still green`.

## P9 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/PAYROLL_RULES.md`: the account, its two people, its pinning, the readiness report, the holds;
   - `docs/PAYROLL_SALARY_CHANGE.md`: the contract activation is an automatic source now; `CreateNextVersion`;
   - `docs/PRV_001_DPIA_PAYROLL.md`: the account number (where it lives, who sees it whole, the masking, the redaction; encryption at rest is wave 2);
   - `docs/SOD_MATRIX.md`: register and verify;
   - the «الحالة» line of G-PAY-02 in `docs/PAY_001_GAP_STUDY.md`;
   - `docs/CI_AND_TESTING.md`: the new class; `OPS_VPS_SINGLE_HOST.md`: what the demo users see, the repair and its undo, the backup location, the change table, the rollback note;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local gates, before any push:**
   - the full Testcontainers step on the host, with CI's filter (P6, if nothing it builds or tests changed since; otherwise again);
   - the local CI steps (P-2) on HEAD;
   - then `pwsh -NoProfile -File scripts/plan035-prepush.ps1`.

   All must be green, with every step not run named. The report is committed before them, because a commit after a green local run needs another run.
3. **`reports/plan55-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations (404 for out-of-scope, the retirement by replacement, the retire policy, anything else). No deviation defers part of the plan.
   4. **The account:** its states, rules, policies, masking and audit.
   5. **The pinning:** what each reader takes now; the readiness report; the holds.
   6. **The versions:** `CreateNextVersion`, the contract activation through the applier, PAY-029 kept, the refusals and their holds, the late `IEmployeeCreated`.
   7. **The repair:** what it found and changed, on the stacks and on the demo.
   8. The existing tests that changed, each with the meaning it keeps; the numbers that did not move.
   9. The guards and their own negative tests.
   10. Unit, architecture, integration and acceptance results; the PR run's artifact link as the evidence of record.
   11. **The server:** before and after, changes and their undo, the backup location, the rollback note.
   12. **Changes for API clients and operators:** the new API, the repair and its script.
   13. Items that other plans own (Scope boundaries), and defects for later plans.
   14. **Timing:**
       - total wall-clock;
       - active time, waiting for CI, and every stop with its cause;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `pwsh -NoProfile -File scripts/plan-timing.ps1 -From 25 -To 67`.
   15. **A readiness note** for PLAN-56..59: the pinned account on the line (PLAN-58's disbursement), the automatic sources of a version, and the holds a retirement makes.
4. **Merge (P-17), by you, in the same turn as the pre-push check:**
   1. Merge the feature branch into a new `release/plan-55` from `main` (`--no-ff`).
   2. Write `reports/plan55-pr.md` (short).
   3. `git push -u origin release/plan-55`
   4. `gh pr create --base main --head release/plan-55 --title "PLAN-55: the employee's bank account, and pay profile versions that keep the unit and the regime" --body-file reports/plan55-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then:
   - `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`;
   - record the PR link, each check result, the merge commit and the post-merge run in `reports/plan55-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`;
   - commit that note on a local branch `docs/plan-55-merge-note`, as the plans before did, so the next plan brings it in. **Do not open a second PR for it;**
   - stop the keep-awake process by its PID (P-2).

## Definition of done

- **A bank account is registered and verified by two people,** pinned in the run, and reaches the payment orders and the treasury file.
- **A change of account after the snapshot moves no money of that run;** a retired account that a line still holds is behind a hold.
- **No pay profile version lacks the unit and the regime when they are known,** and the writers' test refuses a version made outside `CreateNextVersion`.
- **FR-04 works on real accounts.**
- **No account number reaches a log, an audit text, an error or an unmasked page.**
- **Every existing payroll result and every PAY-029 behaviour is unchanged.**
- **Nothing in the plan was deferred to save time.**
- **The tests:** AC-PAY-55a..e = Pass (the PR run's artifact is the evidence of record); the three PostgreSQL integration tests green; the PLAN-36..54 suites green.
- **The server** runs this code; its old profiles repaired from the dry run's list; no account verified there.
- **The documents** P9 lists are updated, the «الحالة» line of G-PAY-02 among them.
- **The merge** is done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed for payroll users, and what the owner will see on the demo;
- what the repair found and filled on the demo;
- how long the plan took, how long it waited for CI, and every stop with its cause;
- the PR link;
- the path of the final report.
