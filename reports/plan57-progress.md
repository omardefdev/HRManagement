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
| P3 close and contract | done | 2026-11-25T21:44:57Z |
| P4 correction | done | 2026-11-25T22:01:57Z |
| P5 payroll's side | done | 2026-11-25T22:22:14Z |
| P6 PostgreSQL proofs | done | 2026-11-25T23:05:16Z |
| P7 acceptance and review | in progress | |
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

## P3 — the close and the contract (finished 2026-11-25T21:44:57Z)

- **`AttendancePeriodService`**: `CloseAsync` (a month, or custom bounds behind `Mode=Custom`), `GetStatusAsync`,
  `EnsurePeriodAsync`, `PublishSummaryAsync` (`[StagesOutboxMessages]`). One save writes the period, every summary (v1), the
  closure row and the staged events; a unique violation in that save is the concurrent close's answer and writes nothing.
  `RequireComplete` refuses before anything is created (not even the period).
- **`IAttendanceSummaryPublished`** (`HRManagement.MessageContracts`): the plan's fields, `TenantId` explicit, schema v1.
  `IAttendancePeriodClosed` is `[Obsolete]` with no publisher and no reference; payroll's `AttendancePeriodClosedConsumer` is
  deleted (its replacement is P5; in between payroll consumes no attendance event).
- **`AttendancePeriodsController`** (`[OrgScopeNotApplicable]`, class `HrCore.ReadOnly`): MVC `Index`, `Close` (GET/POST), and
  the API `GET api/attendance/periods/antiforgery`, `POST api/attendance/periods/{yyyy-MM}/close`,
  `GET api/attendance/periods/{yyyy-MM}`; the POSTs name `HrCore.AttendanceClose` and validate an antiforgery token. Views
  `AttendancePeriods/Index`, `Close` (what is missing: the first 50 and the total; the warning when no holiday is configured).
  The old close is removed (service method, POST action, view); `DailyAttendanceRecords/ClosePeriod` redirects to the new page.
  The menu has «فترات الحضور» and «تصحيحات الحضور».
- **Deviations recorded:**
  1. **A period is closed only once it has ended** (`PeriodEnd <= today` in Damascus): closing a running month would lock days
     that have not happened. The plan is silent; the refusal is a 400 with «لا تُغلق فترة الحضور قبل انتهائها».
  2. A repeat close that finds everyone closed writes nothing at all (no second closure row, no event) and answers 200 with
     `closedEmployees = 0`.
  3. `CloseAsync` is not marked `[StagesOutboxMessages]`: the guard matches a marked method by its name, and
     `RecruitmentRequisitionsController` calls another `CloseAsync`. It saves itself; `PublishSummaryAsync` carries the mark.
- **Tests:** `Lva057AttendanceCloseTests` (10): the close's employees, working days only, `RequireComplete` and its two
  alternatives, idempotence, the period's status, a branch outside its tree, a period not ended, custom periods, the scoped status.
  `OutboxAtomicityIntegrationTests.CloseAttendancePeriod_SavesAfterPublishLoop` rewritten on the new service (every event staged
  before the one save; the same meaning). `Iam051DirectServiceWriteTests`: the former `ClosePeriod` row is gone with the action.
  Unit filter 794 passed; architecture project 414 passed (the interface guard needed an empty message under each looped table and
  the month through `HrMonthInputValue`); integration (SQLite) 52 passed, 2 skipped.
- **Docs:** `EVENT_PUBLISHER_INVENTORY.md` (the new event, SingleSave; the old row replaced, its end noted),
  `MESSAGE_EVENT_MATRIX.md` (the new row). The runbook is written in P10 with the final behaviour.

## P4 — the correction (finished 2026-11-25T22:01:57Z)

