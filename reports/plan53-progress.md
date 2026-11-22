Started (UTC): 2026-11-21T00:15:16.7657533Z

(Stamp taken with `date -u` by the session's first command: this machine is Linux, so `Get-Date` was not the source.)

# PLAN-53 progress (checkpoint)

## Phase status

| Phase | Status |
|-------|--------|
| P0 | BLOCKED at the precondition check; nothing else started |
| P1..P9 | not started |

## BLOCKED (UTC): 2026-11-21T00:16:30.1701242Z — preconditions fail on this machine

The prompt was written for the owner's Windows machine (`D:\my projects\HRManagement`, PowerShell 5.1, `gh` as
`omardefdev`, `ssh hrm-vps`). This session runs on a different machine. Read-only checks only were run; no branch was
created, nothing was committed, no server step, no Docker step, no keep-awake process started.

| # | Precondition | Found here | Result |
|---|--------------|------------|--------|
| 1 | In `D:\my projects\HRManagement`, on `main` or `docs/plan-67-merge-note` | `/home/omar/HRManagement` on host `omar-HP-250-G7-Notebook-PC` (Linux 6.14, PowerShell 7.7.0 at `~/bin/pwsh`, .NET SDK 10.0.400), on branch `fix/dev-scripts-linux` (same commit as `main`, `d9552bad`) | FAIL |
| 2 | `origin/main` holds PLAN-67 | `origin/main` = `main` = `d9552bad` (merge of PR #51, `release/plan-67`) | pass |
| 3 | Working tree clean except the listed files | Uncommitted edits to `scripts/dev-infra-up.ps1`, `scripts/dev-run-hrcore.ps1`, `scripts/merge-keycloak-realm-extensions.ps1` (67 lines added: Linux dev-script fixes). Not on the allowed list. Left untouched | FAIL |
| 4 | `ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm` | `Could not resolve hostname hrm-vps`: there is no `~/.ssh/config` and no `hrm-vps` alias on this machine. This is not the «port 22 blocked» exception: the alias and the key are absent | FAIL |
| 5 | `gh auth status` shows `omardefdev` with `repo` and `workflow` | Signed in as `omardefdev-gif` (scopes `gist`, `read:org`, `repo`, `workflow`). On `omardefdev/HRManagement` this account has `pull: true`, `push: false` | FAIL |

Also found (they bear on P-1 and P-2):

- **PLAN-67's merge note** (`cbbbeaab`, branch `docs/plan-67-merge-note`) is not in this clone and not on `origin`: it
  is a local branch of the Windows machine. P-1's cherry-pick cannot be done here.
- **This prompt file** is not under `docs/acceptance-plans/execution/` (the last one there is `PLAN-67-AGENT-PROMPT.md`).
- **Disk:** `/` has 26 GB free of 233 GB (89% used). Docker: images 34.4 GB (27.1 GB reclaimable, mostly other
  projects'), build cache 0.77 GB. Below P-2's 40 GB line with almost no cache to prune; above its 15 GB stop line.
- **Memory:** 15 GB RAM, 8 cores; about 5 GB available now.
- **Running containers:** the dev infrastructure `hrm-keycloak`, `hrm-postgresql` (5433), `hrm-rabbitmq` (5673, 15673),
  `hrm-clamav` (3310); and other projects' `mod-task-*` (8080) and `checkpoint-app-*` (80). None was touched.
- Side PRs #40 and #42 are still open. Not touched.

### What cannot be done from this machine as it stands

- **Push, PR, merge (P-18):** `omardefdev-gif` cannot push to `omardefdev/HRManagement`.
- **The demo server (P0's picture, P8):** no SSH alias or key for `hrm-vps`.
- **P-1's first commit:** the merge note commit is absent.

### Question for the owner

Where should PLAN-53 run?

1. **On the Windows machine**, as the prompt says: start the session there. Nothing here needs undoing (only this file
   was written, untracked).
2. **On this Linux machine.** Then the owner provides, before P0 continues:
   - `gh` signed in as an account that can push to `omardefdev/HRManagement` (`gh auth login` as `omardefdev`, or push
     rights for `omardefdev-gif`);
   - the `hrm-vps` entry in `~/.ssh/config` with its key (the host key must match
     `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`);
   - a decision on the three uncommitted scripts (commit them on `fix/dev-scripts-linux`, stash them, or let them ride
     along uncommitted);
   - PLAN-67's merge note: push `docs/plan-67-merge-note` from the Windows machine, or say the plan goes on without it;
   - whether the dev containers `hrm-*` may be stopped during acceptance runs (memory and ports), and whether 26 GB
     free disk is to be raised first.
   The prompt's Windows-only steps would be mapped to this machine and recorded as deviations: PowerShell 7 instead
   of 5.1, `systemd-inhibit` for keep-awake, `df` and `ss` instead of drive C and `netsh`.

## Decisions (with reasons)

- No branch created and nothing committed: the tree holds the owner's uncommitted work on another branch, and the
  prompt says to stop when a precondition fails.

## Files changed

- `reports/plan53-progress.md` (this file, untracked).

## Test results

None run.

## Server changes and their undo commands

None. The server was not reached.

## Open issues

The BLOCKED question above.

## Timing

- 2026-11-21T00:15:16Z to 00:16:30Z: precondition checks (1 min 14 s).
- From 00:16:30Z: session stands still, cause: BLOCKED on preconditions (wrong machine, no push rights, no server access).

- 2026-11-22: the owner said the Windows machine was formatted, so PLAN-53 runs on this Linux machine. He gave the
  server's address. `~/.ssh/config` now holds the `hrm-vps` alias (user `hrm`, key `~/.ssh/id_ed25519`); the server's
  ED25519 host key was scanned, matched `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`, and was added to
  `known_hosts`. Port 22 is reachable without the VPN. `ssh -o BatchMode=yes hrm-vps whoami` answers
  `Permission denied (publickey)`: this machine's key (`SHA256:92N5URTr2tg+4iq0wewPWB5wrlyaZRPeqdKcaeHWv+g`) is not in
  `/home/hrm/.ssh/authorized_keys`. The owner adds it through Contabo's VNC. Still BLOCKED; no server change was made.
- Lost with the Windows machine: PLAN-67's merge note (`cbbbeaab`), unless the owner has a copy.

- Stop 1: 2026-11-21T00:16:30Z to 2026-11-22T12:32:25Z = **36 h 15 min 55 s** standing still, cause: BLOCKED on
  preconditions (the plan's machine was formatted; this machine had no push rights and no server access).

## Resumed (UTC): 2026-11-22T12:32:25.5146060Z — the owner restored access; the preconditions pass on this machine

Reason: the owner's message (`ssh hrm-vps whoami` → `hrm`). Re-check:

| # | Precondition | Now |
|---|--------------|-----|
| 1 | The main folder, one session | The Windows machine is formatted (owner, 2026-11-22). **The main folder is now `/home/omar/HRManagement`** on this Linux machine. The branch `fix/dev-scripts-linux` was merged by the owner (PR #52). pass, as a deviation |
| 2 | `origin/main` holds PLAN-67 | `origin/main` = `4ce01f59` = PLAN-67's `d9552bad` + PR #52 (three dev scripts, no product code). pass |
| 3 | Clean tree | clean (only this file, untracked). pass |
| 4 | `ssh hrm-vps` → `hrm` | `hrm`; host key matches; port 22 reachable without the VPN. Running tag `1ee455bf1a6f`, 22 containers, 38 GB free on the server. pass |
| 5 | `gh` as `omardefdev` | active account `omardefdev`, scopes `repo` and `workflow`, `push: true`. pass |

## Deviations forced by the machine (decided 2026-11-22; none drops a part of the plan)

- **Linux instead of Windows.** PowerShell is 7.7.0 (`~/bin/pwsh`), which the owner installed: scripts run with
  `pwsh -NoProfile -File …`. Every new or changed `.ps1` still stays ASCII-only and 5.1-compatible in syntax (P-15),
  because CI and a future Windows machine may run it.
- **Keep-awake (P-2):** `systemd-inhibit --what=sleep:idle --who=plan53 … sleep infinity`, detached, PID **325760**
  (started 2026-11-22T12:33:32Z). Stop at the end: `pkill -f 'systemd-inhibit --what=sleep:idle --who=plan53'`.
  It changes no setting.
- **Disk and ports (P-2):** `df -h /` instead of drive C; `ss -ltn` and `docker ps` instead of `netsh … excludedportrange`
  (Linux reserves no port ranges that way).
- **PLAN-67's merge note (P-1):** commit `cbbbeaab` is lost with the Windows machine. Replaced by a record rebuilt from
  GitHub, appended to `reports/plan67-progress.md` (commit `5089fc8c`). No merge-note branch exists here to delete.
- **The prompt file (P-1):** taken verbatim from the session's transcript, committed as `37717393`.
- **VPN (P-2):** SSH and Docker both work with no VPN: no ordering needed.

## Machine facts to watch

- Disk `/`: **18 GB free** at resume (26 GB the day before). Docker: images 34.4 GB (other projects' included), build
  cache 0.77 GB. P-2's stop line is 15 GB. The owner was told at resume.
- 15 GB RAM, 8 cores. Dev containers `hrm-keycloak`, `hrm-postgresql`, `hrm-rabbitmq`, `hrm-clamav` and other
  projects' containers run beside the session.
- The server's `authorized_keys` holds two keys: the lost Windows key (`SHA256:CkL6…`) and this machine's. Not changed.

## P0

- Started (UTC): 2026-11-22T12:32:25Z (the resume).
- Branch `feature/plan-53-iam-payroll-salary-change` from `origin/main` (`4ce01f59`).

- Baseline (P-3) written: `reports/plan53-baseline.md`. Build 0 errors (4 min 42 s); architecture 313/313; unit
  `Pay0|Payroll` 144/144 after linking `powershell` to `pwsh` in `~/bin` as CI does (undo: `rm ~/bin/powershell`).
- Inventories by search under `artifacts/plan53/inventory.*` (two read-only subagents, 2 min 52 s and 8 min 20 s).
- **Two pay-profile writers the prompt did not list:** `HandleSeparationCompletedHandler` (`InboundEventHandlers.cs:212-213`)
  and `HandleDisciplinaryActionHandler` (`:612-613`) close a profile. They stay, named in `PayProfileWritersTests`.
- **No `Authorization:Policies:*:Roles` key exists in any settings file today** (roles come from code defaults; only
  `AdminExcludedPolicies` is configured). P-4 adds the `Payroll.MasterData` roles to Payroll's three files and the mirror.
- Demo picture (read-only, 12:36Z): tag `1ee455bf1a6f`, 22 containers, 38 GB free; 425 active profiles (checksum
  `3b2e10719627b05f17069bfca7a784f2`), 10 grades all in use, 1 scale; runs 2026-01..06; 500 current records without a
  regime and 500 open `UnclassifiedRegime` exceptions, so a Regular run would be refused there (finding). Both
  `e2e-payroll-*` users present and enabled.
- Notes for later phases (from the inventory, so the big files need no second read):
  - outbox pattern: stager `[StagesOutboxMessages]` in `Payroll.Infrastructure/Integration/PayrollEventPublisher.cs` behind
    `IPayrollEventPublisher`, then one save; R4 needs the type in backticks in `docs/EVENT_PUBLISHER_INVENTORY.md`;
    the guard test is `UnitTests/Architecture/OutboxPublishOrderingArchitectureTests.cs`;
  - job pattern: `AssetApprovalStartRetryJob` (Admin.Logistics) + stagger of `RecruitmentOfferExpiryJob`; lock key
    `4_001_013`; gauge pattern `AssetApprovalMetrics`, meter added in `OpenTelemetryExtensions.cs:46-65`;
  - org scope: `MovementsOrgScopeKeys` pattern; `AddOrgScopeFilter<T>` + `AddSnapshotOrgScopeProvider<T>`;
    `InOrgScopeAsync(HttpContext, db)` for lists; a null unit is central (never shown to a restricted user);
    `OrgScopeCoverageTests` has `Configurations` and `Ceilings` (payroll = 24) to update; HTTP tests through
    `Iam052PgServiceHost<TEntry, TContext>.StartAsync`;
  - policies: `HrManagementAuthorizationExtensions.cs:212-225`; `RolesPolicy` in `ConfiguredRolesAuthorization.cs:68`;
  - time: `HrLocalTime.ToLocal(utc, HrLocalTime.Zone)`; `TimeProvider.System` is in Payroll's DI through `AddLegalControl`;
  - regime: `ServiceRegimeExceptionService.ResolveAsync` applies the transition itself (two saves, no event published);
    patches pattern `AcceptanceMdmReferencePatch`; the backfill runs only on restarts under the default tenant.
- Finished (UTC): 2026-11-22T12:44:54Z — P0 committed (`92fab7c6`). (That commit carries `12:58:00Z` here and as P1's start: an estimate written before the clock was read. Corrected to the clock's value one minute later; no other stamp was touched.)

## P1

- Started (UTC): 2026-11-22T12:44:54Z.

- 12:45Z to 12:51Z: `Payroll.MasterData` and `Payroll.ChangeCancel` (rules written once in `PayrollPolicyRules.cs`), the
  sixteen attributes, the settings and the mirror, the list deleted, `PLAN-53` out of `CeilingList.Owners`, the planted
  rows moved, `iam051-validate.ps1` and `ux067-validate.ps1` updated, `PayrollWriteActionsTests` written. Build 0 errors;
  architecture 344/344 (313 + 31 new) at 12:51:18Z.
- **Stop 2: 2026-11-22T12:51:44Z to 15:16:33Z = 2 h 24 min 49 s.** Cause: the owner stopped the running command (the
  whole unit suite and the validate scripts), and the machine was restarted (uptime 4 min at the resume). Nothing was
  lost: the working tree held every P1 edit; the unit run had rewritten `testdata/*/out` files, restored.

### Resumed (UTC): 2026-11-22T15:16:33.8524623Z — the owner's message «اكمل»

- The keep-awake process died with the restart; started again (`systemd-inhibit`, same undo command).
- Next: the whole unit suite (in the background, log under `artifacts/plan53/`), then the validate scripts, the format
  check, the P1 commit.

- P1 results (15:17Z to 15:27Z): build `-warnaserror` 0 errors; **unit 6353/6353** (8 min 11 s); architecture 344/344;
  `iam051-validate.ps1` pass, its `-SelfTest` 10 planted defects refused (one more: a sensitive list that comes back);
  `ux067-validate.ps1` and its `-SelfTest` pass; format clean on the changed files.
- **Decisions in P1:**
  - `Payroll.ChangeCancel` admits whoever passes `Payroll.Process`, `Payroll.MasterData` or `Payroll.Approve` (P-4 names
    the first and the third; `Payroll.MasterData` is added so a requester of a reference change whose configured roles
    are not the processor's can still cancel his own request). The three rules are written once (`PayrollPolicyRules`).
  - `WriteActionsHaveExplicitPolicyTests`: Payroll is no longer left out of the «read policy only» check; its
    `ExportJobs.Cancel` (P-4: `Module.Payroll`, the owner check stays) is declared like the four other services'
    `ExportJobs.Cancel`, so the declared reads count goes from 22 to 23. The action is not new: the check did not see
    Payroll before. Goes into the report.
  - `GuardSelfTests` now also proves that a row signed `PLAN-53` is refused.
- Finished (UTC): 2026-11-22T15:27:16Z — P1 committed.

## P2

- Started (UTC): 2026-11-22T15:27:16Z.

- Entities `PayProfileChangeRequest` and `PayrollReferenceChangeRequest`, their enums (Arabic `[Display]`),
  `EmployeePayProfileVersioning` (close the day before, copy every field), the EF configuration, and migration
  `20261122153208_PAY053_SalaryChangeRequests` (two tables, row security, filtered unique indexes
  `IX_PayProfileChangeRequests_OneOpen` on `(TenantId, EmployeeId)` where `State IN (0, 1)` and
  `IX_PayrollReferenceChangeRequests_OneOpen`, both relations `Restrict`). No BOM (the tool wrote one in both files;
  stripped). `has-pending-model-changes`: none.
- **Decisions in P2:**
  - The request keeps who cancelled it in its own fields (`CancelledBy`, `CancelledByName`, `CancelledAt`,
    `CancelReason`), apart from the decision, so a scheduled request that is cancelled keeps its approver. Four fields
    more than the plan's table; the contract only grows.
  - `PayrollReferenceChangeRequest` also holds `EntityLabel` (the record's code and name for lists) and the same cancel
    fields; one open request per reference record (a second filtered unique index).
  - **A version cannot start before the version it replaces** (`EmployeePayProfileVersioning.StartsBefore`): otherwise
    the days between would be paid by a version that never held them. Refused at submit, approval and activation (P3).
  - States are stored as integers (the filter of the index names 0 and 1).
- Tests (15:35Z to 15:39Z): build `-warnaserror` 0 errors (1 min 22 s); `Iam053ApplierCopyForwardTests` 11/11;
  `Iam053PayProfileChangePostgreSqlTests` 3/3 on PostgreSQL as the non-superuser role `hrm_rls_app` (row security: agency
  B sees and writes nothing of agency A; one open request; a used grade and a scale with grades not deleted); unit
  `Pay0|Payroll|Architecture|Migration|Ten0|Rls|Data001` 466/466; architecture 344/344; integration without
  Testcontainers `Pay|Outbox|Hcm027` 48 passed, 1 skipped; format clean.
- Testcontainers run on this machine with no VPN issue (12 s for the three tests).
- Finished (UTC): 2026-11-22T15:39:37Z — P2 committed.

## P3

- Started (UTC): 2026-11-22T15:39:37Z.

- **P3 checkpoint, 2026-11-22T16:05Z (not committed yet; everything below is in the working tree and builds):**
  - Written: the three event contracts; `Features/PayProfileChanges/` (result and messages, rules, applier with
    `ApplySystemChangeAsync`, Submit/Decide/Cancel handlers, activator); `PayrollCalendar` (Damascus dates from
    `TimeProvider`); `PayProfileChangeMetrics`; `PayProfileChangeEventPublisher` (stager); `PayrollOrgScopeKeys`;
    `PayProfileChangeActivationJob` (lock key `4_001_013`, meter `HRManagement.PayrollSalaryChange`);
    `PayProfileChangesApiController`, `PayProfileChangesController` and its two views, the request form
    (`EmployeePayProfiles/Create` with `PayProfileChangeRequestViewModel`), the PUT alias (202), the dashboard tiles, the
    menu link, the scope filter and the job in `Program.cs`, the `Payroll:SalaryChange` and `Payroll:MasterData` settings in
    the three files and the mirror. `UpsertEmployeePayProfileCommand` and its handler are deleted.
  - Lists: `EmployeePayProfiles` left the org-scope list (ceiling 24 → 23) and `EmployeePayProfiles.Create` the
    entity-binding list (16 → 15); Payroll's keys are registered in `OrgScopeCoverageTests.Configurations`.
  - Docs: the three event rows in `EVENT_PUBLISHER_INVENTORY.md`, `MESSAGE_EVENT_MATRIX.md` and
    `rel-compat/event-schema-inventory.json`; `docs/policy-rules-inventory.csv` regenerated (one row more).
  - Tests so far: `Iam053*` unit 116/116 (state machine, automatic sources, Damascus dates, run kinds, copy-forward);
    architecture 344/344; `Pay023FrozenRunIsolationTests` rewritten (not run yet).
  - **Decisions in P3 so far:**
    - The commands return a record `PayProfileChangeResult(Success, RequestId, State, Error, Failure)`: the plan's tuple
      plus the kind of refusal, which gives the HTTP status (Invalid 400, Conflict 409, SelfDecision and Forbidden 403,
      NotFound 404). A refused form action renders its page with the same status.
    - The applier's public entry points each do the one save (`ApplyAsync` for an approval that is due or a scheduled
      request; `ApplySystemChangeAsync`), because PLAN-40's guard wants the publish and the save in one method.
    - The employee's scope key is the unit of his **active** pay profile (`KeyBy EmployeeId` in `PayrollOrgScopeKeys`),
      so the filter answers 404 for a foreign `employeeId` before the action; the handler checks it again.
    - Only what differs from the current profile is stored as proposed (the PUT alias always sends both values).
    - A bound of a grade that is zero is «not set» and is not applied (P-5: «when set»).
    - Messages format numbers and the month with the invariant culture (an Arabic culture put a direction mark in
      «2026/12»).
    - **Found, older than this plan:** `scripts/policy-inventory.ps1` could not read the catalog since PLAN-67 added
      `NameAr` between `Domain` and `Status` (it threw «0 governed»). Its regex now tolerates that line; `-Check` passes.
    - **The known limit, read from the rules (P-5):** the engine prorates only when `0 < employee days < period days`
      (`PayrollRules.cs:29-33`). A month calculated after a later month's change has applied reads the newer active
      version, whose dates leave that month no day, so it is paid **the full new salary** (not zero). The old
      `Pay023` test showed exactly this (June calculated with a profile effective in November). For PLAN-56.
  - Then written and passing: `PayProfileWritersTests` (Roslyn, 19 tests: seven named writers, a new writer fails, a
    stale name fails); the transaction's PostgreSQL cases on the real bus outbox
    (`Iam053PayProfileChangePostgreSqlTests`, 9: two concurrent approvals, two concurrent submits, a failure in the
    applier's save from the application and from a database trigger, one active profile, the audit row in the same
    save without the amount); `Iam053ActivationJobTests` (4, three agencies as the non-superuser role);
    `Iam053PayrollScopeHttpTests` (5: PLAN-52's generated test on Payroll.Web, the restricted user's walk, the form,
    the roles); the new write actions in `PayrollWriteActionsTests`; `docs/PAYROLL_SALARY_CHANGE.md`.
  - **A defect the generated scope test caught:** the request form answered 500 under the Arabic culture
    (`[Range(typeof(decimal), "0.01", …)]` parsed its limits with the culture's separator). Fixed with
    `ParseLimitsInInvariantCulture`; a lasting test opens the form and posts it.
  - **A test fact:** the outbox sequence advances on a rolled-back insert, so the PostgreSQL tests count the committed
    rows of `OutboxMessage` (the test host runs no delivery service).
- **P3 results (16:25Z to 16:37Z):** build `-warnaserror` 0 errors; **unit 6488/6488** (7 min 7 s); architecture
  355/355; integration without Testcontainers (`Pay|Outbox|Hcm027|IntegrationHost|Consumer`) 85 passed, 1 skipped;
  Testcontainers `Iam053|Pay023|Iam052ServiceScopeHttp` 22/22 (48 s); `iam050`, `iam051`, `iam052`, `ux067` validate
  scripts pass; `policy-inventory.ps1 -Check` passes; format clean.
- Finished (UTC): 2026-11-22T16:37:54Z — P3 committed.

## P4

- Started (UTC): 2026-11-22T16:37:54Z.

- Written: `Features/ReferenceChanges/` (contracts with the allowlisted `PayGradeChange` and `SalaryScaleChange`, the
  applier with validation at submit and at approval, Submit/Decide/Cancel); `SalaryScaleChangeViewModel`,
  `PayGradeChangeViewModel`, `PayrollReferenceDeleteViewModel`; the two controllers' Create/Edit/Delete as requests
  (Delete got a page for its reason and legal reference) and a Details page each showing the open request;
  `PayrollReferenceChangesController` («طلبات البيانات المرجعية», `[OrgScopeNotApplicable]`) with its list and its
  before/after details; the start-up warning for `Payroll:MasterData:RequireApproval`; the menu link; the section in
  `docs/PAYROLL_SALARY_CHANGE.md`. The four entity-binding rows of PayGrades and SalaryScales left (ceiling 15 → 11).
- **Decisions and findings in P4:**
  - **The plan's «existing exceptions report» does not exist in Payroll.** Rule 1 (new bounds change no profile; the
    profile outside them is shown) is met by a new filter on the pay profiles screen («خارج حدود الدرجة», through the
    branch scope), a count on the grade's details page, and a count on the request's page before approval. The rule is
    written once (`EmployeePayProfileRules.OutsideGradeBounds`) and proved in `Iam053ReferenceChangeTests`.
  - A deletion needs a reason and a legal reference, so `Delete` has a GET page; the POST keeps its name and policy.
  - One open request per reference record (the second filtered index); two creations with the same code are both
    accepted and the second approval is refused (409).
  - With `RequireApproval=false` in Development the request is recorded `Applied` with no decider.
  - `PayrollReferenceWritersTests` (Roslyn): only the reference applier, the two change records and the seed write a
    scale or a grade.
  - **Found, older than this plan:** a form field of type decimal with a fraction («8250.75») fails to bind under the
    service's Arabic culture (the framework reads it with the culture's separator), in every service. This plan's
    forms bind through `HrInvariantDecimalModelBinder` (by attribute, nothing global); the app-wide defect goes into
    the report for a later plan.
- **P4 results (16:58Z to 17:09Z):** build `-warnaserror` 0 errors; **unit 6540/6540** (7 min 6 s); architecture
  358/358; integration without Testcontainers 85 passed, 1 skipped; Testcontainers `Iam053|Pay023|Hcm027` 30/30;
  `iam051`, `iam052`, `ux067` validate scripts pass; format clean.
- Finished (UTC): 2026-11-22T17:09:43Z — P4 committed.

## P5

- Started (UTC): 2026-11-22T17:09:43Z.
- Machine at the start of P5: see the disk line written when the lab stack is prepared.

- **P5 checkpoint, 2026-11-22T18:02:28Z (nothing of P5 committed yet; all of it is in the working tree):**
  - **The machine and the images.** Disk `/` 14 GB free. This machine never built the acceptance images. On Linux
    `acceptance-up.ps1` builds all seventeen at once; with 18 GB free and a link of 90 to 500 KB/s that would fill the
    disk and take hours. So the images are built by a local helper, **not committed**
    (`artifacts/plan53/build-images-sequential.ps1`): one service at a time, the first from the registry's base images,
    the rest from the local base images `acceptance-up.ps1` itself supports (`hrm-local-dotnet-sdk:10.0.400`,
    `hrm-local-dotnet-runtime:10.0`, `REUSE_LOCAL_RUNTIME_BASE=true`), and the build-stage cache pruned after each image
    (`docker builder prune -f --filter type=regular`, then `type=source.local`; the NuGet cache mount and the images stay).
  - **NuGet cache seeded from the host** (`artifacts/plan53/nuget-seed/`, through `docker compose build`, because
    `docker build` has no buildx here): 359 packages, 4.3 GB, into the cache mount `hrmanagement-nuget` the service
    Dockerfile uses; `dotnet restore --locked-mode` still verifies every package. The temporary context copy was pruned
    (4.64 GB reclaimed, recorded prune 1).
  - **First build attempt** (17:13:12Z to 17:29:37Z, 16 min 25 s) **failed**: the pull of
    `mcr.microsoft.com/dotnet/sdk:10.0.400` was reset by the registry at about 95% («connection reset by peer»). The
    apt layer of the runtime base took 403 s. No image was produced.
  - **Now:** `docker pull mcr.microsoft.com/dotnet/sdk:10.0.400` with retries, started 17:30:24Z
    (`artifacts/plan53/p5-sdk-pull.log`); the build helper starts when it ends.
  - The owner's dev containers (`hrm-keycloak` restarting, `hrm-postgresql`, `hrm-rabbitmq`, `hrm-clamav`) and the other
    projects' containers were **not touched**: the acceptance compose resets container names and uses the 6xxx ports.
  - **Written while waiting (build green, tests green where run):**
    - personas: scenarios `AC-IAM-53` for `payroll-processor`, `payroll-approver`, `a-payroll-poster`,
      `b-payroll-approver`, and `AC-23` for `payroll-approver`;
    - `AcceptancePayrollRegimePatch` (HR.Core, called by `AcceptanceSeedOrchestrator` after the recruitment patches) and
      `Iam053AcceptanceRegimePatchTests` 5/5: it writes exactly what `ResolveAsync` and `ApplyTransitionAsync` write
      (proved by running both on equal data), one save per agency; it logs, per agency, the records it classifies, the
      exceptions it resolves and the open exceptions of other types it leaves. **Deviation in order:** the patch was
      written before the first stack start (the build is hours away on this link); the «before» numbers are its own log
      lines at the first start, and the «after» numbers come from SQL;
    - `Ac23PayrollFreezeTests` and `Ac25FraudHoldTests` rewritten on the request and its approval (compile clean; not
      run yet). AC-23: request from 2027-02-01 through the PUT alias (202), the approver's approval refused (409 naming
      2027/02), state pending, recalculation refused, net unchanged, March computed while the request is pending, the
      request cancelled at the end. **Its old last assertion** (`netB != netBefore` after a direct write) is replaced
      by «the request is still pending and unapplied after a later run»; that a run holds an applied change is proved
      by AC-IAM-53a and `Pay023`. AC-25: the fixture is a request from the first day of the current month (Damascus),
      approved and applied, salary inside the grade's bounds; every other assertion as it was;
    - the regime classes checked by reading: `AcIam50` and `AcS2s42` set `law50` by SQL (now a no-op), `AcInt37` finds
      the BR1 head first, `AcHcm29`, `AcMsg40`, `AcQa34` do not depend on an unclassified employee; no class and no
      verify count reads regime exceptions or transitions. To be confirmed by running them (P-14);
    - `scripts/iam053-validate.ps1` with `-SelfTest` (20 planted defects); today it fails only on the acceptance scope
      entries that P5 and P6 add (AC-IAM-53 required, AC-12/23/25 required, the ownership row);
    - the PUT alias body is renamed `PayProfilePutAliasRequest`, so `UpsertEmployeePayProfile` is nowhere in the product
      code (the validate script checks it).

- **P5 checkpoint, 2026-11-22T18:31Z — the images, without the registry's SDK image:**
  - The pull of `mcr.microsoft.com/dotnet/sdk:10.0.400` ran from 17:30:24Z to 18:19Z and never finished: the link gave
    14 to 17 KB/s to the registry (20 to 54 KB/s to other hosts, measured). The agent stopped it (no work waited on it:
    P5's code was written and tested meanwhile).
  - **Deviation (this machine only, nothing committed):** the two local base images `acceptance-up.ps1` supports were
    assembled from what the machine already holds (`artifacts/plan53/sdk-local/`): `hrm-local-dotnet-sdk:10.0.400` =
    the local `mcr.microsoft.com/dotnet/sdk:10.0` image (10.0.101) with its `/usr/share/dotnet` replaced by the SDK
    10.0.400 and the runtime 10.0.11 installed on the host (the same Microsoft linux-x64 files; `dotnet --version` in the
    image answers 10.0.400); `hrm-plan53-aspnet:10.0.11` = the local `aspnet:10.0` (10.0.1) with the host's runtime
    10.0.11. The first service installs curl and Kerberos on it and becomes `hrm-local-dotnet-runtime:10.0`, as the
    script does. CI builds from the registry's images; the evidence of record is CI's.
  - The sequential build started 18:20:53Z: the first image 430 s (apt over the slow link), then about 73 s an image.
  - `personas.json`: the scenarios had been lost by a `git checkout -- testdata` after a test run; written again and
    staged, so a restore of the test data keeps them.
  - `AcIam53SalaryChangeTests` (P6's class) is written while the images build, not compiled yet and not in the scope
    files; it is **not** part of P5's commit.

- **P5 checkpoint, 2026-11-22T19:18Z — the images are built, the first lab start, the regime numbers:**
  - Images: 15 service images (the Acceptance stack has fifteen buildable services, not seventeen: the checkpoint above
    miscounted), all labelled `f9c83a71…-dirty` (P5 is not committed), built in 1,890 s (18:20:53Z to 18:52:33Z);
    disk 12 GB free after it. `alpine:3.20.3` (the volume-init container) pulled.
  - **First lab start** 18:58:35Z to 19:12:29Z (13 min 54 s): every service healthy, then `acceptance-verify.ps1`
    failed **one** check, `SEED-03` (two seed fingerprints). **Cause, the agent's own:** the fingerprint hashes the
    embedded `testdata/acceptance/personas.json`, and the agent wrote the personas' scenarios again at about 18:25Z,
    after the first image (`admin-logistics-web`) had been built from the file without them. Fix: stack down with its
    volumes (`acceptance-reset.ps1`), that one image rebuilt (179 s), second start 19:17Z. Time lost: about 18 minutes.
    **Rule for the rest of the session:** no tracked file that an image embeds changes while images build.
  - **P-10's measure** (HR.Core's log at the first start = before; SQL after), current employment records without a
    regime / open regime exceptions of any type:
    AGENCY-A 1138 / 0, AGENCY-B 261 / 0, AGENCY-C 132 / 0 before; 0 / 0 for each after, every current record `law50`,
    and 1138 + 261 + 132 = 1531 `EmploymentRegimeTransitions` rows with the reference `ACCEPTANCE-SEED-LAW50`. The
    acceptance seed holds **no** `ServiceRegimeExceptions` row at all (the demo holds 500), so the patch resolves none
    here; its resolving path is proved by `Iam053AcceptanceRegimePatchTests`.
  - **The seed's payroll runs of agency A** (read by SQL): Regular, posted and frozen, 1102 lines each, for 2025-08 to
    2026-07; then one run a month of another kind **with no salary line**: 2026-08 Correction, 2026-09 FinalSettlement,
    2026-10 Correction, **2026-11 FinalSettlement (frozen)**, 2026-12 Correction (frozen), 2027-01 FinalSettlement,
    2027-02 Correction (frozen). So the current month has no Regular run, and P-6's reading (a run of another kind
    covers only the employees on its lines) is what lets a change of November be approved.
  - **The seed's pay profiles of agency A:** 1102 active, each with a grade and a unit; **none** has a regime code, a
    bank account, a bank name or a verification time. So AC-IAM-53a sets those four on its employee's profile as a
    stated test precondition (psql), or the copy-forward would be proved on empty values.
  - `AcIam53SalaryChangeTests` compiles (`-warnaserror`); it is adapted to the two facts above (run kinds are stored as
    text; the precondition is «no Regular run of the month, and no line of his in a run of the month»).

- **P5 checkpoint, 2026-11-22T19:44Z — the payroll regression on the lab stack:**
  - Second lab start 19:16:45Z to 19:31:19Z (14 min 34 s), verification **Pass** (64 checks; `DATA-PAYPROFILE`
    1489 = 1489, `DATA-PAYRUN` 57, `DATA-PAYJOB` 6, `DATA-PAYORDER` 72: the seed counts as before).
  - Direct run on that stack (`artifacts/plan53/run-classes.ps1`, the job's environment): **AC-23 Passed, AC-24
    Passed, AC-25 Passed**: each creates, calculates and validates a Regular run for agency A, so the regime gate is
    passed (P-10's proof). `AcHcm29HireContractTests` 3/3 Passed.
  - **AC-12 is Blocked, and not by the regime gate:** its script submits a run for review without calculating it, and
    the product answers 400 («يجب إكمال الحساب والتحقق من الإجماليات قبل الإرسال للمراجعة»). It was Blocked for the same
    cause before this plan (PLAN-41's report), and PLAN-59 owns its rewrite (its §2, G-PAY-05). The prompt assumed the
    patch alone would make it pass and says AC-12 «does not change». **Decision:** AC-12 is not changed and not marked
    required (a required class that is Blocked fails the guard); its ownership row says what was measured. AC-23,
    AC-24 and AC-25 are `required: true` in `ci-scope.json`; AC-23 and AC-25 carry `[RequiredAcceptance]`. A deviation
    for the report: «AC-12 green and required» is PLAN-59's, not delivered here.
  - `AcIam53SalaryChangeTests` tried on the used lab stack: 53b, 53c, 53d, 53e Passed; 53a stopped at its precondition,
    as designed (AC-25 had frozen the month there). Two defects of the test itself fixed on the way: amounts formatted
    with the host's culture, and a page names a user by PLAN-67's span (`data-hr-user-id`), not by text.
  - The lab job for the record (`acceptance-job.ps1 -Profile Lab -NoBuild -Classes Ac12…, Ac23…, Ac24…, Ac25…,
    AcHcm29…, RecruitmentToHireJourneyE2ETests`) started 19:43:05Z on a fresh stack; log `artifacts/plan53/p5-lab-job.log`.
  - The independent review (one fresh subagent, read-only) was started at 19:44Z, before the prod-like record run, so
    its fixes are in the images that run prod-like and go to the server.
  - PLAN-54's step 0.7 carries the line «نُفّذت في PLAN-53» with the measured numbers.

- **P5 end (2026-11-22T20:22:24Z):**
  - **The lab job for the record** (fresh stack, 19:43:05Z to 20:02:30Z; stack 907 s, lab-main 201 s, journey 14 s):
    `acceptance-guard: PASS (6 classes in 2 trx files)`. AC-23 Passed (9 s), AC-24 Passed (41 s), AC-25 Passed (108 s),
    `AcHcm29HireContractTests` and `RecruitmentToHireJourneyE2ETests` Passed; AC-12 Blocked (not required, shown only).
    The stack was brought down with its volumes after it (`acceptance-reset.ps1`): the job leaves its last stack up.
  - Build `-warnaserror` 0 errors; unit **6545/6545** (a first run was aborted at 6273 by a test host crash while the
    stack was being torn down and the machine was swapping; the rerun with `--blame` is complete and green);
    architecture **358/358**; format clean on the changed files; `iam053-validate.ps1` fails only on the two entries
    P6 adds (AC-IAM-53 in the scope and in the ownership file).
  - **The independent review** came back (read-only, about 16 minutes): no high finding; four medium and six low.
    They are fixed in P6, before the prod-like record run, and listed there.
  - Disk: 11 GB free after the stack went down (9.3 GB with it up). Chromium for AC-UX-67 was downloaded to
    `~/.cache/ms-playwright` while the lab job ran (the job's `--with-deps` step needs sudo on Linux; see P6).
- Finished (UTC): 2026-11-22T20:22:24Z — P5 committed.

## Exact next step

P6: fix the review's findings with tests (see the list under P6); add `AcIam53SalaryChangeTests` to `ci-scope.json`
(`prodLike[]`, required, group `main`) and `AC-IAM-53` to `ac-ownership.json`; rebuild the fifteen images
(`pwsh -NoProfile -File artifacts/plan53/build-images-sequential.ps1`, no tracked file changes while it runs); start the
prod-like stack (`acceptance-up.ps1 -Profile ProdLike -NoBuild`), run `AcIam53SalaryChangeTests` with
`artifacts/plan53/run-classes.ps1` until green, reset, then the job for the record with P-14's lists; the Testcontainers
step with CI's filter; commit P6.
