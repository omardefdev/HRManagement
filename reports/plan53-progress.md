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

## P6

- Started (UTC): 2026-11-22T20:22:24Z (P5's commit `32717c1d`).
- **The independent review's findings and what was done** (one fresh subagent, read-only; no high finding, so no refuter):
  1. *Medium — a person's request applied over a profile a consumer changed since.* **Fixed:** at approval and at
     activation the active profile must still be the one the request was written against (version, salary, grade,
     unit); otherwise 409 «تغيّر ملف الأجر الفعّال…», the request as it was (a scheduled one keeps `LastApplyError`).
  2. *Medium — a Regular run calculated and not frozen does not block.* **Not changed, by decision:** it is P-6's letter
     (the owner's rule; recalculation before the freeze is allowed and reads the new salary), and the gap is in the
     run's validation, which this plan must not change. **Mitigated:** the approval answers with a warning (API
     `warning`, the page's alert). **Recorded** as a known limit for PLAN-56 in `PAYROLL_SALARY_CHANGE.md` §13.
  3. *Medium — AC-23 would expire on 2027-03-01* (fixed February 2027, and a request cannot name a past month).
     **Fixed:** its two months follow the service's clock, 13 and 14 months ahead. A date limit that remains, recorded
     for the report: AC-25 uses the current month, AC-24 a fixed April 2027 and AC-12 a fixed June 2027 (neither may
     change here), so AC-25 meets a live run of its month in April and June 2027.
  4. *Medium — a second change with the same start day leaves an inverted period.* **Decided and tested:** allowed (a
     correction in the same month must be possible); the replaced version «held no day», ends the day before it
     began, and no day is in two versions. Closing never extends a period.
  5. *Low — the activation took 500 a cycle, refused ones first.* **Fixed:** every due request once a cycle.
  6. *Low — counts on a grade's page were the agency's for a branch user.* **Fixed:** counted in the caller's scope.
  7. *Low — a planned end of the active profile was lost.* **Fixed:** carried when it lies on or after the new start.
  8. *Low — narrowed items:* AC-12 (see P5); AC-23's old last assertion (kept as recorded); `Pay023` now applies the
     automatic change **inside** the frozen month on PostgreSQL; the validate script's header corrected.
  9. *Low — the decimal binder read «7500,50» as 750050 under `en`.* **Fixed:** no thousands separator in any culture.
  10. *Low — a grade-only change of a profile paid its grade's base was checked with zero.* **Fixed.**
  Tests: `Iam053ReviewFixTests` (17 cases), the binder test, `Iam053PayrollScopeHttpTests` (the counts), `Pay023`.
  Unit **6562/6562**, architecture **358/358**, the PLAN-53 integration tests on PostgreSQL **22/22**, build
  `-warnaserror` clean, `iam053-validate.ps1` and its `-SelfTest` (20 planted defects) pass.
- `AcIam53SalaryChangeTests` is in `ci-scope.json` (`prodLike[]`, required, `main`) and `AC-IAM-53` in
  `ac-ownership.json`. Committed with the fixes (2026-11-22T20:45:34Z); the images are rebuilt from that commit.

- **P6 checkpoint, 2026-11-22T21:50:13Z — AC-IAM-53a..e on the prod-like stack:**
  - Images rebuilt from `9baa4d09` (clean tree): 15 images, 1,416 s (20:45:42Z to 21:09:34Z), every label the commit's
    SHA; this plan's 15 superseded images removed by id (nothing else pruned).
  - Prod-like stack, first start 21:09:54Z to 21:24:12Z (14 min 18 s), verification Pass. First run of the class:
    53b, 53c, 53d, 53e Passed; 53a's product steps all answered as designed (request 302 to its page, own approval 403
    on the screen and the API, approval applied, run created, 1102 lines calculated, validated, frozen) and the test
    itself failed on one of its own SQL reads (`ComponentType` is stored as text). Read by SQL after it: the new
    version 636000.00 from 2026-11-01 with the bank account and the regime carried, the previous one closed
    2026-10-31, the history row, the snapshot naming the new version, the line's base component 636000.00, the outbox
    empty. Fixed in the test; a fresh stack was needed (53a freezes the month).
  - **The prod-like job for the record** (fresh stack; 21:26:43Z to 21:47:45Z; stack 868 s, journey 38 s, main 291 s):
    `AcIam53SalaryChangeTests` **5/5**, `AcIam50ApproverReachDelegationTests` 5/5, `AcIam51ExplicitPolicyTests` 7/7,
    `AcIam52BranchScopeTests` 5/5, `AcHcm25ProductionApprovalTests` 8/8, `RecruitmentToHireJourneyProdLikeE2ETests` 1/1.
    The national-scope and legal-control guards did not refuse 53a's run on the prod-like stack.
  - **The job's guard said FAIL (3), and none of the three is a test of this run:** AC-23, AC-24 and AC-25 are now
    required, a narrowed run copies in the committed evidence of the classes it did not run, and the committed
    evidence of those three is still the «Fail» of CI run 42287724602 (before this plan). The lab job that ran them
    passed its guard (P5). In CI's full run the lab stage rewrites them. **For the report:** until the next
    evidence-of-record commit from a CI run (D-QA-20: never from a local run), a narrowed local run must include
    AC-23, AC-24 and AC-25 or read the class table instead of the verdict.
  - AC-UX-67 (browser): the job's Chromium step needs `--with-deps` (sudo) on Linux, so the class is run with the
    helper against the stack the job left up. Three payroll pages were added to its key pages (the salary change
    list, the reference change list, a grade's details). First try: Chromium's headless shell had not finished
    downloading («Download failure»); it is being downloaded again.
  - `AcIam53VpsChecks` (P8's server checks) is written and compiles.
  - Docs written while the stacks started: the status lines of G-IAM-02, the payroll part of G-IAM-08 and G-PAY-10,
    `SOD_MATRIX.md`, `PAY_001_APPROVAL_BYPASS.md`, `PRV_001_DPIA_PAYROLL.md`, `CI_AND_TESTING.md`.

- **P6 checkpoint, 2026-11-22T22:34:49Z:**
  - AC-UX-67 (browser, with the three payroll pages added to its key pages): **7/7** on the prod-like stack the job
    left up (the helper, Chromium from `~/.cache/ms-playwright`). Stack down with its volumes after it.
  - **The Testcontainers step on the host, CI's filter** (`Category=Testcontainers&Category!=ObjectStorage`;
    21:59:08Z to 22:12:03Z): **248 passed, 1 skipped, 0 failed** (12 min 48 s).
  - **Incident, the agent's own: the disk filled (0 MB) at about 22:33Z.** The migrator image for the leastpriv stage
    (`hr-migrate:local`, fifteen self-contained bundles; 5.3 GB as an image, more while building) was built with
    8.4 GB free; the watch loop stopped at 860 MB, and the agent's kill command ended its own shell first (its pattern
    matched itself), so the build ran on until the disk was full. Freed at once by pruning the build cache (the plan's
    own builds; 8.2 GB in the end, the NuGet cache mount included: the images are all built). 9.5 GB free after it.
    **Checked after it:** `git fsck` clean, the working tree as it was, the image was written whole, the owner's dev
    containers and the other projects' containers are up and healthy (`hrm-keycloak` was restarting before the session
    and still is). No file of the repository was being written at that moment. **Rule for the rest:** no image build
    under 12 GB free; a process is stopped by its id, never by a pattern.
  - The link is faster now (750 KB/s to nuget.org, 530 KB/s to the Ubuntu archive, measured 22:00Z).