- **`AttendanceCorrectionService`**: `RequestAsync`, `ApproveAsync`, `RejectAsync` (each one save) and
  `ApplySystemCorrectionAsync` (no save: the leave consumer's). An approval applies the day through the writer, recomputes the
  summary by the calculator from the stored days with the change laid over them, raises the version, stages the event, writes the
  decision, in one save, retried up to four times on an `xmin` conflict of the summary.
- **Separation of duties:** the caller's subject is compared with `RequestedBy` in the service, for an approval and a rejection
  alike (the requester never decides his own request, whatever his roles). `HrCore.AttendanceCorrectionApprove` leaves the branch
  out; the two decisions also carry `[OrgScopeCentralOnly]` (PLAN-52's driver tells a policy's 403 from the scope's by a central
  control with the same roles, which this policy defeats by design).
- **`AttendanceCorrectionsController`** (`[OrgScoped(typeof(AttendanceCorrection))]`, keyed by the employee in
  `HrCoreOrgScopeKeys`): MVC `Index` (waiting / mine), `Create` from a locked day, `Details` with «اعتماد» and «رفض»; API
  `GET api/attendance/corrections/antiforgery`, `POST api/attendance/corrections`, `…/{id}/approve`, `…/{id}/reject`, every
  POST with `[ValidateAntiForgeryToken]` and its policy. Answers as P-4: 400 a broken rule and the requester's own decision, 403
  outside the scope and a branch on a decision, 404 not in the agency or scope, 409 a second pending request and a decided one.
- **Leave decisions on locked days:** `AttendanceIntegrationService` passes the writer's locked days to
  `ApplySystemCorrectionAsync`. `AutoApply`: one correction row per locked day, approved with no requester and no decider, one new
  version and one event per period, in the consumer's save; the same event again makes nothing. `Pending`: the rows wait for a
  central person; a person's pending request for the same day gives way; a leave cancelled before the decision withdraws its row.
- **Schema change inside this plan's migration** (not pushed yet): `AttendanceCorrections.RequestedByName` and `DecidedByName`
  (200), so the pages show a name and never an id (the interface guard, and what `ApprovalRequest` does). The migration file, its
  designer and the snapshot were edited by hand; `has-pending-model-changes`: none; `Lva057MigrationPostgreSqlTests` 2 passed again.
- **Deviations recorded:** a leave-made day of a closed month is not corrected by a person (it changes by cancelling the leave);
  an approval whose day already holds the requested status answers 409 («no longer applies») and is rejected with a note instead.
- **Tests:** `Lva057AttendanceCorrectionRulesTests` (13): the request's rules, the scope, one pending per day, the atomic approval
  (one save, the day, v2, the event, the decision), the requester refused, the three policies by role, a rejection's note and a
  decided request, a correction with no figure change, an injected save failure, and the leave events (approved, cancelled, a day
  the leave created, `Pending`, cancelled before the decision). `Iam052HrCoreScopeHttpTests` seeds a correction in both branches
  and exercises the new actions (3 passed). Unit (Lva057 + Architecture) 372 passed; architecture project 414 passed.

## P5 — payroll's side (finished 2026-11-25T22:22:14Z)

- **Commit `b8435a88`.** `AttendanceSummaryReceipt` (unique `SummaryId`, `xmin`); `AbsenceDeduction.OriginalPeriodStart/End`,
  `CarryForward`, `SourceVersion`; migration `20261125221004_LVA057_AttendanceHandoff` (the table under forced row security, the
  four columns, the index; no data move; no byte order mark; `has-pending-model-changes`: none).
- **`AttendanceSummaryPublishedConsumer`** and `AttendanceSummaryHandoff` (one transaction: the receipt created if absent and
  locked `FOR UPDATE` on PostgreSQL, a version not newer than the latest ignored, the difference row `ATT:{SummaryId}:v{Version}`
  with `CarryForward = HasCalculatedRunAsync`, the receipt updated, the inbox receipt staged with them). The tenant is the message's.
- **`PayrollAttendanceFreeze`**: the one reader of the run's states (a live regular run of the period: calculated = past `Draft`;
  frozen = `ResultsFrozen` or under review, approved, posted).
