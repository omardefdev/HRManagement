Started (UTC): 2026-11-25T20:19:57Z

# PLAN-57 — progress (the agent's checkpoint)

Main folder: `/home/omar/HRManagement` (the owner's Linux notebook). Branch
`feature/plan-57-lva-attendance-period-payroll-handoff` from `origin/main` = `01a8c3f9`. One session, phases P0–P10.

**Owner's word in this session (first message, 2026-11-25T20:19Z):** «أسمح بتقليص سجلات Seq على الخادم إلى آخر 30 يوماً» — Seq's logs on
the server may be trimmed to the last 30 days. Applied in P9 only, through Seq's own retention setting (P-16).

## Phase status

| Phase | Status | Finished (UTC) |
|---|---|---|
| P0 baseline | done | 2026-11-25T20:49:10Z |
| P1 model, calendar, summary, migration | done | 2026-11-25T21:06:33Z |
| P2 one writer | done | 2026-11-25T21:25:58Z |
| P3 close and contract | in progress | |
| P4 correction | | |
| P5 payroll's side | | |
| P6 PostgreSQL proofs | | |
| P7 acceptance and review | | |
| P8 main | | |
| P9 demo server | | |
| P10 docs, gates, report, merge | | |

## P0 — baseline (finished 2026-11-25T20:49:10Z)

- **Keep-awake:** `systemd-inhibit` PID in `artifacts/plan57/keepawake.pid` (1314307), started 20:20Z; stop it by that PID at the end.
- **Preconditions:** no other plan session; `origin/main` holds PLAN-56 (`01a8c3f9`), PLAN-52 (`aa8be851`), PLAN-37 (`d1642d41`);
  the tree was clean; `ssh hrm-vps` answers `hrm` (port 22 open); `gh` active account `omardefdev` with `repo` and `workflow`.
- **First commits:** PLAN-56's merge note cherry-picked from `docs/plan-56-merge-note` (`2691b459`; the local branch deleted after
  checking that its tree equals the cherry-pick); the prompt file written from the session's own transcript (`f49f04de`).