- Finished (UTC): 2026-11-22T22:41:56Z — P6 committed (AC-IAM-53a..e pass on the prod-like stack; the review's fixes; the scope and
  the ownership file). **Still running or to run after this commit, recorded under P8 as they end** (regression
  only; a product defect found there is fixed before the deploy): the prod-like `isolated` stage (started 22:34:49Z),
  the `s2s42` stage, the lab classes again (AC-23's months changed; the review's fixes touch the payroll paths), and
  the leastpriv stage (the migrator image exists; the disk decides).

## P8

- Started (UTC): 2026-11-22T22:42:29Z (the first server command; the local regression of P6 still runs beside it).
- **SSH and P-17's condition (22:42Z):** `ssh hrm-vps` → `hrm`; the host key presented is
  `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto` (as recorded). Running tag `1ee455bf1a6f` (previous
  `92a69960c826`), which `origin/main` contains. 22 containers, 37 GB free (the 30 GB line is met), up 10 h 41 min.
- **Server change 1 — the backup:** `/opt/hrm/backups/pre-plan53-20261122T224310Z` (700; `pg_dumpall.sql` 33,277,187
  bytes, `env.backup`, `SHA256SUMS`, each 600). Verified: both sums OK; 17 `CREATE DATABASE` lines (the 16 databases
  and `hrm_admin`); the dump ends with «database cluster dump complete». **Undo:** delete the folder when it is no
  longer needed (`rm -rf /opt/hrm/backups/pre-plan53-20261122T224310Z`).