- **The loader:** a regular run also takes a carried row of an earlier period (the plan's expression). **Added for a race the
  plan's rule leaves open:** a free, unmarked attendance row of an earlier period is taken too when that period has a calculated
  regular run (the consumer can decide «not carried» an instant before the period's run calculates and freezes without the row;
  nothing else would ever take it). A period with no run keeps its rows for its own run.
- **`AbsenceRule`:** negative days are a refund (a deduction component with a positive amount, «استرداد خصم غياب — فترة yyyy-MM»);
  a row applied by a later period's run is «فرق غياب عن فترة yyyy-MM»; every row read is consumed. Old rows give the same amount
  and name as before.
- **`PayrollAttendanceGate`** (`Payroll:Attendance:Gate`, `Warn` in the three appsettings files and mirrored in the prod-like
  overlay): in `StartPayrollRunJobHandler`, after the interim gate and before `run.BeginCalculating()`. `Warn` writes **one open**
  audit record per run (refreshed by a later evaluation), whose findings start with «تحذير بوابة الحضور», carry the count and the
  employees' ids; `Block` refuses with the count and writes nothing (no job row either); `Off`, a correction run and a final
  settlement pass. A resumed job does not meet it again.
- **Reads and panel:** `GET api/payroll/runs/{id}/attendance-status`, `GET api/payroll/attendance/receipts?periodStart=`; the run's
  page shows «الحضور» (mode, in scope, missing by name — the first 50 —, carried applied and pending).
- **Compose:** `Attendance__*` and `Payroll__Attendance__Gate` in `docker-compose.acceptance-prodlike.yml`; the acceptance holidays
  2025-02-17 and 2025-02-18 in `docker-compose.acceptance.yml`. `scripts/vps/lint-compose.ps1`: both chains pass.
- **Tests:** `Lva057AbsenceRuleRefundTests` (7, unit); `Lva057PayrollAttendanceGateTests` (9) and
  `Lva057PayrollAttendanceHandoffTests` (27 with the state table) in the integration project on SQLite, through the real handler,
  loader, engine and run acts (the plan lists the gate test as a unit test: it needs a real calculation, so it sits beside
  `Pay056RunScope`). Unit filter (Lva057, Payroll, Pay0, Gov002, Architecture, Leave, Consumer) 1,255 passed; architecture project
  414; payroll integration filter (SQLite) 175 passed, 86 skipped (Testcontainers: P6).
- **Existing test changed, meaning kept:** `Pay056ReversalTests` counted every audit record of the run to say «a reversal writes
  no closed record by nobody»; it now counts the closed ones (one: the posting's precondition) and checks that any other is the
  open attendance warning.

## P6 — PostgreSQL proofs (finished 2026-11-25T23:05:16Z)

- **`Lva057AttendanceCloseIntegrationTests`** (5, PostgreSQL): a close is one save with its outbox rows; an injected trigger failure
  leaves no summary, no closure and no outbox row; two closes at once of one unit give one summary per employee and one event each;
  two approvals at once on one summary give versions 2 and 3 (the `xmin` retry); **2,000 employees × 31 days closed in 2.69 s**
  (limit 10 s), the lock lookup of a page in 0.10 s.
- **A defect the PostgreSQL test found, fixed:** after a concurrency failure the approval's retry saved on the same context after
  `ChangeTracker.Clear()`, and the bus outbox's state row was lost with it (`FK_OutboxMessage_OutboxState_OutboxId`). A retry now
  runs in a new DI scope (`AttendanceCorrectionService.ApproveAsync`, up to 4 attempts).
- **`Lva057PayrollAttendanceHandoffIntegrationTests`** (9, PostgreSQL): the same version twice; versions in reverse order; two
  versions at once on one receipt (`INSERT … ON CONFLICT DO NOTHING` then `SELECT … FOR UPDATE`); the carry past a frozen month; the
  refund; `Block`; another tenant's summary invisible under row security; payroll's migration applied by a non-superuser owner from
  `20261125055305_PAY056_RunLifecycle`.
- **`Lva057MigrationPostgreSqlTests`** (2): HR.Core's migration on a filled database — the leave-made fill, row security on the four
  new tables. **HTTP:** `Lva057AttendanceApiHttpTests` (8: statuses 200/400/403/404/409, antiforgery, roles, scope) and
  `Lva057PayrollAttendanceReadsHttpTests` (1).
- **The gate's scope, refined:** an employee whose first pay profile (or history row) begins after the run's period is not in the
  gate's scope — a new hire must not hold back an earlier month. Test
  `An_employee_whose_first_pay_profile_begins_after_the_period_is_not_in_the_gates_scope`; gate + handover classes 37 passed.
- **Full Testcontainers step** (CI's filter `Category=Testcontainers&Category!=ObjectStorage`): started 2026-11-25T22:42:33Z,
  finished 22:57:29Z — **320 passed, 1 skipped** (`Ops038BootstrapIdempotencyTests`, needs the migrator image), 14 min 51 s. The
  wait was used to write the acceptance class (no build ran beside it).
- Build `-warnaserror`: 0 warnings, 0 errors (4 min 33 s). Format check on the changed files: clean.
- Commits: `4adfe897` (tests), `73f397e5` (gate scope).

## P7 — acceptance and review (in progress; written as it goes)

- **The class and its pieces** (commit `cdcb541c`): `AcLva57AttendancePeriodTests` (AC-LVA-57a..h, required, ordered),
  `testdata/acceptance/lva057-periods.json`, three publishers in `AcceptanceMessagingHelper` (the leave's approval and
  cancellation, a summary version), `AcceptanceHarness.CreateAttendanceClientAsync`, `ci-scope.json` (`prodLike[]`, main),
  `ac-ownership.json` (AC-LVA-57, PLAN-57), the seven personas' scenarios, `AcIam52`'s free day (no summary of the employee covers
  it), seven attendance pages among `AcUx67`'s key pages (four optional while the stack holds no record, opened directly when it
  does), `scripts/lva057-validate.ps1` (11 checks; `-SelfTest`: 44 planted defects refused),
  `docs/runbooks/attendance-period-close.md`.
- **A read-only check of the class against the source** (one subagent, 20 min, before any stack): names, enums, form fields,
  literals and JSON shapes matched; eight assumptions about the seed did not, and were fixed before the first run — the seeded
  employees sit in directorates under the branch, not in its root; a close also covers who left later; five employees were hired
  inside January 2025; an integer cannot be cast to jsonb; a rounding to the cent; three «refused» assertions that the form's own
  hint would have satisfied (now: the text once more than on the form).
- **Images:** 16 services one at a time, 23:20:50Z → 23:43:05Z, **1,320 s**, revision `cdcb541c`; disk 32 GB before, 39 GB
  after. HR.Core and Payroll again after the review fixes: 00:21:42Z → 00:26:33Z, 280 s, revision `b92ed935`.
- **Prod-like stack 1** (`acceptance-up.ps1 -Profile ProdLike -UseLocalBaseImages -NoBuild`): 23:45:21Z → 00:00:04Z, ready on
  the first try, 14 min 43 s. `AcLva57AttendancePeriodTests` alone: 00:06:04Z → 00:09:32Z, **7 of 8** (57d: the test's own wait
  counted two inbox rows per message; fixed), then 57c and 57d again, 2 of 2. Stack down at 00:11:46Z.
  57e on that stack: 1,011 employees in the run's scope, all missing before the close; the whole-agency close wrote 1,189
  summaries (1,189 receipts); 20 still missing, none of them in service in June 2025 by HR.Core's rule.
- **Independent review** (one fresh subagent, read-only, beside the image build and the stack's start; 12 min). One high, six
  medium, seven low. All fixed with tests in `b92ed935`, except the two noted:
  - **High — the gate's warning could be closed by one person and then satisfied the posting's «closed audit record».** Now the
    posting looks for a closed record that is not the gate's, `CloseAudit` refuses the warning, the list hides its «إغلاق» and
    shows its sentence without the ids, and a recalculation with nobody missing removes it.
  - **M1 — a close and a writer could pass each other** (check, then save). Both now meet on a PostgreSQL advisory lock per
    agency and month (`AttendanceMonthLock`): the close takes it exclusively before it reads; every save of an attendance day
    takes it shared and reads the lock again (`HRCoreDbContext.SaveChangesAsync`); a correction's days are exempt. Two closes at
    once now take turns (the unique index stays as the last line).
  - **M2/M3 — a history row that notes a status over itself was read as a move:** the reconciler no longer leaves an employee on
    leave after another leave's cancellation, and the end of a service is the last row that moved the employee into an ended
    status.
  - **M4 — an approval and a rejection of one request could both win:** `xmin` on `AttendanceCorrection` (the model only; no DDL).
  - **M5 — the gate's scope.** `73f397e5` had narrowed it to «a pay profile that began by the period's end»; the run gives a line
    to every active profile, so the narrowing hid people who are paid. **Reverted to the plan's rule** (who the run gives a line
    to). Consequence for AC-LVA-57e, recorded as a deviation: the prod-like seed dates some pay profiles before their employee's
    hire date, so a whole-agency close cannot empty `missingAttendance` in any month free for the class; the scenario asserts
    that nobody HR.Core closed is missing and that whoever is left is not in service in the month by HR.Core's rule (and that
    the warning lists exactly them). The empty list with no warning record is proved by
    `Lva057PayrollAttendanceGateTests.A_run_whose_employees_are_all_closed_gets_no_warning_record`.
  - **M6 — a carried row could be marked by two runs calculating at once:** `xmin` on `AbsenceDeduction` (the model only); the
    second save is refused. The same unconditional marking of deductions and adjustments is older than this plan and stays as it
    was (a defect for PLAN-58/PAY-H: two regular runs of one agency calculating at once).
  - Low: a waiting request is not approved over a day a leave took meanwhile; `POST api/attendance/corrections` with no body or
    a broken date is 400; a double bulk submit is a message, not a 500; a repeat close reads the period's state again; 57d waits
    for the consumer's inbox rows instead of sleeping; each payroll scenario cancels its runs and deletes a freed carried row
    in a `finally`; the rollback note goes into `OPS_VPS_SINGLE_HOST.md` in P9.
  - The reviewer's unverified point (`Pending` mode's replacement of a waiting request under the filtered unique index, in one
    save) now has a PostgreSQL test, and passes.
