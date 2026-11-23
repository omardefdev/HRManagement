# Task: PLAN-54 — The payroll legal framework and its reference data in production (PAY-A)

The deliverables:
- **An agency on production settings (`Seed:Profile=None`, no seed) reaches its first payroll calculation:**
  - it creates a legal framework, or imports it from a JSON pack;
  - three different people approve, attest and publish it;
  - then the first run calculates.
- **Every value that governs pay lives inside the framework version pinned on the run, and inside its fingerprint:**
  - tax brackets, statutory deductions, holidays and disbursement priority rules, each with its legal reference and status;
  - no general table that anyone can edit decides a run of a new version.
- **An interim value is published only under an active STR-002 exception,** and a calculation stops once that exception has expired.
- **A manual allowance, deduction or adjustment counts only after a second person approves it.**
- **Every version and every input that exists today computes exactly as before.**
- **The demo server** runs it.

The final legal values stay out (D-PAY-1, wave 2: PAY-E): this plan builds the container and binds it to governance.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P9** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**Why this plan exists** (`docs/PAY_001_GAP_STUDY.md`):
- **G-PAY-01:** there is no way to create a framework in production. The seed exists in Demo and Acceptance only, so a production agency's first calculation is refused.
- **G-PAY-10 (the part PLAN-53 left):** statutory deductions and holidays sit in general tables outside the version and its fingerprint, and manual inputs count without a second person.
- **G-PAY-12 (the structure):** interim values carry no reference to the exception that allows them.
- **G-PAY-09 (the framework's part):** approve, attest and publish take the same policy, and publishing accepts a draft.

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder. There is no second clone and there are no locks.
- **The main folder** is `/home/omar/HRManagement` on the owner's Linux notebook, where PLAN-53 ran after the Windows machine was formatted (owner, 2026-11-22). If the owner opened this session elsewhere, that folder is the main folder: record it. A folder's path is never a reason to stop.
- **PLAN-53 is merged** (Preconditions).

**Complete execution** (owner, 2026-11-12, standing since PLAN-51): the owner wants every plan carried out in full, however long it takes.
- Time is never a reason to skip, defer or allowlist anything.
- Only what the plan itself assigns elsewhere stays out: its «Out of scope».
- Where the plan speaks in general, this prompt makes it concrete, and all of it is in scope:
  - **creation, import, export and the effective date,** through the API and the screens, with the operator's script;
  - **the four tables inside the version** (brackets, statutory deductions, holidays, priority rules) and the references of the numeric fields, in the engine, the export and the fingerprint v2;
  - **which version governs a period** once publishing respects effective dates (P-6);
  - **the four acts and three people** of the framework's cycle, the interim gate at every act and at the start of a run, and the exceptions' expiry;
  - **the three manual inputs** (allowances, deductions, adjustments), with every reader of them;
  - **every new action names its policy, every new controller declares its org-scope stance** (PLAN-52), **every new view passes PLAN-67's interface guard,** and **every new API write validates an antiforgery token** — each from its first commit;
  - **AC-PAY-54a..f,** positive and negative, the empty-database integration test, and the regression the plan names.

**Standing owner instruction on merges** (PLAN-38 to PLAN-53, PLAN-66 and PLAN-67): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-18).
- **Never ask the owner whether to merge:** his instruction is the permission. Asking cost PLAN-51 2 h 09 min.

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Check formatting on the files you changed (see «Lessons»).
4. Commit, using the phase's commit message.
5. Update **`reports/plan54-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan54-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan54-progress.md`. Its first line is:
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
- Write long outputs (inventories, logs, trx files) under `artifacts/plan54/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.
- Build the acceptance images once per code state. Run the unit and integration filters before a stack run, and rerun only the classes that failed.

**Long commands.** A foreground command that outlives the tool's time limit ends the turn. So:
- Run anything that can outlive the limit in the background or detached, with its log under `artifacts/plan54/`, and poll it. That covers the unit suite, the Testcontainers step, an image build, a stack start, an acceptance chain, the local CI steps and the server build.
- Run **one heavy job at a time**: never an image build beside a host test run (P-2).
- Stop a process **by its id**, never by a pattern: PLAN-53's watch loop killed its own shell with a pattern that matched itself, and a build filled the disk (P-2).

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

You are a senior .NET engineer for payroll systems that must be correct, auditable and lawful:
- effective-dated configuration versions, content fingerprints, and the rule of which version governs a period;
- legal governance: maker-checker with three people, legal references, interim values under dated exceptions;
- validated JSON import, atomic and idempotent;
- EF Core 10 with PostgreSQL: migrations under forced row security, defaults for existing rows;
- ASP.NET Core MVC and Web API: named policies, antiforgery on API writes, file uploads;
- a calculation engine whose existing results must not move by a single unit;
- architecture tests with ceiling lists that only shrink; acceptance tests on Docker Compose; PowerShell (ASCII, 5.1-compatible syntax, run by `pwsh` 7 here) and bash.

You work on the `HRManagement` repository on the owner's Linux notebook (P-2), signed in to GitHub with push rights, and you operate the demo server through `ssh hrm-vps`, as PLAN-36..53 did.

**Where things stand** when this prompt was written (2026-11-23):
- **`main` is `53690e0e`:** PLAN-53 merged through PR #53 at 2026-11-23T04:37:42Z.
  - PLAN-53's merge note is on the local branch `docs/plan-53-merge-note` of the owner's machine. Read it, and PLAN-53's final report.
  - PR #52 (the dev scripts on Linux, by the owner) merged before it.
- **The demo server runs PLAN-53's last tag** (`/opt/hrm/current-tag`, named in the merge note: `27cbf35db9cc` at the report, then the payroll image again for the CodeQL fix): your rollback target. It had 22 containers healthy and 32 GB free; its last backup is `/opt/hrm/backups/pre-plan53-20261122T224310Z`.
- **PLAN-53 took 52 h 22 min to its merge,** of which 38 h 41 min were stops:
  - 36 h 16 min `BLOCKED:` on the preconditions: the prompt named the Windows folder, and the Linux machine had no push rights and no server access until the owner added its key;
  - 2 h 25 min: a stopped command and a restart;
  - **its PR's first run failed CodeQL, rightly:** five `cs/web/missing-token-validation` findings (severity 8.8) on the new API's POST actions. Fixed with a token endpoint and `[ValidateAntiForgeryToken]`, then built and deployed again. The local gates cannot see CodeQL.
- **The owner's machine,** from PLAN-53's progress file (P0, P5, P6, P9):
  - Linux 6.14; PowerShell 7.7 at `~/bin/pwsh`, with `~/bin/powershell` linked to it as CI has; .NET SDK 10.0.400;
  - `/` is 233 GB with **9 to 26 GB free** during PLAN-53. Docker also holds other projects' images, containers and volumes: never touch them;
  - **a slow link** (14 KB/s to 750 KB/s). The registry's SDK image could not be pulled. The acceptance images were built one service at a time from local base images (`scripts/acceptance-up.ps1 -UseLocalBaseImages`, and the helper PLAN-53 kept outside the tree);
  - **`scripts/ci-local.ps1` cannot run there** (its pinned SDK image, and its «Verify Docker builds» step builds every image at once). The same `scripts/ci/steps.json` ran natively with `scripts/ci/run-steps.sh --all` in a fresh clone of HEAD: 87 steps passed, five not run and named, and `scripts/plan035-prepush.ps1` accepted the five by name;
  - port 22 reached the server without the VPN;
  - a dev container of the owner restarts every minute, so a long browser tour meets `net::ERR_NETWORK_CHANGED`;
  - `kubectl` is absent (`Ac42i` fails locally only), and the job's Chromium step uses `--with-deps` (sudo): use the Chromium in `~/.cache/ms-playwright`.
- **The side PRs** about the E2E Playwright install (#40, #42), if still open: never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI today:**
  - a PR run has 15 checks (52 min in PLAN-52). One acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - the required checks on `main` are `CI`, `CodeQL`, `Gitleaks`, `E2E` and `Acceptance`. The last step of `Acceptance` («Every stage job succeeded») fails when the image build or any stage failed;
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - `acceptance-job.ps1` takes `-Classes` and `-Stage`;
  - a new acceptance class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs. The `prodlike` job runs the stages `journey`, `main`, `isolated` and `s2s42` on one stack;
  - **AC-23, AC-24 and AC-25 are required since PLAN-53;** AC-12 is Blocked and not required (PLAN-59 rewrites it); `AcIam53SalaryChangeTests` is required in `prodLike[]`;
  - **their committed evidence is still the «Fail» of the run before PLAN-53** (D-QA-20: nothing of a local run is committed). A narrowed local run that leaves them out copies that evidence in and its guard names them: include them, or read the run by its class table;
  - **the build and test steps live in `scripts/ci/steps.json`,** which CI and the local runners read; `CiStepsParityTests` keeps `ci.yml` in line with it.
- **From PLAN-53** (`docs/PAYROLL_SALARY_CHANGE.md`, and its report's §16):
  - the policies `Payroll.Process`, `Payroll.Approve`, `Payroll.MasterData` (`hr-admin`, `payroll-processor`), `Payroll.ChangeCancel`, in `HrManagementPolicies`, with their rules in `PayrollPolicyRules`;
  - the pattern of a request command and a decision command where the decider is not the requester; `PayProfileChangeResult` and its failure kinds for the HTTP answer; `IPayrollCalendar` for «today» (Damascus); `HrInvariantDecimalModelBinder` for amounts on forms;
  - **an API's writes validate an antiforgery token:** a `GET …/antiforgery` endpoint (`MessagingRepairAntiforgery.Issue`, as `PayProfileChangesApiController.cs:34-36`), and `AcceptanceHarness.CreateAntiforgeryClientAsync` for tests;
  - `PayrollWriteActionsTests` fixes each Payroll write's policy and who reaches it, role by role. `WriteActionsHaveExplicitPolicyTests` accepts no row anywhere; `CeilingList.Owners` = `IAM-F`, `IAM-H`;
  - Payroll's org scope is wired (`PayrollOrgScopeKeys`, the filter);
  - **payroll runs compute in acceptance** (`AcceptancePayrollRegimePatch`): PLAN-54's step 0.7 is done.
- **From PLAN-52, PLAN-67 and PLAN-40:**
  - every controller declares an org-scope stance or keeps a row, and `OrgScopeCoverageTests` refuses new rows;
  - `InterfaceTextGuardTests` scans every view (Arabic labels, `HrEnumDisplay`, `_UserDisplayName` for people, `HrDate`/`HrDateTime`, `_HrEmptyState`, `data-hr-confirm`, no inline handler or `style`, JSON only inside `_TechnicalJson`), and its lists take no row. The prod-like and E2E stacks enforce the CSP;
  - the outbox rules R1–R4 (`OutboxPublishOrderingArchitectureTests`).
- **The code today** (read on `53690e0e`; find code by its content if a line moved):
  - **The framework's screens** (`PayrollConfigurationsController.cs`, class `[Authorize(Policy = ModulePayroll)]` at `:14-15`, and an `IAM-H` org-scope row):
    - no `Create` and no import;
    - `Edit` (`:59-117`, `Payroll.Process`) binds the entity and copies an allowlist (`ApplyFields`, `:71-92`) that touches neither the brackets nor the effective date. An immutable version is cloned into a new draft (`:94-104`);
    - `Approve` (`:120-141`), `LegalAttest` (`:143-173`), `Publish` (`:175-210`) and `Activate` (`:212-232`) all take `Payroll.Approve`. `Activate` on an unpublished version publishes it. A refusal redirects with `TempData["Error"]`.
  - **Versioning** (`PayrollConfigurationVersioning.cs`):
    - `ResolvePublishedForPeriod` (`:9-25`) takes `Published` versions only, active first, then the latest effective date, then the highest version, **whatever their code**;
    - `CloneAsDraft` (`:27-86`) copies the brackets only, and the clone inherits `EffectiveFrom` (`:47`);
    - `TryPublish` (`:100-138`) accepts a draft (`:106`) and fills `ApprovedBy` with the publisher (`:113`). It supersedes the code's published versions and ends them the day before the date it publishes from — which, for a clone, is its source's date: a retroactive cut (`:120-128`);
    - `PayrollRepository.GetActiveConfigurationAsync` (`:52-56`) takes the first active published row of any code. End of service and the inbound consumers use it.
  - **Runs** pin a version: `PayrollRunJobHandlers.cs:64-69` writes `PayrollConfigurationId` and `ConfigContentHash`, and `ResolveConfigAsync` (`:161-186`) checks the stored fingerprint or refuses «لا توجد نسخة إعدادات معتمدة/منشورة للفترة».
  - **The fingerprint** (`PayrollConfigurationHashService.cs:32-55`) covers the code, the regime, the numeric fields and the brackets only.
  - **The general tables a run reads:**
    - statutory `DeductionTypes` (`PayrollRunParallelCalculator.cs:87`; `CorrectionRunHandlers.cs:66`), applied to everyone on gross pay by the engine's `StatutoryDeductionRule` (`PayrollRules.cs:126-151`; also `PayrollCalculation.cs:40, 105`);
    - the general holidays, `PayrollConfigurationId == null` (`PayrollRunParallelCalculator.cs:82-85`; `CorrectionRunHandlers.cs:61-65`);
    - the active priority rules when a funding decision names no rule set (`ExecutePartialDisbursementCommand.cs:80-86`);
    - the insurance shares of the export are fixed at 7% and 14% (`PayrollExternalExportService.cs:19-20, 94`);
    - a version without brackets falls back to `PayrollEngine.DefaultBrackets` (`PayrollEngine.cs:12, 41, 93`; `PayrollRules.cs:265-266`).
  - **The entities:** `PayrollConfiguration` (no field records who drafted it; `BaseEntity` has no `CreatedBy`), `TaxBracket`, `OfficialHoliday` (`PayrollConfigurationId?`), `DisbursementPriorityRule` (`RuleSetId`, `Category`, `Priority`, `MinimumAmount`, `LegalSource`, `IsActive`, and no version), `LegalDecisionRecord`, `LegalAttestation` (`DecisionIdsJson`, `ContentHash`, `Status`), `DeductionType` (`IsStatutory`, `Percentage`, `FixedAmount`). The manual inputs are `AuditableEntity` (`CreatedBy`, `UpdatedBy`, which nothing fills today).
  - **The seeds** (Demo and Acceptance only, `Payroll.Web/Program.cs:147-158`):
    - `SeedPayrollDataAsync` (`:193`) adds the decision register (`LegalDecisionRegisterSeed`, every row «ApprovedInterim») and three published versions from 2000-01-01: `LAW_50_2004` active, `LAW_53_2021` and `CIVIL_SERVICE_2026` inactive (`PayrollRegimeSeed.cs`, brackets `:12-19`, fields `:75-126`), with the four default holidays tied to each;
    - `DemoPayrollSeeder` adds the deduction types (`:55-67`): four statutory — `DED-TAX` «ضريبة الدخل» 5%, `DED-SOC` 7%, `DED-HEALTH` 2%, `DED-PENS` 3% — and six others; Regular runs, Posted, with no pinned version (`:343-375`);
    - `AcceptancePayrollSeeder` adds each agency's `LAW_50_2004` (`:82`) and three priority rules (`:303-345`).
  - **Governance:**
    - `PayrollLegalGovernance` (`CreateAttestation` takes the regime's default decision set when given none; `EnsureAttestationForPublish` checks the attestation's fingerprint) does not check that the decisions exist;
    - `ILegalControlCatalogProvider.IsExceptionActive(id)` (`LegalControlCatalogProvider.cs:49-55`) checks the status, not the date. **The services' start-up gate uses it** (`LegalControlGuard.cs:81, 97, 104`);
    - `IPayrollLegalControlGuard` has `ValidatePayrollRelease` and `IsCapabilityAllowed` only;
    - **all six exceptions of `docs/str-legal/control-catalog.json` expire on 2027-01-31,** `EX-STR2-TAX-INTERIM` and `EX-STR2-LAW50-UNCERTIFIED` among them (P-10).
  - **Manual inputs** (`PayrollExtendedControllers.cs`): `AllowanceRecords.Create` (`:55-76`), `DeductionRecords.Create` (`:119-160`) and `PayrollAdjustments.Create` (`:206-247`) take `Payroll.Process`, bind the whole entity (their `IAM-F` entity-binding rows) and save it in force at once.
    - The system writes many more: the inbound consumers (`InboundEventHandlers.cs`, a deduction or an adjustment at `:82` to `:707`) and the seeders.
    - The loader reads them with no approval filter (`PayrollPartitionInputLoader.cs:121-164`, allowances at `:218-245`).
  - **Operations:** `scripts/deploy-organization.ps1` lost `HR_LEGAL_REGIME` in PLAN-36 already (`:10-12`); it does not say how to bring in a framework. `docs/PILOT_DEPLOYMENT.md` exists. `scripts/hcm025-repair-stuck-approvals.ps1` is the pattern of an operator script that calls the API as a user (credentials from parameters or the environment, never stored or printed).
  - **The integration host** registers the fake service-regime client (`PayrollIntegrationTestSetup.cs`, QA-034), so plan 0.6 is a check, not work.
  - **The personas:** 67 (`DATA-AUTH` in `scripts/acceptance-verify.ps1:556`); agency A has `payroll-processor`, `payroll-approver`, `a-payroll-poster` and `a-payroll-reverser`. On the demo: `e2e-payroll-processor`, `e2e-payroll-approver`, and `hradmin`.
  - **A JSON Schema validator is not in the repository,** and the link is too slow to bring packages in.
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43). Lift `FORCE` inside the migration, as `WF044` does — or let a column default fill the existing rows (P-12).
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role.
  - **A new PostgreSQL test uses the connection strings of `PostgreSqlFixture`,** whose idle connections close after seconds. Make sure CI's Testcontainers filter picks up your new classes.
  - **The prod-like stack runs `ASPNETCORE_ENVIRONMENT=Acceptance`,** so it does not load `appsettings.Production.json`. A value in a Production file must be mirrored in `docker-compose.acceptance-prodlike.yml`.
  - **Personas and users:**
    - a persona change needs `scripts/acceptance-reset.ps1` before the first start (the personas' hash is in the seed pack's fingerprint);
    - **never write `personas.json` while an image is building:** PLAN-53's image then carried another fingerprint, and one image was built again;
    - a new persona also changes `DATA-AUTH` in `scripts/acceptance-verify.ps1`;
    - a user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh`.
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - **Formatting:** before every commit, run `dotnet format whitespace HRManagement.slnx --verify-no-changes --no-restore --include <the files you changed>`. CI runs the full check.
  - **Acceptance tests:**
    - a test posts the URL and the token the page renders (PLAN-44);
    - an API write needs the antiforgery token (`AcceptanceHarness.CreateAntiforgeryClientAsync`; PLAN-47, PLAN-53).
  - **CodeQL runs in CI only:** a new API POST or PUT without `[ValidateAntiForgeryToken]` fails `cs/web/missing-token-validation`. Never answer it with a baseline entry or an ignore attribute.
  - **The machine** (P-2):
    - **the disk:** PLAN-53 filled it to 0 MB once, building the migrator image with 8.4 GB free;
    - after a full disk, stale MSBuild and Razor servers gave parse errors on unchanged files until `dotnet build-server shutdown`;
    - never delete build outputs while a step builds with them;
    - helper Dockerfiles inside the tree tripped the base-image-pin check: keep helpers outside the tree;
    - never change `~/.ssh/config` in the middle of a run (PLAN-53 switched on connection reuse and disturbed a server check);
    - after the host sleeps, the Docker VM's clock may lag, and time-based tests fail until it catches up (PLAN-50).
  - **The session may stand still** (PLAN-51: 7 h 44 min after the pre-push check, until «Try again»). Keep the progress file's next step exact, and go from the pre-push check straight to the push and the PR, in the same turn.
  - **The server:**
    - **its disk:** check the free space before the build (P-17);
    - run the server build detached (PLAN-44, PLAN-53), and the server suites with nothing heavy beside them (PLAN-45);
    - if the server build fails because the provider's resolvers answer SERVFAIL, wait and retry; never change the server's resolver configuration (PLAN-50);
    - the server suites write into the committed `acceptance-evidence/` and `reports/`: restore those files, or move them under `artifacts/plan54/vps/` (PLAN-48);
    - a server check cleans up what it leaves open, and decodes a page before it compares Arabic text (PLAN-49);
    - a user without HR.Core signs in through another page; a refusal of the gateway's own pages is a 302; the gateway audits only what it forwards (PLAN-51);
    - two PLAN-53 failures came from the owner's machine, not the server: `AC_POL_47_vps_the_migration…` reads a «before» file of the formatted Windows machine, and the `AC_UX_67` tour meets the restarting dev container. Record them; do not chase them.
  - **Before the push, and before any fix pushed to the release branch:**
    - run the unit suite (its tests scan every source file) and the format check: PLAN-67 lost a CI run to a fix pushed without them;
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
   - `docs/acceptance-plans/PLAN-54-pay-legal-framework-reference-data.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
   - `docs/PAY_001_GAP_STUDY.md`: G-PAY-01, G-PAY-09, G-PAY-10, G-PAY-12, and the PAY-A row.
2. **These reports:**
   - `reports/plan53-final-report.md` (§3, §11, §14, §16) and its merge note; `docs/PAYROLL_SALARY_CHANGE.md`;
   - `reports/plan53-progress.md`, for the machine: its P0, P5, P6 and P9 notes;
   - `reports/plan52-final-report.md` (the org scope) and `reports/plan67-final-report.md` (the interface guard).
3. **These documents:**
   - `docs/PAYROLL_CONFIG_VERSIONING.md`, `docs/PAYROLL_RULES.md`, `docs/PILOT_DEPLOYMENT.md`;
   - `docs/str-legal/control-catalog.json` (its exceptions), `docs/STR_002_CONFLICT_EXCEPTION_REGISTER.md`, `docs/STR_002_ADOPTION_MINUTES.md`, `docs/syr/legal-source-binding.md`;
   - the rows D-PAY-1, D-PAY-6, D-PAY-12 and D-PAY-13 of `docs/OWNER_DECISIONS_REGISTER.md`; `docs/SOD_MATRIX.md`.
4. The code named in «The code today».
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **No other plan session is running,** and you are in the main folder (see «How to run»), on `main` or on `docs/plan-53-merge-note`.
2. **`git fetch origin`**, and `origin/main` holds PLAN-53: the merge of `release/plan-53` is in `git log origin/main --merges --oneline -5`.
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

- **D-PAY-1, the legal values:** the pack `law50-interim` reproduces today's seed values exactly. Every value is `legalStatus=Interim` and cites an active exception: `EX-STR2-TAX-INTERIM` for tax, `EX-STR2-LAW50-UNCERTIFIED` for the rest. No calculation result changes. Both exceptions end on 2027-01-31, and publishing and calculating with the interim framework stop after that, on purpose.
- **D-PAY-6, manual inputs:** an allowance, a deduction or an adjustment entered by hand needs a second person (`Payroll:ManualInputs:RequireApproval=true`), and does not enter a calculation before that.
- **D-PAY-12, who attests:** a holder of `Payroll.Approve` who is neither the version's approver nor its editor. The setting `Payroll:Legal:AttestorPolicy` (default `Payroll.Approve`) can later name a legal role's policy without a code change. The plan adds the persona `a-payroll-approver-2`.
- **D-PAY-13, retroactive publishing:** a version whose effective date falls in a period with a posted run of the same regime is not published. Retroactive effect is wave 2 (PAY-H).

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-54-pay-legal-framework-reference-data` from `origin/main` (plan 0.1), in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-53's merge note from `docs/plan-53-merge-note`. If that branch is gone, rebuild the record from GitHub (the PR, its runs, the merge commit) as PLAN-53 rebuilt PLAN-67's, and say so;<br>&nbsp;&nbsp;– commit this prompt file;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **The machine (Linux): awake, the disk, Docker, the ports, the link.**<br>• **Keep the machine awake for the session, changing no setting:** `systemd-inhibit --what=sleep:idle --who=plan54 --why="PLAN-54" sleep infinity`, detached. Record its PID, and stop it **by that PID** at the end. If the machine restarts, start it again and record it.<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• **Remove only this repository's leftovers:** compose projects `hrm-*`, Testcontainers, this plan's images and this repository's build cache. Never prune, stop or remove another project's images, containers or volumes, or the owner's dev containers.<br>• **The disk** (PLAN-53 filled it once): read `df -h /` and `docker system df` before every image build and every stack start.<br>&nbsp;&nbsp;– Build an image only with at least 12 GB free. Below that, prune this repository's build cache and remove this plan's unused images first;<br>&nbsp;&nbsp;– a watch that stops a build near 2 GB free stops it **by its PID**;<br>&nbsp;&nbsp;– if 12 GB cannot be reached without touching other projects: `BLOCKED:`, one line — the owner frees space.<br>• **The link is slow:** build the images one service at a time from the local base images, as PLAN-53 did (`acceptance-up.ps1 -UseLocalBaseImages`, and its helper outside the tree). Never commit a helper.<br>• **One heavy job at a time:** never an image build beside a host test run.<br>• **After an image build,** check that every service image was rebuilt from this tree. A failed build leaves stale images, and a `-NoBuild` run on them proves nothing.<br>• **Before each stack start,** check the stack's host ports with `ss -ltn` and `docker ps`. A port held by something that is not this repository's: `BLOCKED:` with the port.<br>• **The local CI steps:** `scripts/ci/run-steps.sh --all` natively in a fresh clone of HEAD, as PLAN-53 did, naming each step not run and why. `scripts/ci-local.ps1` only if the machine can hold its image and its builds.<br>• **If this session runs on Windows again,** use PLAN-67's forms of these checks (drive C, `netsh`, `SetThreadExecutionState`, the VPN order), and record it. |
| P-3 | **The baseline (plan 0.2–0.6),** into `reports/plan54-baseline.md`.<br>• **0.2–0.3:** `dotnet build HRManagement.slnx`; the unit tests filtered on `Pay0`, `Payroll` and `Gov002`. Green, or a list.<br>• **0.4, the inventory,** generated by search, never typed, into `artifacts/plan54/inventory.*`, and summarised:<br>&nbsp;&nbsp;– every reader of `DeductionTypes` (statutory or not), `OfficialHolidays`, `DisbursementPriorityRules`, `GetActiveConfigurationAsync`, `GetPublishedConfigurationForPeriodAsync`, `ResolvePublishedForPeriod`, `DefaultBrackets`, and of the export's two rates;<br>&nbsp;&nbsp;– every writer of `PayrollConfiguration`, `TaxBracket`, `OfficialHoliday`, `DisbursementPriorityRule`, `LegalDecisionRecord` and `LegalAttestation`;<br>&nbsp;&nbsp;– every writer and every reader of `AllowanceRecords`, `DeductionRecords` and `PayrollAdjustments`: screens, API, consumers, seeders, the loader, corrections, reports, payslips;<br>&nbsp;&nbsp;– the tests that pin today's results (`Pay002ImmutableConfigTests`, `PayrollEnginePipelineTests`, `Gov002LegalGoldenPackTests` and `testdata/gov002/golden-cases.json`, the determinism tests).<br>• **0.5, PLAN-53's names:** as «Where things stand» lists them; record them, and use them as they are.<br>• **0.6:** the payroll integration tests that use the fake regime client are green.<br>• **Two more, from this prompt:**<br>&nbsp;&nbsp;– the versions on a fresh lab stack, read-only: per agency, the codes, versions, statuses, `IsActive`, effective dates and fingerprints; the statutory deduction types; the holidays; the priority rules (P5 reads them on the stack it starts anyway);<br>&nbsp;&nbsp;– the demo's picture (P-16). |
| P-4 | **Policies, stances and the API's surface.**<br>• **Every new action names its policy:**<br>&nbsp;&nbsp;– create, import, export, the effective date, and `Edit` (screens and API alike) → `Payroll.MasterData`;<br>&nbsp;&nbsp;– approve, publish, activate → `Payroll.Approve`;<br>&nbsp;&nbsp;– attest → a new `Payroll.LegalAttest`, met when the user meets the policy named by `Payroll:Legal:AttestorPolicy` (default `Payroll.Approve`). The service refuses to start on a name that is unknown or names `Payroll.LegalAttest` itself;<br>&nbsp;&nbsp;– a manual input: create → `Payroll.Process` (as today), approve or reject → `Payroll.Approve`.<br>&nbsp;&nbsp;`PayrollWriteActionsTests` gains each one, with who reaches it role by role.<br>• **The separation of duties** lives in the handlers and in `PayrollFrameworkGovernance` (P-8), not in the policies.<br>• **The stances:** `PayrollFrameworksApiController` and `PayrollConfigurationsController` declare `[OrgScopeNotApplicable("…")]` — a framework is the agency's, not a branch's; the latter's `IAM-H` row leaves. The manual input controllers keep their `IAM-H` rows (their branch scope is `IAM-H`'s).<br>• **The API** (`api/payroll/frameworks`): `POST` (create), `POST import`, `GET {id}/export`, `PUT {id}/effective-from`, and a `GET antiforgery` endpoint. **Every `POST` and `PUT` carries `[ValidateAntiForgeryToken]`** from its first commit, as `PayProfileChangesApiController` does.<br>• **An uploaded pack:** a size limit, JSON only, a bounded depth; an error names the field and the row, in Arabic, and never echoes the file; no log line writes the file or a value from it.<br>• **The entity bindings:** the three manual input `Create` actions move to view models, and their `IAM-F` rows leave. `Edit` of a framework keeps its allowlist copy and gains the effective date; a view model for it is `IAM-F`'s, optional here. |
| P-5 | **The pack and the importer (plan §1).**<br>• **`docs/syr/payroll-framework-packs/schema.v1.json`** is the documented contract. There is no validator package (and none is added): **the importer validates in code,** and a unit test keeps the schema's required fields and allowed values in line with the importer's.<br>• **`law50-interim.json` reproduces exactly what a run computes today** (D-PAY-1):<br>&nbsp;&nbsp;– the brackets of `PayrollRegimeSeed.Law50TaxBrackets` and the numeric fields of `CreateConfiguration`;<br>&nbsp;&nbsp;– the four statutory deductions `DemoPayrollSeeder` seeds (`DED-TAX` 5, `DED-SOC` 7, `DED-HEALTH` 2, `DED-PENS` 3, all on gross pay, no ceiling);<br>&nbsp;&nbsp;– the four default holidays, the three priority rules of the acceptance seed, and the register's decisions verbatim;<br>&nbsp;&nbsp;– every value `Interim`, citing `EX-STR2-TAX-INTERIM` (tax: the brackets, the exemption, the taxable base, `DED-TAX`) or `EX-STR2-LAW50-UNCERTIFIED` (the rest).<br>&nbsp;&nbsp;**Prove it:** the same employee and month, calculated with the seeded version and with the imported one, give identical lines, unit for unit.<br>&nbsp;&nbsp;**Record, do not change:** an income tax of 5% as a statutory deduction on top of the brackets taxes twice. That is a finding for D-PAY-1 and PAY-E; the numbers do not change in wave 1.<br>• **The importer:**<br>&nbsp;&nbsp;– validates the structure and the rules (brackets ascending with no gap; percentages 0–100; the effective date mandatory; every cited decision present after the import);<br>&nbsp;&nbsp;– creates a `Draft` (`VersionNumber` = the code's highest + 1), or replaces the content of a draft named by `targetDraftId` (a draft only);<br>&nbsp;&nbsp;– inserts the missing `LegalDecisionRecord` rows and updates the others by `DecisionId`;<br>&nbsp;&nbsp;– **one transaction:** a refused pack leaves no row;<br>&nbsp;&nbsp;– records the importer as an editor of the version (P-8).<br>• **`CreatePayrollFrameworkCommand`:** a draft from a regime's template (`regimeCode`) or a clone (`cloneFromId`), with the effective date the user gives.<br>• **The screens:** `Create.cshtml`, `Import.cshtml` (a file upload), and `Details.cshtml` showing the four tables, the numeric fields' references, and each value's reference and status (`HrEnumDisplay`); the export is a download, never rendered. **The import is the way to edit the tables;** `Edit` keeps the numeric fields of a draft, plus its effective date.<br>• **First run:** the payroll dashboard shows «لا يوجد إطار منشور» with the import link, and the calculation's refusal names the link.<br>• **`scripts/pay054-import-legal-pack.ps1`:** authenticates as `scripts/hcm025-repair-stuck-approvals.ps1` does, fetches the antiforgery token, posts the pack, and prints the approval, attestation and publication steps. It never prints a password or a token. **`scripts/deploy-organization.ps1`** prints that command after a deployment (its first half, `HR_LEGAL_REGIME`, went in PLAN-36). |
| P-6 | **Versions, dates, and which version governs a period.**<br>• **The effective date belongs to the version:** a clone no longer inherits it (`:47`); create, clone and import take the date the user gives.<br>• **`CloneAsDraft` carries every child table and every reference.** A reflection test, in the manner of PLAN-53's copy-forward test, fails on a property or a child the clone does not carry.<br>• **Cloning a version of fingerprint v1 into a v2 draft** copies, as the draft's starting rows, what a v1 run reads today: the statutory deduction types, the general holidays and the active priority rules, each `Interim` with no exception. So the numbers do not move, and the draft cannot be approved before someone classifies them (export, then import).<br>• **`TryPublish`:**<br>&nbsp;&nbsp;– from `Approved` only; the `ApprovedBy` fill goes (`:113`);<br>&nbsp;&nbsp;– it uses the version's own effective date;<br>&nbsp;&nbsp;– refused when that date is on or before the effective date of the code's published version;<br>&nbsp;&nbsp;– **D-PAY-13:** refused when a posted run of the same regime has a period ending on or after that date. The run's regime is its pinned version's; a posted run with no pinned version counts for every regime (the seeded ones have none);<br>&nbsp;&nbsp;– the predecessor ends the day before, and only that.<br>• **A superseded version still governs its own dates.** When version 2 is published from 1 January, version 1 still governs December. Today's resolver takes `Published` versions only (`:9-25`), so after this change December would find no version of its code and fall to another regime's version, or to none. So:<br>&nbsp;&nbsp;– the resolver for a period takes `Published` and `Superseded` versions whose dates cover it, preferring a code that has an active version, then the latest effective date, then the highest version number;<br>&nbsp;&nbsp;– `GetActiveConfigurationAsync` becomes that resolver for today (`IPayrollCalendar`). Today it takes any active row, and two active codes make it a coin toss.<br>&nbsp;&nbsp;**Prove:** a future version of the same code leaves the current period on its predecessor; AC-PAY-54a's new code from 2031 changes nothing before 2031 for any other class on the same stack; the active configuration is the same on every call with two active codes.<br>• **`Activate`** works on `Published` only (plan rule 3); otherwise as today. |
| P-7 | **The tables inside the version, the fingerprint v2, and the engine (plan §2).**<br>• **The entities:** `StatutoryDeductionRule` (the plan's fields; `Ceiling` caps the base before the percentage), `PayrollConfigurationFieldSource`, and the reference fields (`LegalReference`, `LegalStatus`, `InterimExceptionId`) on `TaxBracket`, `OfficialHoliday` and `DisbursementPriorityRule`, which gains `PayrollConfigurationId`. Reuse `LegalSource` as the reference if it means the same, and record it.<br>• **A name clash:** the engine already has a rule class `StatutoryDeductionRule` (`PayrollRules.cs:126`). The entity keeps the plan's name; if a file needs a qualified name to tell them apart, rename the engine's rule, and record it.<br>• **`HashSchemaVersion`:** 1 for every existing version (its fingerprint untouched), 2 for every new one. Fingerprint v2 covers the four tables, the field sources, every reference and status, in a canonical order. v1 versions are never hashed again.<br>• **The engine on a v2 version** reads only the version: its statutory deductions (base kind, ceiling, fixed amount), its holidays, its priority rules. It never falls back to `DefaultBrackets`: a version without brackets is not published.<br>• **On a v1 version** everything computes as today (the general tables, the fallback, the export's two rates), through a path named as legacy, with a line in the job's log. First prove whether any v1 version anywhere (seed, test, demo) has no brackets.<br>• **The export** takes the employee's and the employer's shares from the pinned v2 version's insurance rule; the two fixed rates live on in the v1 path only.<br>• **`DeductionTypes`** stays the catalogue of non-statutory deductions. `IsStatutory` becomes read-only on its screens, with «انتقل إلى الإطار القانوني».<br>• **Tests:** a v1 run and its export unchanged; a v2 run reads its own rows; editing `DeductionTypes` after pinning changes neither a v2 result nor its fingerprint; the golden pack and the pipeline tests unchanged. |
| P-8 | **The framework's cycle: three people and the interim gate (plan §3).**<br>• **`PayrollFrameworkGovernance`** replaces the rules of `PayrollLegalGovernance` (the attestation record and the change notice stay).<br>• **The editors:** a version records every person who created it, imported into it or edited it. The fingerprint ignores this record.<br>• **The rules:**<br>&nbsp;&nbsp;– the approver is not an editor;<br>&nbsp;&nbsp;– the attestor is neither an editor nor the approver, and attests an `Approved` version (rule 1's order; approval fixes the content);<br>&nbsp;&nbsp;– the publisher is the approver or the attestor (rule 1), so never an editor;<br>&nbsp;&nbsp;– publish from `Approved` only, with an active attestation that matches the fingerprint.<br>• **A refusal answers 400 with an Arabic message and changes nothing** (rule 1) — on the form posts too: the action renders the details page with the message and status 400, and only a success redirects.<br>• **The interim gate:**<br>&nbsp;&nbsp;– `IPayrollLegalControlGuard.IsExceptionActive(id, asOf)`: `Status=Active` and `expiresOn` after `asOf`, the Damascus date from `IPayrollCalendar`;<br>&nbsp;&nbsp;– at approval, attestation and publication, every `Interim` value cites an exception active that day; a refusal names the value and the exception;<br>&nbsp;&nbsp;– at the start of a calculation, every exception the pinned version cites is active that day. Otherwise the run is refused, and the job's log says «انتهى الاستثناء القانوني X»;<br>&nbsp;&nbsp;– **the shared `IsExceptionActive(id)` keeps its meaning:** the services' start-up gate uses it. Record that this gate does not read the date.<br>• **The attestation** checks that every decision the version cites exists, and stores the exceptions it relies on (`LegalAttestation.InterimExceptionIdsJson`).<br>• **The indicators:**<br>&nbsp;&nbsp;– a run's details and its provenance say «إطار مؤقت — استثناء EX-…» when its version holds interim values, and that a v1 version predates PLAN-54;<br>&nbsp;&nbsp;– the dashboard warns, from 60 days before, of an exception cited by a version in force that is about to expire, naming it and its date. |
| P-9 | **Manual inputs with a second person (plan §4).**<br>• **The fields** `ApprovalStatus` (`Approved`, `PendingApproval`, `Rejected`), `Origin` (`System`, `Manual`), `ApprovedBy`, `ApprovedAt` and `DecisionNote` on the three entities. Every existing row, and every row a consumer or a seeder writes, is `Approved` and `System`.<br>• **The view models** (`ManualInputViewModels.cs`) are allowlists: the employee, the type, the amount, the date, the reason. Never `AppliedInRunId` or `PayrollRunId` from the client.<br>• **The commands** (`Features/ManualInputs/`): create → `PendingApproval`, recording its creator; decide → the decider is not the creator, and a rejection needs a reason. PLAN-53's pattern.<br>• **Every reader that feeds pay** — the loader (`PayrollPartitionInputLoader.cs:121-245`), corrections, and any other the inventory finds — reads `Approved` rows only. A payslip or report that lists inputs marks a pending one as pending.<br>• **The screens:** a state column; approve and reject for those whose policy allows it, with `data-hr-confirm` on reject.<br>• **`Payroll:ManualInputs:RequireApproval`** is true; false is honoured in Development only, ignored elsewhere with a warning at start.<br>• **Rule 1:** an input approved after its period's run froze enters the next run, as the loader does (its behaviour is PLAN-56's). |
| P-10 | **The interim exceptions expire on 2027-01-31.**<br>• All six exceptions of the catalog end that day. From 2027-02-01, by design (D-PAY-1), the product refuses to approve, attest, publish or calculate a v2 version with interim values. v1 versions cite no exception, so the demo and the seeded runs go on.<br>• **AC-PAY-54a needs the two cited exceptions active.** Before its positive path, the class reads them from the catalog the service loads. If one is no longer active, the class fails with a message that names the exceptions and the date, and says that renewing them is the owner's STR-002 decision. It never switches to another path, and never edits the catalog.<br>• **Say it plainly** in the final report, in `docs/CI_AND_TESTING.md`, in `docs/PILOT_DEPLOYMENT.md` and in the PR: from 2027-02-01 this required class fails until the owner renews or replaces those exceptions, and the payroll dashboard warns from 2026-12-02. |
| P-11 | **The persona `a-payroll-approver-2`** (agency A; `payroll` and `payroll-approver`; scenario AC-PAY-54), in `testdata/acceptance/personas.json` and wherever the acceptance realm is generated from it. `DATA-AUTH` goes from 67 to 68, with any other count your search finds. Reset the local stacks before their first start (Lessons). It is not added to the demo (P-16). |
| P-12 | **The migration `PAY054_LegalFrameworkPacks`.**<br>• **One migration** in `Payroll.Infrastructure`: the new tables with `TenantId`, the new fields, row security on the new tables through `TenantRlsMigrationHelper`, and the grants, as Payroll's latest migrations (`PAY053`) give them.<br>• **The existing rows** get `HashSchemaVersion=1`, `ApprovalStatus=Approved` and `Origin=System` **by column defaults,** with no `UPDATE`: under forced row security an `UPDATE` by the migrator touches nothing. If an `UPDATE` cannot be avoided, lift `FORCE` as `WF044` does. No `ContentHash` changes.<br>• No byte order mark; `dotnet ef migrations has-pending-model-changes -p src/Services/Payroll/Payroll.Infrastructure -s src/Services/Payroll/Payroll.Web` reports none.<br>• **Prove it as a non-superuser** (the leastpriv stack): every existing version and input got its value; agency B sees no row of agency A in the new tables.<br>• **`Pay054EmptyDatabaseFirstRunPostgreSqlTests`:** an empty database with `Seed:Profile=None` reaches its first calculation through import, approval, attestation and publication only. The employees and their pay profiles come through the product's own paths (the consumers), never SQL. And a refused pack leaves no row. Make sure CI's Testcontainers filter picks it up. |
| P-13 | **Acceptance placement (PLAN-41's rules).**<br>• **`AcPay54LegalFrameworkTests`** (AC-PAY-54a..f): `prodLike[]`, `required: true`, group `main`; `[RequiredAcceptance]` and `EvidenceRecorder("AC-PAY-54")`. It does not stop or recreate containers, and uses the stack's defaults (the plan's `--force-recreate payroll-web` line is a local convenience).<br>• **Its steps run in order,** as `AcIam53`'s do. The code is `LAW_50_2004_AC54` (regime `LAW_50_2004`).<br>• **Its months:** 2031-01 for 54a's run; version 2 of 54c from 2031-02-01; a month of its own for 54e. Never the current month, which AC-IAM-53a takes. A rerun on the same stack finds the code already there: its precondition fails with a clear message, and a rerun needs a fresh stack.<br>• **54b:** the approver attesting, the editor publishing, and a publication from `Draft` each answer 400 and change nothing.<br>• **54d:** a pack whose interim value cites an exception that does not exist is refused at approval, with a message naming the value and the exception.<br>• **54e:** the creator approving his own input answers 403 (no `Payroll.Approve`); a posted `appliedInRunId` is ignored.<br>• **54f:** the seed's posted Regular runs (2025-08 to 2026-07, pinned to no version, so they count for every regime: P-6) make a version from 2026-03-01 refuse to publish.<br>• **P-10's check** comes first in 54a.<br>• **`ac-ownership.json`:** `AC-PAY-54` (owner PLAN-54, `provesClaim: true`, and its reason).<br>• **The evidence of record is the PR run's artifact,** not `acceptance-evidence/AC-PAY-54/`. Copy what the report needs into `artifacts/plan54/`, then restore the committed evidence. |
| P-14 | **Local testing (owner's rule of 2026-11-06).** Run only the classes this plan touches, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`, and the overlay groups with `-Stage prodLike:<group>`. **The full suite runs once, in CI.**<br>• **prod-like:** `AcPay54LegalFrameworkTests`, `AcIam53SalaryChangeTests`, `AcIam51ExplicitPolicyTests`, `AcIam52BranchScopeTests`, `AcUx67ReadableInterfaceTests` (with the framework's details and import pages and a manual input list added to its key pages), `AcHcm25ProductionApprovalTests`, the prod-like journey, and the `isolated` group;<br>• **lab:** `Ac23PayrollFreezeTests`, `Ac24PartialDisbursementTests`, `Ac25FraudHoldTests` (v1 runs: their numbers must not move), `Ac21PayrollDeterminismTests` (PowerShell 7 is here now), `AcHcm29HireContractTests`, the lab journey;<br>• **leastpriv:** `AcOps38FirstBootTests` and `AcTen48LeastPrivilegeTests` (the migration);<br>• **E2E:** in CI only.<br>Then compare the seeded counts of `scripts/acceptance-verify.ps1` by psql. Known local-only failures (`Ac42i` without `kubectl`) are recorded, not chased. |
| P-15 | **Scripts.**<br>• Every new or changed `.ps1` is ASCII-only, 5.1-compatible in syntax, and runs here with `pwsh -NoProfile -File …`. The plan's `pwsh scripts/pay054-validate.ps1` stays as written.<br>• **`scripts/pay054-validate.ps1`** has a `-SelfTest` with planted defects, as `iam053-validate.ps1` does. It checks, at least:<br>&nbsp;&nbsp;– the schema and the pack: the pack's values equal the seed's (brackets, fields, the four statutory deductions, holidays, priority rules), every value has a reference and a status, every interim value cites a catalog exception;<br>&nbsp;&nbsp;– the migration has no byte order mark and sets the existing rows by defaults;<br>&nbsp;&nbsp;– the settings `Payroll:ManualInputs:RequireApproval` and `Payroll:Legal:AttestorPolicy` in Payroll's settings, mirrored in the prod-like compose file where Production sets them;<br>&nbsp;&nbsp;– no `[HttpPost]` or `[HttpPut]` of `PayrollFrameworksApiController` without `[ValidateAntiForgeryToken]`;<br>&nbsp;&nbsp;– `AcPay54LegalFrameworkTests` required in `prodLike[]`, and the persona present.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile <ProdLike or Lab> -UseLocalBaseImages`. |
| P-16 | **The demo server facts this plan must respect.**<br>• **It runs Production in demo mode** (`Deployment__Mode=demo`), so `hradmin` keeps the payroll policies (AC-IAM-51's d-iam-4).<br>• **The three people of a server check:** `e2e-payroll-processor` edits, `e2e-payroll-approver` approves, and `hradmin` attests — if P0 proves that `hradmin` meets the attest policy in demo mode. Otherwise add `e2e-payroll-approver-2` to the realm manifest and to the live realm through `scripts/vps/add-demo-user.sh`, its password never printed.<br>• **P0's read-only picture:**<br>&nbsp;&nbsp;– the running tag and the free disk;<br>&nbsp;&nbsp;– the versions (codes, numbers, statuses, `IsActive`, dates, fingerprints) and a checksum of them;<br>&nbsp;&nbsp;– the decision register's checksum;<br>&nbsp;&nbsp;– the runs per status and month, and which are pinned;<br>&nbsp;&nbsp;– the manual inputs' counts per kind.<br>&nbsp;&nbsp;A new Regular run is refused on the demo by the service-regime gate (PLAN-53 §12: 500 unclassified records). Not this plan's; record that it still holds.<br>• **What the owner will see change:**<br>&nbsp;&nbsp;– the legal frameworks screen gains create and import, and shows the tables with their references and statuses;<br>&nbsp;&nbsp;– approving, attesting and publishing take three people;<br>&nbsp;&nbsp;– a manual allowance, deduction or adjustment waits for a second person;<br>&nbsp;&nbsp;– nothing else changes: the demo's versions are v1 and compute as before. |
| P-17 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan54-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Before the deploy, read-only:** P-16's picture.<br>• **The disk:** at least 30 GB free before the build. Otherwise remove the server's build cache and the local images of older releases. They stay in the loopback registry `127.0.0.1:5050` (undo: `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>`). Record each removal.<br>• **Build once,** on the final code, detached; then `scripts/vps/deploy.ps1 -Tag <sha12>`. The migration runs at start-up.<br>• **No version the checks make can ever govern a run:** the check's version takes an effective date inside a posted month of the demo, so D-PAY-13 refuses to publish it, and it stays `Approved` and attested. No published version, decision row or pay datum of the demo changes.<br>• **Rollback safety:** find out what PLAN-53's tag does with this plan's data, and record it in `OPS_VPS_SINGLE_HOST.md`:<br>&nbsp;&nbsp;– it never reads the new tables and fields;<br>&nbsp;&nbsp;– it counts pending manual inputs, since it knows no approval;<br>&nbsp;&nbsp;– it computes a v2 version with the general tables.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn:<br>&nbsp;&nbsp;– roll back to PLAN-53's tag, or fix forward;<br>&nbsp;&nbsp;– restore the backup only if data was damaged;<br>&nbsp;&nbsp;– prove the demo works, and record the downtime. |
| P-18 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P9, after the local CI steps and the pre-push check are green, in the same turn as the pre-push check (Lessons).<br>2. `gh pr create`, then wait («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, run the unit suite and the format check, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>`, and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-19 | **No regression of PLAN-25, PLAN-36..53, PLAN-66 and PLAN-67.** These stay green:<br>• **every existing payroll result, unit for unit:** `Pay002ImmutableConfigTests`, `PayrollEnginePipelineTests`, `Gov002LegalGoldenPackTests`, the determinism tests, AC-23, AC-24 and AC-25 (required), and AC-21;<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests`;<br>• `AcIam50`..`AcIam53`, `AcUx67`, `AcWf43`..`AcWf46` with the orphan class, `AcPol47`, both `AcTen48` classes, both `AcPol49` classes, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, `AcHcm29`, and both recruitment journeys;<br>• the architecture tests of PLAN-51..53 and PLAN-67 (`PayrollWriteActionsTests`, `PayProfileWritersTests`, `PayrollReferenceWritersTests`, `OrgScopeCoverageTests`, `InterfaceTextGuardTests`, `GuardSelfTests`), `OutboxPublishOrderingArchitectureTests`, `Rel004CompatibilityTests`, `Str002ProductionBindingTests`, `ProductionWiringMatrixTests`, `CiStepsParityTests`;<br>• `scripts/iam053-validate.ps1`, `iam052`, `iam051`, `iam050`, `ux067`, `pol047`, `pol049` validate scripts, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-53, done:** the sixteen policies, Payroll's ceiling, the salary change. This plan uses them.
- **Wave 2, PAY-E:** the final legal values; a framework per employee by regime; the deduction ceiling; the day divisor (G-PAY-11, the values of G-PAY-12).
- **PLAN-58:** the priority rules in the unified disbursement path.
- **Wave 2 (HCM-002 / PAY-H):** Hijri holidays and a central official calendar; retroactive effect.
- **A later decision (D-PAY-12):** attestation by a separate legal role in Keycloak. The setting is ready.
- **PLAN-56:** the run's state machine, and the loader's behaviour for an input approved after a freeze.
- **`IAM-F` and `IAM-H`:** a view model for `PayrollConfigurations.Edit`; the branch scope of the manual input screens.
- **The owner's governance:** renewing or replacing the STR-002 exceptions (P-10); the services' start-up gate reading dates.

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
- change a v1 version's content or fingerprint, or any existing payroll result;
- edit the exceptions of `docs/str-legal/control-catalog.json`, or change what the shared `IsExceptionActive(id)` means;
- publish a framework version on the demo that can govern a run, or change its decision register or its pay data;
- write a pack, a salary or a reason into a log;
- touch another project's Docker images, containers or volumes, or the owner's dev containers;
- print a password, or rotate a secret that exists on the server;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** (port 22 at P0 excepted: Preconditions 4).
- **The interim pack cannot reproduce today's results exactly** for a reason that needs a value or a rule changed (P-5).
- **An existing result would change** (a v1 run, a golden case, AC-23..25), and the cause cannot be removed without changing the plan's design.
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning.
- **12 GB free for an image build cannot be reached** without touching other projects, **or a port the tests read is held** by something else (P-2).
- **Port 22 is still blocked when P8 starts:** one line asking the owner to restore it (the VPN, on his network).
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, keep-awake, preconditions, baseline

1. Write the `Started (UTC)` line (see «Timing»). Start the keep-awake process (P-2). Check the Preconditions.
2. `git switch -c feature/plan-54-pay-legal-framework-reference-data origin/main`. Bring in PLAN-53's merge note, and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan54-baseline.md` (P-3): 0.2 to 0.6, and the demo's picture (P-16).
4. If port 22 is blocked, record it, and take the demo's picture at the start of P8.

Commit: `chore(pay054): baseline, the readers and writers of the framework's tables and of manual inputs, the demo's payroll picture`.

## P1 — The data and the fingerprint (plan §2 and §4 fields, P-7, P-12)

- **The entities and fields** of P-7 and P-9, the editors' record of P-8, `HashSchemaVersion`, and fingerprint v2.
- **`CloneAsDraft`** carries everything (P-6), with its reflection test.
- **`PAY054_LegalFrameworkPacks`,** with the existing rows set by defaults, row security proved as a non-superuser.

Commit: `feat(pay054): a version holds its statutory deductions, holidays, priority rules and references; fingerprint v2; migration PAY054 keeps every existing version and input as it is`.

## P2 — Create, import, and the version that governs a period (plan §1, P-4, P-5, P-6)

- **The schema, the pack, the importer, the create command;** the identity proof against the seed.
- **The API** with its antiforgery endpoint; the screens (`Create`, `Import`, `Details`, `Edit` with the date).
- **`TryPublish`,** the resolver, `GetActiveConfigurationAsync`, `Activate`.
- **The first-run diagnostics;** the operator's script; `deploy-organization.ps1`.
- **Tests:** `Pay054FrameworkPackImporterTests` (every rule, every refusal leaving no row, the decisions); the resolver and publishing cases of P-6; the policies in `PayrollWriteActionsTests`; the antiforgery answers (400 without the token, nothing written); the interface guard; `OrgScopeCoverageTests`.

Commit: `feat(pay054): a framework is created or imported as a draft with its own effective date; a published version never cuts back in time, and a superseded one still governs its own months`.

## P3 — The engine reads the version (plan §2, P-7)

- **The v2 paths** of the statutory deductions, the holidays, the priority rules and the export; **the v1 legacy path** with its log line; `DefaultBrackets` out of every v2 path; `IsStatutory` read-only.
- **Tests:** `Pay054HashV2Tests`; v1 runs and exports unchanged; a v2 run reads only its rows; a `DeductionTypes` edit after pinning moves nothing; the golden pack and the pipeline tests unchanged.

Commit: `feat(pay054): a v2 run reads its statutory deductions, holidays and priority rules from its version, and its export takes the insurance shares from it; v1 runs compute as before`.

## P4 — The cycle's governance (plan §3, P-8, P-10)

- **`PayrollFrameworkGovernance`,** the attest policy and its setting, the interim gate with the exceptions' dates at the four acts and at a run's start, the attestation's checks and record.
- **The indicators** on the run and the dashboard; the 400 refusals on the forms.
- **Tests:** `Pay054FrameworkGovernanceTests` (every actor rule, every act, the state unchanged on each refusal); the gate with a controlled clock on both sides of 2027-01-31; the start-up gate untouched.

Commit: `feat(pay054): three people approve, attest and publish a framework; an interim value needs an active STR-002 exception, and a run stops when it expires`.

## P5 — Manual inputs with a second person (plan §4, P-9)

- **The view models, the commands, every reader of pay filtered, the screens, the setting.**
- **Tests:** `Pay054ManualInputApprovalTests` (a pending input is absent from a run; approved, it counts; the creator cannot decide it; a rejection needs a reason; a posted run id is ignored; consumers and seeders write `Approved`/`System`); the setting honoured in Development only.

Commit: `feat(pay054): a manual allowance, deduction or adjustment counts only after a second person approves it`.

## P6 — Acceptance, then the independent review (plan Acceptance, P-11, P-13, P-14, P-15)

1. **The pieces:** the persona, `AcPay54LegalFrameworkTests`, `ci-scope.json`, `ac-ownership.json`, `scripts/pay054-validate.ps1`.
2. **Acceptance.**
   1. Check the disk (P-2). Reset the local stacks (the persona). Build the images, one service at a time. Start the prod-like stack, and record the time to ready.
   2. Run `AcPay54LegalFrameworkTests` (AC-PAY-54a..f), positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   3. Then P-14's lists, `scripts/pay054-validate.ps1` (and `-SelfTest`), and the seeded counts by psql. All required classes must pass.
   4. `down -v`. Restore the committed evidence.
3. **The full Testcontainers step on the host,** with CI's filter.
4. **Independent review** (one fresh subagent), before the server, so the server build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - an existing payroll number, fingerprint or export that moved;
   - a v2 run that still reads a general table, or a fallback;
   - a value that governs pay but is not in fingerprint v2;
   - a version that cuts back in time, a month with no governing version, or an active configuration that depends on row order;
   - an act of the cycle one person can do twice, or a refusal that changes state;
   - an interim value published or calculated without an exception active that day, or a date read in UTC where Damascus decides;
   - a pack refusal that leaves a row, an upload that can echo or log its content, an API write without an antiforgery token;
   - a pending manual input that reaches a run, a payslip or an export;
   - a migration whose defaults or row security leave an existing row without its value;
   - a test changed to pass rather than to keep its meaning, or a part of the plan deferred, skipped or listed to save time;
   - something PLAN-53's tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(pay054): AC-PAY-54a..f pass on the prod-like stack; review fixes; CI scope, ownership and the new persona`.

## P7 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to `PayrollDbContext` after yours, remove yours and add it again on top. Rebuild, and rerun the unit, architecture and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you.

Commit (only if the merge needed fixes): `fix(pay054): main merged, conflicts resolved`.

## P8 — The demo server (P-16, P-17)

Record every server change and its undo command **as you make it**.

1. Check SSH and P-17's condition.
2. Take the backup, then **verify** it. Take the read-only picture (P-16), if P0 could not.
3. **Check the server's disk, build and deploy.** Run the build once, detached, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`. If the deploy stops at `keycloak-reconcile` after PostgreSQL restarts, run the same command again (PLAN-40 incident). Record the downtime and the time to all healthy.
4. **Then check**, with evidence in `artifacts/plan54/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them. The new checks go in an `AcPay54VpsChecks` class; each one writes realistic Arabic text, keeps its marker in a field the reader does not rely on (the version's code, an input's reference), and leaves nothing open.
   1. **The migration:** PAY054 recorded; every existing version v1 with its fingerprint as in the picture; every existing input `Approved`/`System`, the counts as before.
   2. **The cycle** (P-17's inert version): the processor imports the real pack under a check code, from a date inside a posted month → `Draft`; publishing it now → 400; the processor approving → 403; the approver approves; the approver attesting → 400; the attestor attests; publishing → 400 for the posted month (D-PAY-13). The version stays `Approved`, attested and unpublished; the decision register's checksum as before.
   3. **A malformed pack** (a bracket of 120%) → 400, no row.
   4. **A manual input:** the processor enters an allowance for a demo employee → `PendingApproval`; his own approval → 403; the approver rejects it with a note → `Rejected`.
   5. **The screens** open in Arabic under the enforced CSP with no violation (PLAN-67's method), with names, not ids.
   6. **Everyone else is unchanged:** the PLAN-36..53 server checks pass, and `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass. The AC-WF-46 sweeps check is rerun alone after a sweep cycle.
5. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-17 says, prove the demo works, and record it.

Commit: `test(pay054): demo server with the framework's cycle and manual inputs; PLAN-36..53 server checks still green`.

## P9 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/PAYROLL_CONFIG_VERSIONING.md`: the pack, fingerprint v2, which version governs a period, the people of the cycle, the exceptions and their expiry;
   - `docs/PAYROLL_RULES.md`: what a v2 run reads, and the v1 legacy path;
   - `docs/PILOT_DEPLOYMENT.md`: step 3 is the import; two payroll approvers per agency (D-PAY-12); the exceptions' end on 2027-01-31 (P-10);
   - `docs/SOD_MATRIX.md`: the cycle's four acts and the manual inputs;
   - the «الحالة» lines of G-PAY-01, G-PAY-10, G-PAY-12 (the structure) and G-PAY-09 (the framework's part) in `docs/PAY_001_GAP_STUDY.md`;
   - `docs/CI_AND_TESTING.md`: the new class, the dated limit of P-10, the machine notes that still hold;
   - `OPS_VPS_SINGLE_HOST.md`: what the demo users see, the backup location, the change table, the rollback note;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local gates, before any push:**
   - the full Testcontainers step on the host, with CI's filter (P6, if nothing it builds or tests changed since; otherwise again);
   - the local CI steps (P-2) on HEAD;
   - then `pwsh -NoProfile -File scripts/plan035-prepush.ps1`.

   All must be green, with every step not run named. The report is committed before them, because a commit after a green local run needs another run.
3. **`reports/plan54-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations (P-6's governing version, P-8's order and publisher, the name clash, anything else). No deviation defers part of the plan.
   4. **The framework:** the pack and its proof of identity, the importer's rules, the API and the screens.
   5. **Versions and periods:** publishing, the resolver, the active configuration, D-PAY-13.
   6. **The engine:** what a v2 run reads, the v1 path, the export, the fallback.
   7. **Governance:** the people of each act, the interim gate, the dates, the indicators.
   8. **Manual inputs:** the readers filtered, the system writers, the screens.
   9. The existing tests that changed, each with the meaning it keeps; the numbers that did not move.
   10. The guards and their own negative tests.
   11. Unit, architecture, integration and acceptance results; the PR run's artifact link as the evidence of record.
   12. **The server:** before and after, changes and their undo, the backup location, the rollback note, the inert version.
   13. **Changes for API clients and operators:** the new API, the script, the deploy step.
   14. Items that other plans own (Scope boundaries), and defects for later plans — the double tax of the interim values among them.
   15. **The dated limit:** 2027-01-31 (P-10).
   16. **Timing:**
       - total wall-clock;
       - active time, waiting for CI, and every stop with its cause;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `pwsh -NoProfile -File scripts/plan-timing.ps1 -From 25 -To 67`.
   17. **A readiness note** for PLAN-55..59 and PAY-E: the names they take, the v2 container they fill, and the cycle a new framework goes through.
4. **Merge (P-18), by you, in the same turn as the pre-push check:**
   1. Merge the feature branch into a new `release/plan-54` from `main` (`--no-ff`).
   2. Write `reports/plan54-pr.md` (short).
   3. `git push -u origin release/plan-54`
   4. `gh pr create --base main --head release/plan-54 --title "PLAN-54: the payroll legal framework and its reference data in production" --body-file reports/plan54-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then:
   - `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`;
   - record the PR link, each check result, the merge commit and the post-merge run in `reports/plan54-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`;
   - commit that note on a local branch `docs/plan-54-merge-note`, as the plans before did, so the next plan brings it in. **Do not open a second PR for it;**
   - stop the keep-awake process by its PID (P-2).

## Definition of done

- **An empty database with production settings** reaches its first calculation through import, approval, attestation and publication only, without SQL or seed (the integration test and AC-PAY-54a).
- **A v2 run reads no general table** that anyone can edit; fingerprint v2 covers the statutory deductions, holidays, priority rules, references and statuses.
- **A superseded version still governs its own months,** and no publication cuts back in time or into a posted period.
- **Nothing interim is published or calculated** without an exception active that day.
- **No manual input enters a calculation** without a second person.
- **Every existing payroll result is unchanged,** unit for unit.
- **Nothing in the plan was deferred to save time.**
- **The tests:** AC-PAY-54a..f = Pass (the PR run's artifact is the evidence of record); `Pay002ImmutableConfigTests` and `PayrollEnginePipelineTests` green; the PLAN-36..53 suites green.
- **The server** runs this code, with no published version, decision or pay datum changed.
- **The documents** P9 lists are updated, the «الحالة» lines among them.
- **The merge** is done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed for payroll users, and what the owner will see on the demo;
- the date 2027-01-31, and the decision it asks of the owner;
- how long the plan took, how long it waited for CI, and every stop with its cause;
- the PR link;
- the path of the final report.