- **The picture before the deploy (read-only, 22:43Z; counts and checksums, no amount):** active pay profiles 425 of
  425, checksum of (employee, salary) `3b2e10719627b05f17069bfca7a784f2` (the same as P0's); 10 grades (md5 of their
  rows `d964b59013d8ce5845a3dc816ff67c92`); 1 scale; 6 runs; last payroll migration `MSG040_InboxStateKeys`.
- The scanner image the deploy bundle needs (`zricethezav/gitleaks` at its pinned digest) was pulled to this machine.
- `AcIam53VpsChecks` follows the prompt's list: the three refused posts as the approver, the request through the API
  with the marker as its `SourceReference` and Arabic reasons and references, the outbox sequence, Damascus's next
  month, a reject with a note, the delete refused, nothing left open, the checksum compared.
- The documents of P9 are committed (`79d523f8`); the server build will be of that commit, started when the local
  `isolated` stage is green (one build, on the final code).

- **P8 checkpoint, 2026-11-22T23:17:20Z:**
  - **The prod-like `isolated` stage (local, fresh stack, 22:34:49Z to 23:04:02Z; stack 922 s):**
    `AcMsg40AtomicPublishingTests` 8/8, `AcWf44InstanceIntegrityTests` 5/5, `AcWf45AssetApprovalLoopTests` 8/8,
    `AcWf46CrossServiceApprovalTests` 9/9, `AcPol47RuleLifecycleTests` 1/1, `AcOps39KeycloakDataProtectionTests` 8/8,
    `AcInt37ProductionWiringTests` 7/8. The one that did not pass, `Ac37b`, was **Blocked by the way the stage was
    run alone:** it needs the prod-like journey's appointment on the same stack («run Category=JourneyProdLike
    first»), as CI's `prodlike` job runs them (journey, main, isolated, s2s42). It is run again with the journey before
    it, with the `s2s42` stage after it (started 23:04Z; log `artifacts/plan53/p6-int37-s2s-job.log`).
  - **The server build, tag `0640c2ac6db0`** (HEAD: the P6 commit's product code plus documents). `build-on-host.ps1`
    uploaded the archive (31.0 MB, 139 s) and, as the script is written, started the build **attached** to its ssh
    session (23:06Z). The prompt wants it detached, so at 23:16Z the agent stopped that run by its process ids (local
    ssh client, then the server's script and bake processes; the script's cleanup removed its source folder) and
    started the same script **detached** on the server (`setsid nohup`, start 23:16:53Z, log
    `/opt/hrm/build-0640c2ac6db0.log`, exit code in `/opt/hrm/build-0640c2ac6db0.done`). One build of the final code;
    the layers the first ten minutes built are in the server's build cache. Server disk: 37 GB free before, 27 GB at
    the restart (the cache; the script removes it at its end).
  - **Server change 2 — the build's files:** `/opt/hrm/build-on-host-0640c2ac6db0.sh` (kept, as for every earlier
    tag), `/opt/hrm/src-0640c2ac6db0.tar.gz` (the upload; removed after the build), `build-0640c2ac6db0.start|log|done`.
    **Undo:** `rm /opt/hrm/build-0640c2ac6db0.* /opt/hrm/src-0640c2ac6db0.tar.gz`; the images go to the loopback
    registry under the tag `0640c2ac6db0` and change nothing until a deploy names them.