- **After the fixes:** build `-warnaserror` 0 warnings (5 min 41 s); `has-pending-model-changes` none for both services; unit
  filter 1,565 passed; architecture 414; integration (SQLite) 231 passed, 124 skipped (Testcontainers); the `Lva057` PostgreSQL
  classes **22 of 22** (1 min 35 s); `Lva057AttendanceApiHttpTests` 8 of 8; format clean; gitleaks on the changed files: none.

- **Acceptance on this machine, on the images of `b92ed935`** (all with `-NoBuild`; UTC of 2026-11-26):

  | Step | Start | End | Result |
  |---|---|---|---|
  | Prod-like stack 2 (`acceptance-up.ps1 -Profile ProdLike -UseLocalBaseImages -NoBuild`) | 00:28:09 | 00:42:56 | ready on the first try, 14 min 47 s |
  | `AcLva57AttendancePeriodTests` alone | 00:42:59 | 00:45:43 | **8 of 8** |
  | `AcUx67ReadableInterfaceTests` by hand (Chromium is installed for the user; the job's install needs sudo) | 00:45:50 | 00:49:28 | **7 of 7**; six of the seven attendance pages opened, the edit form had no unlocked record to open (57h reset July) — 57h now leaves one (below) |
  | Acceptance job, prod-like, fresh stack: `-Profile ProdLike -NoBuild -Stage prodLike:journey,prodLike:main,prodLike:isolated -Classes <17>` | 00:52:08 | 01:31:46 | stack 885 s; journey **1 of 1**; main **54 of 55** in the order AcPay54, AcIam53, AcPay55, **AcLva57 (8 of 8)**, AcPay56, AcIam52, AcHcm25, AcIam51, AcIam50 — the one failure is AC-IAM-52a's expectation (below), so the guard said FAIL (2); isolated: AcInt37 8, AcMsg40 8, AcWf44 5, AcWf45 8, AcWf46 9, AcPol47 1, AcOps39 8, **all passed** |
  | Prod-like stack 3 | 01:33:28 | 01:47:55 | ready on the first try, 14 min 27 s |
  | `AcLva57AttendancePeriodTests` + `AcIam52BranchScopeTests` in one run | 01:47:58 | 01:51:28 | **13 of 13** (AC-IAM-52a..e first, then AC-LVA-57a..h) |
  | `AcLva57AttendancePeriodTests` **again on the same stack** + `AcUx67ReadableInterfaceTests` | 01:51:40 | 01:57:19 | **15 of 15**: the class gives the same numbers on a second run; all seven attendance pages opened in Chromium with one policy and no violation |

- **AC-IAM-52a, an existing scenario whose expectation changed (its meaning kept).** Its step «his own attendance record edited
  with the ALEP employee's id» expected 404: before this plan the edit bound the employee and refused a foreign one. The edit now
  binds the status and the note only (the plan's fix of «an edit can move a record»), so the post is an edit of his own record
  that moves nothing: 200, and the record is still his employee's on its own day (read by psql before and after: its employee, day,
  status and note), while ALEP's employee gets no row. AC-LVA-57f proves the same with a forged date too. Commit `a876899d`.
- **AC-LVA-57h gained a step** (`361e20c2`): after the sub-unit's close, the branch still writes by hand a July day of an employee
  of another directorate, while the same day of a closed employee is refused — the lock is the closed employees' only. It also
  leaves an unlocked record for `AcUx67`'s edit page.
- **AC-LVA-57e on the fixed images** (the gate's scope is every active pay profile again): 1,102 in scope and missing before
  the close; 1,189 summaries and receipts after it; 20 still missing, none in service in June 2025 by HR.Core's rule; one open
  warning record that lists exactly those 20.

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

P7, on prod-like stack 2 (started 2026-11-26T00:28:09Z, images of `b92ed935`): `AcLva57AttendancePeriodTests` then
`AcUx67ReadableInterfaceTests` by hand (`artifacts/plan57/run-classes.ps1`), stack down; then
`scripts/ci/acceptance-job.ps1 -Stage prodLike:journey,prodLike:main,prodLike:isolated -NoBuild`; the lab stage
(`-Stage lab:main,lab:journey`) with the seeded counts; the migrator image (`artifacts/plan57/migrator-compose.yml`) and
`-Stage prodLike:leastpriv -NoBuild`; the full Testcontainers step again (the review fixes changed what it tests); the P7 commit.
Then P8 (`git fetch`, merge `origin/main` only if it moved) and P9 (the demo server; `AcLva57VpsChecks` is written).