- **Baseline:** `reports/plan57-baseline.md` (dependencies, build 4 min 25 s, no pending model change, unit 419 passed, integration
  32 passed / 13 skipped, the data inventory on a fresh local stack and on the demo, the code inventories, the demo's picture).
- **Reserved months: January–July 2025** (agency A). Proved on a fresh local prod-like stack: the run of 2025-01 was created,
  calculated (1,102 lines, 9 s) and cancelled through PLAN-56's API. Stack start 14 min 38 s (20:29:58Z → 20:44:36Z), first try;
  down with its volumes at 20:47:14Z (two volumes the up script creates outside the compose file were removed by name).
- **The old event's queue on the demo is empty** (0 messages, 1 consumer, no error queue): the old consumer is deleted (P-8).
- **Findings that change the prompt's picture** (recorded as deviations for the report):
  1. The acceptance seed holds **no attendance record**, and the demo holds two months (2026-09, 2026-10), none on a rest day and
     none made by a leave: the migration's fill changes no demo record.
  2. `EmploymentStatus` has more ends of service than `Terminated` (Retired, Resigned, Deceased, Merged, Anonymized) and a
     not-yet-started status (`PendingActivation`). The plan's rule for `Terminated` is applied to every status that ends the
     service (included when the end is on or after the period's first day, working days up to that day); `PendingActivation` has no
     attendance. Otherwise a retired employee would need attendance for ever under `RequireComplete`.
  3. A system correction from a leave is made **per event and period, not per day**: every locked day of the leave gets its
     correction row, and the summary takes **one** new version for them all (one event to payroll).
  4. A correction's `NewStatus` is nullable: a cancelled leave on a locked day that had no record before the leave removes the record.
- **Timing:** inventories about 6 min; baseline tests about 1 min 20 s; stack start 14 min 38 s; probe 2 min (a build and one test).
  No time stood still.

## P1 — the model, the calendar and the summary (finished 2026-11-25T21:06:33Z)

- **Commit `6f887768`.** HR.Core domain: `AttendancePeriod`, `AttendancePeriodClosure`, `AttendanceEmployeeSummary` (`xmin`),
  `AttendanceCorrection`; enums `AttendancePeriodStatus`, `AttendanceRecordSource`, `AttendanceCorrectionStatus` (with the
  correction's source, the summary's reason and the unrecorded-days mode); `DailyAttendanceRecord.Source/SourceReference/ReplacedStatus`;
  the pure `AttendanceSummaryCalculator` (and `IAttendanceWorkingDays`); `AttendanceOptions` and `AttendanceCalendar`
  (`Attendance:*` in HR.Core's three appsettings files; the holidays empty everywhere).
- **Migration `20261125205352_LVA057_AttendancePeriods`:** the four tables under forced row security (`TenantRlsMigrationHelper`),
  the three columns, the unique and filtered indexes, and the fill (`Source = 2` for leave-made `OnLeave`) with `FORCE` lifted
  around it. No byte order mark (ef wrote one on both files; stripped). `has-pending-model-changes`: none.
- **Tests:** `Lva057AttendanceSummaryCalculatorTests` 11 passed; the unit filter (Lva057, Attendance, Leave, Hcm002, Architecture)
  496 passed; `Lva057MigrationPostgreSqlTests` 2 passed on PostgreSQL as a NOSUPERUSER NOBYPASSRLS owner under forced row security
  (two agencies; the control shows the owner sees no record under FORCE); `Iam052OrgScopeMigrationPostgreSqlTests` 2 passed.
- **Format:** `dotnet format whitespace … --verify-no-changes` on the phase's files: clean. **Gitleaks** over the branch's 4 commits: no leak.
- **Build:** 0 errors (1 min 57 s). One build failed on stale Razor compiler servers (parse errors on unchanged
  `CandidatePortal` views); `dotnet build-server shutdown` cleared it, as the earlier plans recorded.
- **Deviation recorded:** a leave-made record of before this plan carries no leave reference (the migration marks it by its
  note). `RevertLeaveAsync` therefore also takes a `Source = Leave` record with no reference in the cancelled leave's range, as the
  old removal did; a manual record is never touched.

## P2 — one writer for an attendance day (finished 2026-11-25T21:25:58Z)

- **`AttendanceDayWriter`** (`HR.Core.Infrastructure/Services/Attendance/`): `CreateManualAsync`, `UpdateManualAsync`,
  `DeleteManualAsync`, `BulkFillAsync`, `ApplyLeaveAsync`, `RevertLeaveAsync`, `ApplyApprovedCorrectionAsync`; it never saves. With
  it: `AttendanceLockService` (one day, and a batch in one query), `AttendanceEligibility` (the employees of a unit's tree for a
  period: the close and the bulk entry share it), `AddHrAttendance` (the registration, with the calendar validated at start).
- **PLAN-52's scope check of the employee** lives in the org-scope filter (it resolves `EmployeeId` of the bound model, and the
  record's id). It stays. The writer adds the one explicit check (`RefuseOutOfScopeAsync`) for a create, a change, a removal, the
  bulk entry and, from P4, a correction.
- **The controller** (`DailyAttendanceRecordsController`) calls the writer; `PopulateEmployeesAsync` is gone; `Create` uses the
  employee picker; the edit view model binds `Id`, `Status`, `Notes` only and the view shows the employee and the date read-only;
  `Details` has its view; the list shows the source, «عرض», «تعديل», and «طلب تصحيح» on a locked day; the bulk entry reports what
  it wrote and skipped; no form offers «في إجازة». The five writes name `HrCore.AttendanceWrite` / `HrCore.AttendanceClose`.
- **The three policies** are in `HrManagementPolicies` and `HrManagementAuthorizationExtensions` (two through `RolesPolicy`; the
  approval excludes a branch-scoped caller).
- **The leave consumers:** the status history and the days in one save (the marker used to be saved first). The reconciler reads
  `Source = Leave` records and returns only an employee whose last move to `OnLeave` was a leave's.
- **The old close stays until P3** (service method, the two actions and the view), so that each phase's commit is coherent.
- **Tests:** `Lva057AttendanceDayWriterTests` (20), `Lva057LeaveAttendanceTests` (6), `AttendanceSingleWriterArchitectureTests`
  (3 + 17 planted cases); the unit filter 541 passed; integration (SQLite) `Iam052HrCoreScope`, `Iam051`, `HRCoreConsumer`,
  `OutboxAtomicity`, `Hcm025`: 60 passed, 2 skipped; the architecture project 414 passed (the interface guard reads the new views).
- **Existing tests changed, meaning kept:** `Iam051DirectServiceWriteTests` reads the attendance actions' own policies (same
  roles: a supervisory employee 403, hr-core passes); `IntegrationTestHelpers` registers the attendance services;
  `OutboxAtomicityIntegrationTests` builds the service with the writer (rewritten in P3).
- **Format:** the whitespace formatter rewrote multi-line object initializers of three test files; clean after it.
- **Commit:** see `git log` (the P2 commit follows `6f887768`).

## Decisions (with reasons)

- **D-P57-1, the reserved months:** 2025-01 … 2025-07 (see the baseline). 57a January, 57b February (and the current month for «no
  future day»), 57c March, 57d April and May, 57e June, 57f/57g/57h July and January's closed days.
- **D-P57-2, the old consumer:** deleted (the demo's queue is empty at P0; checked again before the deploy).
- **D-P57-3, new enums are stored as integers** in HR.Core's new tables and columns (as `DailyAttendanceRecords.Status` is).

## Server changes and their undo commands

None yet (P0 was read-only).

## Open issues

- `scripts/pol049-validate.ps1` is red on `main` (28 findings) before this plan: recorded, not chased.

## The exact next step

P3: `IAttendanceSummaryPublished` (contract); `AttendancePeriodService` (close, status, ensure period) with the summaries, the
closure and the events in one save; `AttendancePeriodsController` (MVC Index/Close + API close/get + antiforgery) and its two
views; remove `ClosePeriodAndPublishAsync`, the old actions and view; mark `IAttendancePeriodClosed` obsolete and delete payroll's
old consumer (its replacement is P5: until then payroll has no attendance consumer); rewrite the outbox test; docs inventories and
the runbook; tests of the close's employees, `RequireComplete` and its alternatives, idempotence, the period's status, 403.