- **P8 checkpoint, 2026-11-23T00:06:59Z — deployed; this plan's server checks pass:**
  - **Local regression, the rest:** the prod-like journey 1/1 and `AcInt37ProductionWiringTests` **8/8** with the
    journey before it; `AcS2s42TrustBoundaryTests` 8/9 — `Ac42i` needs `kubectl`, which this machine does not have
    (a local limit; CI's runner has it). The lab job again after the review's fixes (23:28Z to 23:47:19Z; stack 887 s):
    guard **PASS**, AC-23 (months by the clock), AC-24, AC-25 Passed, `AcHcm29` 3/3, AC-12 Blocked (not required).
    **The leastpriv stage** (23:49Z to 23:57:43Z; stack 185 s, tests 341 s): guard **PASS**, `AcOps38FirstBootTests`
    and `AcTen48LeastPrivilegeTests` — PAY053 runs from empty volumes under the least-privilege roles. The migrator
    image and the stage's tags were removed after it (14 GB free).
  - **The server build:** started detached 23:16:53Z; build 1,542 s, push 44 s, **17 of 17** images with digests in
    the loopback registry; the script removed the source, the build cache and the SDK image; 29 GB free after it.
  - **The deploy.** Attempt 1 (23:48Z) stopped on this machine before it touched the server: `make-deploy-bundle.ps1`
    called `tar.exe`, which only Windows has. Fixed in both deploy scripts (`tar` when not on Windows; ASCII, 5.1
    syntax). Attempt 2: 23:49:30Z to 23:54:06Z (4 min 36 s with the smoke): pull 18 s, up 149 s, bootstrap 63 s
    (PAY053 applied by the migrator), keycloak-reconcile exit 0; `deployments.log`:
    `2026-11-22T23:53:13Z|tag=0640c2ac6db0|action=deploy|result=healthy|previous=1ee455bf1a6f`. The services were
    away for about three minutes at most. 22 containers (20 with a health check, all healthy). Smoke: **50 pass,
    0 fail, 1 warn** (`artifacts/vps/smoke-hrm-vps-20261122T235406Z.json`).
  - **Server change 3 — the release:** current-tag `0640c2ac6db0`, previous-tag `1ee455bf1a6f`; migration
    `20261122153208_PAY053_SalaryChangeRequests` in `payroll_db`. **Undo:** `scripts/vps/deploy.ps1 -Rollback` (the
    rollback note is in `OPS_VPS_SINGLE_HOST.md`).
  - **`AcIam53VpsChecks` (evidence under `artifacts/plan53/vps/`):**
    1. the policies — PAY053 recorded, row security on both tables, as `e2e-payroll-approver` the three posts answer
       403, the processor's approvals 403, no row written: **Passed**;
    2. the reference change — requested by the processor, pending with the grade as it was, his own approval 403,
       rejected by the approver with a note; the delete of a grade in use 409 with its message; the grade's row as it
       was: **Passed**;
    3. the salary change — first run **failed on the test's own expectation** (the source is stored as a number, the
       test expected its name) after the request had been accepted; its `finally` cancelled the request. Corrected and
       run again: request through the API with the marker as `SourceReference` (201), pending, the processor's own
       approval 403, the approver's approval → `Scheduled`, the outbox sequence advanced, the employee's profiles as
       they were, cancelled → `Cancelled`: **Passed**.
  - **Server change 4 — what the checks left:** two salary change requests `Cancelled` (both for 2026-12-01, source
    reference `PLAN-53 server check`; the first from the failed run) and one reference change request `Rejected`, with
    their audit rows. **No undo:** requests are records and are not deleted; none is open. After the checks: active
    pay profiles 425 of 425, checksum `3b2e10719627b05f17069bfca7a784f2`, grades' md5
    `d964b59013d8ce5845a3dc816ff67c92` — both as before the deploy.
  - The server suites of PLAN-36..67 started 00:06:36Z (nothing heavy beside them).

