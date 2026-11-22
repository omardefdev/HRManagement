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

## Exact next step

P4 (P-8): `Features/ReferenceChanges/` (submit, decide, cancel; the change applied in the approval's save; validation at
submit and approval; the delete protection), `SalaryScaleChangeViewModel` and `PayGradeChangeViewModel`, the two
controllers' Create/Edit/Delete as requests, `PayrollReferenceChangesController` («طلبات البيانات المرجعية»,
`[OrgScopeNotApplicable]`) with its list and readable before/after details, `Payroll:MasterData:RequireApproval`
(false honoured in Development only, with a warning at start), the entity-binding rows of PayGrades and SalaryScales
removed (ceiling 15 → 11), the exceptions report test for a profile outside new bounds, `Iam053ReferenceChangeTests`,
the reference writers guard, the new actions in `PayrollWriteActionsTests`, the section in `PAYROLL_SALARY_CHANGE.md`.