- **P8 checkpoint, 2026-11-23T01:11:05Z — the server suites, and a defect the server's browser check found:**
  - **The server suites of PLAN-36..67** (00:06:36Z to 00:51:28Z, 44 min 52 s; `AcOps36VpsBootTests` without 36j,
    `AcInt37VpsServerTests`, the server checks of PLAN-38..67): **50 of 55** in the run, then **53 of 55** after the
    reruns. The five, each with its cause:
    1. `AC_WF_44_vps_the_job_runs…` — failed once **because of the agent:** it switched on ssh connection reuse for
       the server alias while the suite ran (each ssh command took 3.5 s over this link; with reuse 0.9 s), and ssh's
       «ControlSocket … already exists» line entered a command's output. Run again alone: **Passed**.
    2. `AC_WF_46_vps_the_sweeps_run…` — no sweep cycle yet after the deploy, as in every earlier plan. Run again after
       a cycle: **Passed**.
    3. `AC_UX_67_vps_the_portal_is_styled…` — a 30 s page load timeout over this link. Run again: **Passed**.
    4. `AC_POL_47_vps_the_migration_the_trigger…` — **not passed, a local cause:** it reads
       `artifacts/plan47/vps/fingerprint-before.json`, an uncommitted file of the formatted Windows machine. Every
       assertion before that line passed (the migration, the triggers, the roles, the change sets). The comparison it
       makes (policy versions unchanged since before PLAN-47's deploy) cannot be made from this machine; PLAN-53
       touches no policy data.
    5. `AC_UX_67_vps_every_key_page…` — **not passed on this machine:** `net::ERR_NETWORK_CHANGED` twice, on a
       different page each time. The owner's dev container `hrm-keycloak` restarts about every minute on this
       machine, and each restart changes a network interface under Chromium during the five-minute tour. The
       container is not the agent's to stop. The same tour passes on the local prod-like stack (loopback).
  - **ssh connection reuse (this session only):** `ControlMaster auto`, `ControlPath`, `ControlPersist 10m` under
    `Host hrm-vps` in `~/.ssh/config`; the host key check is unchanged. Backup `~/.ssh/config.plan53.bak`. **Undo, done
    at the end of the session:** restore the backup.
  - **A fourth PLAN-53 server check, `AC_IAM_53_vps_the_salary_and_reference_change_screens…`** (P8's item 5): eight
    payroll screens in a real Chromium as `e2e-payroll-processor`, a short tour that is run again when the machine's
    network changes under it. **It found a real defect:** the four screens with a form (the request's details, the
    request form, the reference request's details, the grade's edit form) raised one `style-src-attr` violation each.
    Cause: an empty `asp-validation-summary="All"` writes `<li style="display:none"></li>`. PLAN-67's static guard
    reads the views, not what the tag helper writes, and its key pages held no form of this kind.
  - **Fix (product code, so a second server build is needed — the «one build» rule is broken for a defect the server
    check exists to find):** the twelve payroll forms write their errors through `Views/Shared/_FormErrors.cshtml`,
    which renders the summary only when the model state has errors. `Iam053PayrollScopeHttpTests` asserts the
    untouched form carries no `style=` attribute; AC-UX-67's key pages gain the grade's edit form, so CI and the
    server check both open a form from now on. Architecture 358/358, the PLAN-53 integration tests 22/22, the
    interface guard tests and `ux067-validate.ps1` pass. **Left for a UX plan:** the same summary in four HR.Contracts
    views and one HR.Core view (not key pages).

- **P8 end (2026-11-23T01:59:43Z):**
  - **Local, with the fixed payroll image** (prod-like stack 01:14Z to 01:30:11Z): `AcIam53SalaryChangeTests` 5/5 and
    `AcUx67ReadableInterfaceTests` 7/7 (its key pages now open the grade's edit form). Stack down with its volumes.
  - **Server change 5 — room for the second build:** the local images of the releases `d0e446fe3280` and
    `92a69960c826` were removed (34 tags; 28 GB → 40 GB free). They stay in the loopback registry. **Undo:**
    `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>`.
  - **The second build, tag `27cbf35db9cc`,** detached from its start (upload 31.0 MB in 22 s; start 01:12:37Z; build
    2,035 s, push 34 s, 17 of 17 images; 32 GB free after it).
  - **The second deploy:** 01:47:39Z to 01:51:27Z (3 min 48 s with the smoke): pull 13 s, up 138 s, bootstrap 60 s
    (no migration to apply), reconcile 13 s, healthy 15 s; `deployments.log`:
    `2026-11-23T01:50:41Z|tag=27cbf35db9cc|action=deploy|result=healthy|previous=0640c2ac6db0`. Smoke 50 pass, 0 fail,
    1 warn (`artifacts/vps/smoke-hrm-vps-20261123T015127Z.json`). **Server change 6:** current-tag `27cbf35db9cc`,
    previous-tag `0640c2ac6db0`. **Undo:** `scripts/vps/deploy.ps1 -Rollback` (to the first PLAN-53 tag) or
    `-Tag 1ee455bf1a6f` (to before the plan; its images are still on the server).
  - **On the final tag (01:51:45Z to 01:57:47Z):** the four `AC_IAM_53_vps` checks **Passed** — the screens check on
    its second attempt (one network change on this machine), eight screens, HTTP 200, one policy and **no violation**
    each, Arabic, right-to-left, the requester by the registry's span and no user id printed — with `AC_OPS_36b`
    (sign-in) and the portal check.
  - **The demo at the end:** 22 containers, 20 of 20 health checks healthy, 32 GB free; active pay profiles 425 of
    425, checksum `3b2e10719627b05f17069bfca7a784f2`, the grades' md5 `d964b59013d8ce5845a3dc816ff67c92`, 6 runs, no
    history row — all as before the deploy. Requests left by the checks: 3 salary requests `Cancelled`, 2 reference
    requests `Rejected`; none open.
  - `~/.ssh/config` restored from its backup (no connection reuse; the shared connection closed).
  - The server suites rewrote 37 files under the committed `acceptance-evidence/*/vps/`: restored with
    `git checkout`, their new files moved under `artifacts/plan53/vps/` (nothing of a local run is committed).
  - `docs/OPS_VPS_SINGLE_HOST.md` has the plan's section, the change table and the rollback note; the plan's status
    and the final report are written.
- Finished (UTC): 2026-11-23T01:59:43Z — P8 committed.

## P9

- Started (UTC): 2026-11-23T01:59:43Z.

- **P7:** `origin/main` is `4ce01f59`, as at the start: nothing to merge (checked again at 2026-11-23T03:05:41Z).
- **Local gates on `2dc1c9f9` (the report's commit):**
  - **`scripts/ci-local.ps1` could not be used on this machine,** and this is a deviation: its image needs the pinned
    `mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a…` (880 MB from the registry that gave 14 KB/s), and its «Verify
    Docker builds» step builds every image at once, which this disk cannot hold (it filled once tonight). The same step
    list (`scripts/ci/steps.json`) was run **natively with `scripts/ci/run-steps.sh --all`, in a fresh clone of HEAD**
    (as ci-local's container does), 02:34:29Z to 03:02:54Z (28 min 25 s): **87 steps passed, 0 failed**, 5 not-run and
    said so — `Integration tests (Testcontainers)` (run natively in P6: 248 passed, 1 skipped; the code it tests changed
    after it only in the payroll views and one test, and `Iam053*` ran again: 22/22), `Coverage gate`,
    `Install ReportGenerator` and `Generate coverage report` (they follow the Testcontainers step and would install a
    global tool on the owner's machine), `Verify Docker builds` (the fifteen service images and the migrator were built
    on this machine one at a time, and seventeen on the server, twice). In that run: unit tests 506 s, architecture
    5.8 s, integration (MassTransit InMemory) 371 s, formatting, the Release build with `-warnaserror`, the pending
    model check for the fifteen contexts, every validate script.
  - Three false starts of that run, each the agent's or the machine's: the base-image-pin check read this plan's own
    helper Dockerfiles under `artifacts/` (moved out of the tree); the agent deleted Debug outputs to free the disk
    while a step was building with them; and stale MSBuild and compiler servers (alive since the disk filled) gave
    Razor parse errors on files that had not changed, in the tree and in a clean clone, until
    `dotnet build-server shutdown`.
  - **`scripts/plan035-prepush.ps1`:** gitleaks PASS, actionlint PASS, cilocal PASS (the five not-run steps allowed
    by name), forbidden PASS, nonprod PASS, signatures PASS (15 commits, unsigned, not required).
- This plan's local images (`hrm-acceptance-*`, `hrm-local-dotnet-*`, `hrm-plan53-*`) and the build cache were removed
  when the disk fell to 5 GB during the Release build; 18 GB free now. Other projects' images, containers and volumes
  were never touched.
- `release/plan-53` = `origin/main` + one `--no-ff` merge of the feature branch; `reports/plan53-pr.md` written.

## The pull request, the fix CodeQL asked for, and the merge

- **PR #53** <https://github.com/omardefdev/HRManagement/pull/53>: `release/plan-53` pushed 03:05:47Z, opened
  03:05:59Z.
- **Waiting for CI:** this session runs in the Claude desktop app, which forbids the agent to poll CI and watches the
  PR itself («Auto-fix», switched on for #53). The agent ended its turn at about 03:12Z with the checks running (a
  deviation from the prompt's «wait in the turn») and was woken at 03:27Z by the app's event: **CodeQL failed.**
- **Run 1** (<https://github.com/omardefdev/HRManagement/actions/runs/45103935080>, head `a583a739`): **CodeQL
  failed**, rightly — five new `cs/web/missing-token-validation` findings (security-severity 8.8) on the five POST
  actions of `PayProfileChangesApiController`. The new API is reached with a gateway session (a cookie) as well as
  with a bearer token, so a forged cross-site POST could have reached an approval (`SameSite=Lax` on the gateway's
  cookie stops the plain cross-site case only). No local gate runs CodeQL, and the independent review missed it.
- **The fix, on the release branch (`5aa61b78`):** `GET api/payroll/pay-profile-changes/antiforgery` and
  `[ValidateAntiForgeryToken]` on the five writes — the pattern of PLAN-40, PLAN-43 and PLAN-47. **No baseline entry,
  no ignore attribute.** `Iam053PayrollScopeHttpTests` gained a test (each write answers 400 without the token or
  with another user's, and nothing is written); the acceptance classes (AC-IAM-53, AC-23, AC-25) and the server check
  use a client that carries the token (`AcceptanceHarness.CreatePayrollClientAsync`); the contract document and the
  final report say so. Before the push: unit 6562/6562, architecture 358/358, the PLAN-53 integration tests 22/22,
  format clean. Pushed 03:48:14Z (that branch only).
- **Run 2** (<https://github.com/omardefdev/HRManagement/actions/runs/45108118556>, head `5aa61b78`, 03:48:21Z to
  04:36:01Z, 47 min 40 s): **all 15 checks passed** — Acceptance, Acceptance images, Acceptance stage (lab-main,
  lab-rest, leastpriv, meta, prodlike, prodlike-final, prodlike-policy), CI, CodeQL, Dedupe post-merge run, E2E,
  Gitleaks, Reuse check. **The evidence of record** is that run's artifact `acceptance-evidence` (and
  `acceptance-stage-*`, `test-results`, `codeql-sarif`); nothing of a local run is committed.
- **Merged by agent on owner instruction (UTC): 2026-11-23T04:37:43Z** — `gh pr merge 53 --merge` (no auto-merge), with
  every check green at the pushed head and `main` unchanged since the start. Merge commit **`53690e0e0900`**.
  `git switch main; git pull --ff-only`: `main` = `origin/main` = `53690e0e`.
- **The post-merge run** <https://github.com/omardefdev/HRManagement/actions/runs/45112995268> (push to `main`,
  04:37:45Z): **completed, success** — Gitleaks and «Dedupe post-merge run» passed, and the other jobs were skipped
  by design (the run reuses the PR's green run of the same tree).
- **CI wait:** 03:06:01Z to 04:36:01Z = **1 h 30 min** for the two runs (42 min of it the run that failed and was
  replaced). **Actions minutes** (sum of the jobs' durations): run 1 about 202, run 2 about 204, **about 406**, plus the
  post-merge run.

## The server after the fix (the deployed tag is the merged code)

- **Third build, tag `5aa61b78e3a6`,** detached (upload 31.0 MB in 56 s; start 03:49:40Z; build 1,819 s, push 31 s,
  17 of 17 images). **The server's disk fell to 5.4 GB at the build's lowest point** (32 GB before it; three
  releases' local images were on the disk with the build cache); the agent watched it with a stop line at 3 GB, and
  the script's cleanup brought it back to 24 GB. For the next plan: remove the local images of older releases
  **before** the build even when 30 GB are free.
- **Third deploy:** 04:21:20Z to 04:25:11Z (3 min 51 s): pull 3 s, up 126 s, bootstrap 55 s, reconcile 13 s, healthy
  14 s; `2026-11-23T04:24:23Z|tag=5aa61b78e3a6|action=deploy|result=healthy|previous=27cbf35db9cc`; smoke 50 pass,
  0 fail, 1 warn (`artifacts/vps/smoke-hrm-vps-20261123T042511Z.json`). **Server change 7:** current-tag
  `5aa61b78e3a6`, previous-tag `27cbf35db9cc`. **Undo:** `scripts/vps/deploy.ps1 -Rollback` (to the tag without the
  token), or `-Tag 1ee455bf1a6f` (to before the plan).
- **On the final tag (04:26Z to 04:36:52Z):** the four `AC_IAM_53_vps` checks and the sign-in check **Passed**. The
  salary change check now also proves, through the gateway session, that a write without the token answers 400 and
  with it 201.
- **Server change 8:** the local images of the first PLAN-53 tag `0640c2ac6db0` removed (17 tags; they stay in the
  loopback registry; 24 GB → 30 GB free). **Undo:** `docker pull 127.0.0.1:5050/hrmanagement/<image>:0640c2ac6db0`.
- **The demo at the end:** tag `5aa61b78e3a6`, 22 containers, 20 of 20 health checks healthy, 30 GB free; active pay
  profiles 425 of 425 with the checksum `3b2e10719627b05f17069bfca7a784f2` and the grades' md5
  `d964b59013d8ce5845a3dc816ff67c92`, both as before the plan; 6 runs. Requests left by the checks' four runs:
  **4 salary requests `Cancelled`, 3 reference requests `Rejected`**, none open.

## Timing, final

- Started 2026-11-21T00:15:16Z; merged 2026-11-23T04:37:43Z: **52 h 22 min** wall-clock.
- Stopped: 36 h 15 min 55 s (the preconditions) + 2 h 24 min 49 s (the owner's stop and the restart) + about 15 min
  between the end of the turn and the app's wake-up = **about 38 h 56 min**.
- Working: **about 13 h 26 min**, of which waiting for CI 1 h 30 min (overlapped with the third server build, deploy
  and checks).
- The keep-awake process was stopped after the merge (`pkill -f 'systemd-inhibit --what=sleep:idle --who=plan53'`).

## Exact next step

None: PLAN-53 is merged (`53690e0e0900`) and deployed (`5aa61b78e3a6`). This note lives on the local branch
`docs/plan-53-merge-note`; the next plan brings it in. No second PR for it.
