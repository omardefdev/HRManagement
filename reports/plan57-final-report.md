# PLAN-57 — final report

**A closed attendance period, corrections that become differences, and a reliable handover to payroll (LVA-C).**
Plan: [PLAN-57](../docs/acceptance-plans/PLAN-57-lva-attendance-period-payroll-handoff.md). Prompt:
[PLAN-57-AGENT-PROMPT.md](../docs/acceptance-plans/execution/PLAN-57-AGENT-PROMPT.md). Checkpoint with every stamp:
[plan57-progress.md](plan57-progress.md). Baseline: [plan57-baseline.md](plan57-baseline.md). One session, 2026-11-25 to
2026-11-26, on the owner's Linux notebook; no owner action. The owner allowed, in the session's first message, trimming Seq's
logs on the server to the last 30 days (section 12).

## 1. Summary per phase

| Phase | What was done | Finished (UTC) |
|-------|---------------|----------------|
| P0 | Preconditions, keep-awake, the baseline, the attendance data inventory (local stack and demo), every writer of an attendance day and every reader of its statuses, the calculation paths, the reserved months proved by a run, the demo's picture | 11-25 20:49 |
| P1 | The model (period, closure, versioned summary, correction), the calendar from configuration, the summary calculator, migration `LVA057_AttendancePeriods` with its fill | 21:06 |
| P2 | One writer for an attendance day; the lock; the edit that cannot move a record; leave-made days; the bulk entry on working days | 21:25 |
| P3 | The close (one save, one event per summary), its API and screens; the old close and its event ended | 21:44 |
| P4 | The correction: its rules, the separation of duties, the atomic approval, the system corrections of a leave decision; its API and screens | 22:01 |
| P5 | Payroll's side: the receipt, the difference, the freeze, the carry, the refund, the gate, the reads and the run's panel; migration `LVA057_AttendanceHandoff` | 22:22 |
| P6 | PostgreSQL proofs (the close's atomicity, concurrency, version races, the carry, the gate, both migrations as a non-superuser), HTTP tests, the full Testcontainers step | 23:05 |
| P7 | AC-LVA-57a..h on the prod-like stack; the independent review (1 high, 6 medium, 7 low: fixed with tests); P-13's lists on prod-like, lab and leastpriv; the validate script; the Testcontainers step again | 11-26 03:14 |
| P8 | `origin/main` had not moved (`01a8c3f9`): nothing to merge | 03:14 |
| P9 | The demo server: backup, Seq's retention, the stop on the disk and the owner's answer, the registry cleaned, one build, the deploy, this plan's five checks, the older plans' checks, the smoke | 07:44 |
| P10 | Documents, this report, the local gates, the pull request and the merge | see the merge note in the progress file |

## 2. Commits

On `feature/plan-57-lva-attendance-period-payroll-handoff`, from `origin/main` `01a8c3f9`:

| Commit | Subject |
|--------|---------|
| `2691b459` | docs(pay056): PLAN-56's merge note (PR #56, merge commit 01a8c3f96d9b), the CodeQL fix, and the server on a commit of main |
| `f49f04de` | docs(lva057): the PLAN-57 agent prompt, written from the session's text |
| `38d42ba9` | chore(lva057): baseline, the attendance data inventory, every writer of an attendance day, the reserved months proved, the demo's picture |
| `6f887768` | feat(lva057): attendance periods, versioned employee summaries and corrections in the model; the calendar from configuration; migration LVA057_AttendancePeriods marks leave-made records |
| `6583a7e9` | feat(lva057): one writer for an attendance day; locked days refused; the edit cannot move a record; leave-made days restorable; the bulk entry fills working days only |
| `2e105bd2` | feat(lva057): the attendance close writes one versioned summary per employee and publishes IAttendanceSummaryPublished in the same save; the old close and its event end |
| `b3931914` | feat(lva057): a change after the close is a correction with a reason, approved by a central person who is not the requester, and publishes the summary's next version |
| `b8435a88` | feat(lva057): payroll records each attendance summary version as one difference, carries it past a frozen month, refunds a negative one, and checks attendance before a calculation |
| `4adfe897` | test(lva057): PostgreSQL proofs of concurrent closes, the close's outbox atomicity, version races on one receipt, the carry past a frozen month and the gate |
| `73f397e5` | fix(lva057): the attendance gate leaves out an employee whose first pay profile begins after the run's period |
| `cdcb541c` | test(lva057): AC-LVA-57's class with its CI scope, ownership and personas; the validate script with its self-test; the close runbook; AC-UX-67's key pages |
| `b92ed935` | fix(lva057): review — the gate's warning never unlocks a posting, a close and a writer meet on the month's lock, a decision race on one request, the end of service and the reconciler read moves only, a carried row is marked by one run |
| `c4d7da7e` | docs(lva057): the attendance model as built, payroll's side, the gap studies' status lines, the CI notes; the demo server's checks |
| `361e20c2` | test(lva057): AC-LVA-57h also proves the lock is the closed employees' only |
| `a876899d` | test(lva057): AC-IAM-52a — an edit posted with another branch's employee moves nothing (the form binds the status and the note only); it used to be refused for that employee |
| `938bffd5` | test(lva057): AC-LVA-57a..h pass on the prod-like stack; review fixes; CI scope and ownership |

(The `docs(lva057): progress …` commits between them carry the checkpoint file only. This report, the pull request's text and anything after them — the release branch, a fix a check asks for, the merge — are in the merge note of the progress file.)

Nothing was pushed before the release branch, and no history was rewritten.

## 3. Decisions applied and deviations

No deviation defers a part of the plan. The correction API stays in scope and is built. Where the prompt and the plan differ, the
prompt was followed.

**The reserved months (P-12, D-P57-1).** January to July 2025, agency A (`testdata/acceptance/lva057-periods.json`): the plan's
2026-01..04 are taken by the seed's runs. P0 proved a run of January 2025 calculates (1,102 lines, 9 s) and cancels. 57a and 57g
January, 57b February (holidays 17 and 18 February, in the acceptance compose layer only), 57c March, 57d April and May, 57e
June, 57f and 57h July.

**The HTTP answers (P-4).** Done 200; a broken rule 400 (a month that cannot be closed, a short reason, the requester's own
decision, a rejection without a note, a body that is not a request); outside the caller's scope or policy 403; another agency's
unit or request 404; a request that lost to another 409 (an incomplete close with its list, a second waiting request, a decided
request, a decision that lost a race). Every refusal carries its Arabic message and changes nothing. Every write validates an
antiforgery token (`GET api/attendance/periods/antiforgery`, `GET api/attendance/corrections/antiforgery`).

**The old consumer's fate (P-8, D-P57-2).** Deleted. The demo's queue `payroll-web-attendance-period-closed` held no message at
P0 and again right before the deploy. `IAttendancePeriodClosed` is marked obsolete, with no publisher and no consumer
(`scripts/lva057-validate.ps1` refuses a reference).

**AC-IAM-52's day.** `FreeAttendanceDayAsync` picks a day with no record of the employee **and** that no attendance summary of
the employee covers. Its meaning (a branch creates attendance in its own scope; another branch's is refused) is unchanged.

**New enums are stored as integers** (D-P57-3) in HR.Core's new tables and columns, as `DailyAttendanceRecords.Status` is. A
release that does not know a value still reads the row (section 12, the rollback note).

**What the plan left open, decided here, each recorded with its reason in the progress file:**

1. **Every status that ends a service** (Terminated, Retired, Resigned, Deceased, Merged, Anonymized) follows the plan's rule for
   `Terminated`; `PendingActivation` has no attendance. Otherwise a retired employee would need attendance for ever under
   `RequireComplete`.
2. **The end of a service is the last history row that moved the employee into an ended status.** A row that notes a status over
   itself (another leave's cancellation, a medical decision) is not a move (the review's M3).
3. **A period closes only once it has ended** (Damascus date): closing a running month would lock days that have not happened.
4. **A repeat close** that finds everyone closed writes no closure row and no event, answers `closedEmployees = 0`, and reads
   the period's state again.
5. **A system correction from a leave** is one row per locked day and **one** new version and event per leave event and period.
   A correction's `NewStatus` may be empty: a cancelled leave on a locked day that had no record before the leave removes the
   record.
6. **A leave-made record of before this plan carries no leave reference** (the migration marks it by its note); a
   cancellation's removal also takes such a record in the leave's range. A manual record is never touched.
7. **A leave-made day of a closed month is not corrected by a person** (it changes with the leave), also when the leave took
   the day while a request waited. An approval whose day already holds the requested status answers 409.
8. **The requester decides neither way:** a rejection by the requester is refused too.
9. **`RequestedByName` / `DecidedByName`** are stored on the correction, so the pages show a name and never an id.
10. **`CloseAsync` is not marked `[StagesOutboxMessages]`** (the guard matches a marked method by its name, and another
    `CloseAsync` exists); it saves itself, and `PublishSummaryAsync` carries the mark.
11. **Payroll's loader also takes a «stranded» row:** a free, unmarked attendance row of an earlier period whose run has been
    calculated meanwhile (the consumer can decide «not carried» an instant before that run freezes). Nothing else would ever
    take it.
12. **The gate's tests sit in the integration project** (SQLite, through the real start of a calculation), not in the unit
    project as the plan lists them.
13. **The gate's warning is one open audit record per run,** refreshed by a recalculation and removed when nobody is missing
    any more. It is not a review: the posting ignores it even closed, the screens do not close it (the review's high finding).

**AC-LVA-57e's positive, a deviation in the scenario only.** The plan asks that a run of a fully closed month have
`missingAttendance = []` and no warning record. The gate's scope is the plan's: who the run gives a line to (every active pay
profile). The prod-like seed dates some pay profiles before their employee's hire date, and HR.Core closes no attendance before a
hire; and no month free for the class (2025-01..07) has everyone hired. So on that stack a whole-agency close leaves 20
employees listed (of 1,102 in scope; 1,189 summaries written). The scenario asserts what is true and stricter than «a list»:
nobody HR.Core closed is missing; whoever is left is not in service in the month by HR.Core's own rule; and the warning lists
exactly them. The empty list with no warning record is proved by
`Lva057PayrollAttendanceGateTests.A_run_whose_employees_are_all_closed_gets_no_warning_record` and on PostgreSQL by
`Lva057PayrollAttendanceHandoffIntegrationTests`. A short-lived narrowing of the scope (`73f397e5`: «a pay profile that began by
the period's end») was reverted in the review fixes: it hid employees who are paid.

**The leave schedule reconciler's rule** is proved by unit tests (`Lva057LeaveAttendanceTests`, `Lva057ReviewFixTests`): its job
runs hourly, and the acceptance class restarts no container.

**Test-only preconditions of the acceptance class,** each recorded in its scenario's steps: the reset of the scenario's month;
the employee whose service ended inside January (psql; no screen ends a service on a past date); the injected leave events and
replayed summary versions; at a scenario's end, its runs cancelled through PLAN-56's API, the carried difference a cancellation
freed deleted, and the zero adjustments payroll notes for an injected leave deleted.

## 4. The period and the lock

- **Model:** `AttendancePeriod` (a calendar month of one agency; `Custom` periods behind a setting, API only, never overlapping),
  `AttendancePeriodClosure`, `AttendanceEmployeeSummary` (unique per period and employee, `Version`, six counts, `xmin`),
  `AttendanceCorrection`. Migration `LVA057_AttendancePeriods`: four tenant tables under forced row security, three columns on
  `DailyAttendanceRecords` (`Source`, `SourceReference`, `ReplacedStatus`), and the fill `Source = Leave` for leave-made records
  with `FORCE` lifted around it.
- **The close's employees:** the unit's tree by `MaterializedPath` (none = the whole agency, for a central user), hired by the
  period's end, not waiting for activation, with who left on or after the period's first day; less whom an earlier close gave a
  summary.
- **Its rules:** working days only (weekly rest days and holidays from configuration); `RequireComplete` refuses the whole close
  with the list and writes nothing (`CountAsAbsent` and `Ignore` are the alternatives); a branch closes its own tree (403
  outside, 404 another agency's unit); a period not ended is refused.
- **Its atomicity:** one save writes the period, every summary at version 1, the closure row and one outbox message per
  summary. An injected failure after the events are staged leaves no summary, no period, no closure and no outbox message
  (`Lva057AttendanceCloseIntegrationTests`, PostgreSQL, under row security).
- **Concurrency:** a close and every save of an attendance day meet on a PostgreSQL advisory lock per agency and month
  (`AttendanceMonthLock`). Two closes at once take turns, and the second finds everyone closed. A save that read the day as free
  and is saved after a close locked it is refused, with nothing written. A close waits for a save in flight and counts what it
  wrote. The summary's unique index stays as the last line.
- **Performance measured (PostgreSQL, this notebook):** 2,000 employees × 31 days closed in **2.69 s** (limit 10 s); the lock
  lookup for a page of records in 0.10 s. On the prod-like stack the whole-agency close of June 2025 (1,189 employees) and its
  1,189 receipts in payroll completed inside the scenario's 42 s.

## 5. One writer

- **What writes a day now:** `AttendanceDayWriter` only — `CreateManualAsync`, `UpdateManualAsync`, `DeleteManualAsync`,
  `BulkFillAsync`, `ApplyLeaveAsync`, `RevertLeaveAsync`, `ApplyApprovedCorrectionAsync`. It checks the scope, the lock and the
  status, and never saves: the caller's one save writes the day with whatever belongs to it.
- **What was removed:** the controller's direct writes; the edit's binding of the employee and the date (an edit could move a
  record to another employee); the first-50 employee list (a scoped picker instead); «في إجازة» by hand, in a create, an edit
  and the bulk entry; the bulk entry's writes on rest days, holidays and future days; the old close
  (`ClosePeriodAndPublishAsync`), its action and its view.
- **The leave consumers:** an approved leave writes its unlocked days as leave-made (`Source = Leave`, with the leave's
  reference, keeping what it replaced); a cancelled leave removes or restores only what the leave wrote; locked days become
  system corrections; the employee's status history, the days and the corrections are one save.
- **The reconciler** reads leave-made days only, and returns to work only an employee a leave (or itself) moved to «في إجازة».
- **Guards:** `AttendanceSingleWriterArchitectureTests` (exceptions: the demo seeder only) and `scripts/lva057-validate.ps1`.

## 6. The correction

- **Rules:** a locked day; another status than the day's; never «في إجازة»; a reason of at least 15 characters; one waiting
  request per employee and day (a filtered unique index); the employee in the requester's scope.
- **Separation of duties:** the decision's policy `HrCore.AttendanceCorrectionApprove` is hr-admin or hr-core and **not
  branch-scoped** (`[OrgScopeCentralOnly]` on the four decision actions); the service compares the caller with the requester,
  whatever the caller's roles, for an approval and a rejection alike.
- **An approval is one save:** the day (through the writer), the summary recalculated at its next version, the correction and
  the event. Two approvals on one summary at once write two versions (the `xmin` retry runs in a new unit of work — a defect the
  PostgreSQL test found: the bus outbox's state was lost when the same context retried). An approval and a rejection of one
  request at once do not both win (`xmin` on the request).
- **System corrections:** a leave decision on a locked day, with no requester and no decider; `AutoApply` applies it in the
  consumer's save, `Pending` leaves it for a central decision in place of a person's waiting request; a leave cancelled before
  the decision withdraws it; the same event twice makes one (a unique index per employee, day and leave reference).

## 7. The contract

`IAttendanceSummaryPublished` (schema 1): `SummaryId`, `Version`, `AttendancePeriodId`, `PeriodStart`, `PeriodEnd`, `EmployeeId`,
`WorkingDays`, `PresentDays`, `AbsentDays`, `OnLeaveDays`, `RemoteDays`, `UnrecordedWorkingDays`, `Reason`, `CorrectionId`,
`TenantId`, `OccurredAt`, `CorrelationId`, `SourceSystem`, `SchemaVersion`.

Its eight rules:
1. One summary per employee and period; its id never changes.
2. A version is the whole summary (absolute counts), not a change.
3. Version 1 is the close; each approved correction (or leave event on locked days) is the next.
4. A version is published in the save that writes it (the outbox).
5. The tenant is on the message and in the header; the consumer writes under the message's tenant.
6. The consumer records a version once: a redelivery writes nothing, and an older version after a newer one is ignored.
7. The difference in absent days against what was recorded is one row; zero writes none.
8. A difference that meets a frozen run of its period is carried to the next cycle; the frozen run never changes.

**What PLAN-59 calls:** `POST api/attendance/periods/{yyyy-MM}/close`, `GET api/attendance/periods/{yyyy-MM}`,
`GET api/payroll/attendance/receipts?periodStart=`, `GET api/payroll/runs/{id}/attendance-status`
(`docs/HCM_002_ATTENDANCE.md`, `docs/runbooks/attendance-period-close.md`).

## 8. Payroll's side

- **The receipt** (`AttendanceSummaryReceipt`, migration `LVA057_AttendanceHandoff`, forced row security): inserted
  `ON CONFLICT DO NOTHING`, then locked `FOR UPDATE`; two versions of one summary at once never compute from the same base.
- **The difference:** `AbsenceDeduction` with `SourceReference = ATT:{SummaryId}:v{Version}` (unique), `SourceVersion`,
  `OriginalPeriodStart/End`, `CarryForward`; negative days for a refund.
- **The freeze** (`PayrollAttendanceFreeze`): the period's regular run, not cancelled and not reversed, whose results are frozen
  or that is under review, approved or posted — every run state covered by an integration test.
- **The carry:** a carried row is taken by the first regular run calculated after it, as «فرق غياب عن فترة yyyy-MM»; a
  cancellation, a rejection or a reversal frees it again (PLAN-56's release touches the markers only). The row has a row version:
  of two runs calculating at once, one marks it.
- **The refund:** `AbsenceRule` consumes every row; negative days give «استرداد خصم غياب — فترة yyyy-MM», a positive amount.
- **The gate** (`Payroll:Attendance:Gate`, `Warn` by default), after the interim gate and before the run moves: `Block` refuses
  with the count and writes nothing; `Warn` adds one open audit record with the count and the ids; `Off`, a correction run and a
  final settlement pass. Its scope is who the run gives a line to.
- **The warning:** open, never closed by the screens, never the «closed audit record» a posting needs.
- **Reads and the panel:** the two GETs above; the run's page shows «الحضور» (mode, in scope, missing by name, carried applied
  and pending).

## 9. The existing tests that changed, and the numbers that did not move

| Test | What changed | The meaning it keeps |
|---|---|---|
| `OutboxAtomicityIntegrationTests.CloseAttendancePeriod_SavesAfterPublishLoop` | rewritten on `AttendancePeriodService` | every event is staged before the one save (stronger: it counts the saves at each publish) |
| `Iam051DirectServiceWriteTests` | the `ClosePeriod` row removed with the action; the attendance actions mapped to their three policies | every write action names its policy |
| `Iam052HrCoreScopeHttpTests` | seeds an `AttendanceCorrection` and a period; drives the new actions | another branch's record answers 404 and is unchanged |
| `IntegrationTestHelpers` | registers `AddHrAttendance` | — |
| `Pay056ReversalTests` | counts the closed audit records (the gate adds an open one) | a reversal writes no closed record by nobody |
| `AcIam52BranchScopeTests.FreeAttendanceDayAsync` | a day no summary of the employee covers | the branch creates attendance in its scope |
| `Lva057AttendanceCloseIntegrationTests` (this plan's, after the review) | two closes at once now take turns on the month's lock | no double summary, no partial effect |

**Numbers that did not move:** every existing payroll result — the golden pack (`Gov002LegalGoldenPackTests`),
`PayrollEnginePipelineTests`, the determinism tests, `PayrollFinancialInvariantsTests`, `Pay029*`, `Pay023*`, `Pay004*`,
`Pay056*` — passes unchanged; an old absence row gives the same amount and name as before. On the demo, the checksum of the
2,550 salary lines, of the 14 runs and of the 17,850 attendance records is P0's (section 12). No ceiling list grew.

## 10. The guards and their own negative tests

- `AttendanceSingleWriterArchitectureTests`: any write of an attendance day outside the writer fails the build's tests; its
  exception list holds the demo seeder only.
- `scripts/lva057-validate.ps1`: eleven checks (section 3 of `docs/CI_AND_TESTING.md` lists them); `-SelfTest` plants 44 defects
  and requires each to be refused, and the repository itself to pass.
- `OutboxPublishOrderingArchitectureTests` with the inventory row of `IAttendanceSummaryPublished`;
  `WriteActionsHaveExplicitPolicyTests`, `OrgScopeCoverageTests`, `InterfaceTextGuardTests` cover the new controllers and views.
- The month's lock is guarded by the script (the async save goes through it, the sync save refuses, the close takes it
  exclusively before it reads) and proved by three PostgreSQL tests.

## 11. Results

**Build:** `dotnet build HRManagement.slnx -warnaserror`: 0 warnings, 0 errors (4 min 25 s at the baseline, 5 min 41 s with the
review fixes). `dotnet ef migrations has-pending-model-changes`: none, for HR.Core and for Payroll.

**Unit and architecture** (this plan's classes, all in `HRManagement.UnitTests`): `Lva057AttendanceSummaryCalculatorTests` 11,
`Lva057AttendanceDayWriterTests` 20, `Lva057LeaveAttendanceTests` 6, `Lva057AttendanceCloseTests` 10,
`Lva057AttendanceCorrectionRulesTests` 13, `Lva057AbsenceRuleRefundTests` 7, `Lva057ReviewFixTests` 6,
`AttendanceSingleWriterArchitectureTests`. The filter run after the review fixes (Lva057, Payroll, Pay0, Gov002, Architecture,
Leave, Consumer, Attendance, Hcm002, InterfaceText, Guard): **1,565 passed**. `HRManagement.ArchitectureTests`: **414 passed**.

**Integration, SQLite** (`Lva057PayrollAttendanceHandoffTests` 27, `Lva057PayrollAttendanceGateTests` 10,
`Lva057AttendanceApiHttpTests` 8, with the payroll, outbox, inbound-event and scope filters): **231 passed**, 124 skipped
(Testcontainers classes, run below).

**Integration, PostgreSQL** (Testcontainers): `Lva057MigrationPostgreSqlTests` 2, `Lva057AttendanceCloseIntegrationTests` 10,
`Lva057PayrollAttendanceHandoffIntegrationTests` 10, `Lva057PayrollAttendanceReadsHttpTests` 1 — 22 of 22 (and the
`Lva057AttendanceApiHttpTests` host). **The full Testcontainers step with CI's filter,** on the host: 320 passed, 1 skipped before
the review (14 min 51 s); **325 passed, 1 skipped** after it (14 min 56 s).

**Acceptance on this notebook** (images built here one service at a time; the tables with every stamp are in the progress file):

| Run | Result |
|---|---|
| `AcLva57AttendancePeriodTests` alone, three fresh prod-like stacks | 7 of 8 on the first (a wait in the test itself), then **8 of 8**, 8 of 8 |
| `AcLva57AttendancePeriodTests` twice on one stack | **8 of 8 both times** (each scenario resets its own month) |
| Acceptance job, prod-like: journey, main (9 classes in the group's own order), isolated (7 classes) | journey **1 of 1**; main **54 of 55** — the one failure was AC-IAM-52a's old expectation (section 9), fixed and run again with AC-LVA-57 on a fresh stack: **13 of 13**; isolated **47 of 47** (AcInt37 8, AcMsg40 8, AcWf44 5, AcWf45 8, AcWf46 9, AcPol47 1, AcOps39 8) |
| `AcUx67ReadableInterfaceTests` by hand in Chromium (the job's browser install needs sudo here) | **7 of 7**, twice; the seven attendance pages open with one CSP policy and no violation |
| Acceptance job, lab: AC-16, AC-21, AC-23, AC-24, AC-25, the catalog, AC-HCM-29, the lab journey | **17 of 17** and the journey 1 of 1; `acceptance-guard: PASS` |
| Seeded counts by psql on the fresh lab stack | the 32 tables of `acceptance-verify.ps1` have their expected counts |
| Acceptance job, leastpriv (`AcOps38FirstBootTests`, `AcTen48LeastPrivilegeTests`) | 8 of 9 on the first run (a 502 seconds after the stack came up under load, as PLAN-56 met), **9 of 9** on the second: both migrations applied by the migrator's role from empty volumes |

**Validation scripts:** `lva057-validate.ps1` PASS, `-SelfTest` 44 planted defects refused. The validate scripts of `pay056`, `pay055`, `pay054`, `iam053`, `iam052`, `iam051`, `iam050` and `ux067` pass; `scripts/vps/lint-compose.ps1` passes for both chains (22 and 24 services); `scripts/int037-wiring-check.ps1` passes. `pol049-validate.ps1` is red on `main` already and is red the same way after this plan (28 findings, before and after).

**The evidence of record is the pull request's run:** its link, each check's result and the merge commit are in the merge note of `reports/plan57-progress.md` (this report is committed before the push). No evidence is committed.

## 12. The server

**Before (P0's read-only picture):** tag `e64a0f7e65be` (on `origin/main`); 21 GB free of 145 GB; `hrm_seq_data` 46.85 GB;
17,850 attendance records dated 2026-09-02 to 2026-10-29, none on a Friday or a Saturday, none made by a leave (checksum
`28881f67…`); 14 payroll runs (`5e260f8e…`) and 2,550 salary lines (`a258ad24…`); no absence row; the old event's queue empty.

**The disk, and two decisions of the owner.** After P-16's removals the server had 26 GB free, under the 28 GB line for a build.
- **Seq's retention (allowed in the session's first message: the last 30 days).** Applied through Seq's own API, one policy
  (`retentionpolicy-34`). **Sizes: before 20,727 MiB free and `hrm_seq_data` 47.4 GB; after 20,726 MiB and 47.4 GB. It freed
  nothing,** because Seq's store begins on 2026-10-31 (26 days). From 2026-11-30 Seq deletes what is older than 30 days.
  **A deletion by retention cannot be undone.**
- The session stopped `BLOCKED` at 04:01Z with one question. A read-only look had found what really held the disk: **the loopback
  registry, 53.7 GB** — the images of every release pushed since PLAN-36, which nobody had measured — beside Seq's 47.4 GB.
- **The owner's answer (04:14Z): «امسح وأكمل»** — wipe the older releases from the local registry, keep the running one, finish
  the plan; and move to GHCR (the documented default, never yet used) as a change of its own after the merge.
  **Registry 53,651 MiB → 1,913 MiB; free 26,486 MiB → 78,221 MiB.** The running release's 17 images were pushed back (17 of
  17). **This cannot be undone:** 33 older tags are gone from the server; a rollback to one of them is a rebuild from git.

**Changes and their undo** (UTC of 2026-11-26; the same table, in Arabic, is in `docs/OPS_VPS_SINGLE_HOST.md`):

| # | When | Change | Undo |
|---|------|--------|------|
| 1 | 02:59:30 | Backup `/opt/hrm/backups/pre-plan57-20261126T025930Z` (700/600): `pg_dumpall.sql` 44,611,604 bytes, `env.backup`, `SHA256SUMS`; verified (sums, 17 `CREATE DATABASE` lines, the dump complete) | delete the folder when no longer needed |
| 2 | 03:08:20 | Seq retention policy, 30 days | the policy: `curl -X DELETE …/api/retentionpolicies/retentionpolicy-34`; **a deletion cannot be undone** |
| 3 | 03:09:23 | The 17 local images of the older release `69570ea4e1f6` removed (20,724 → 26,521 MiB) | none since row 4 |
| 4 | 04:15:53 | The loopback registry wiped; the running release pushed back | **none** (a rebuild from git) |
| 5 | 04:17:08–04:48:52 | One detached build of `4eccb1f269a0`, watched by one process on the server with a watchdog (stop by PID under 3,000 MiB): upload 29 s, build 1,798 s, push 32 s, 17 of 17 images; **78,187 MiB before, 50,031 MiB at the lowest, 70,601 MiB after** | none needed |
| 6 | 04:49:30–04:53:01 | Deploy of `4eccb1f269a0`, first try: pull 6 s, up 116 s, bootstrap 54 s (the two migrations), reconcile 11 s, healthy after 15 s; the services away about 2.5 min; smoke 50 passed, 0 failed, 1 warning | `scripts/vps/deploy.ps1 -Tag e64a0f7e65be`, after reading the rollback note |
| 7 | 04:53:50–05:12:58 | What the checks left (below) | none; the close is not reopened |
| 8 | 05:13–07:38 | What the older plans' checks left, as in every run of them; the smoke with the integration wiring | none (records) |

**The close the checks made, and why there.** The unit **`HR-ATT`** (17 employees), **August 2026**: a small unit (P-16: at most
20), in a past month that no demo run covers (the demo's runs are January to June 2026) and that holds none of the demo's
seeded attendance (September and October 2026), so nothing of the demo's own data is locked or counted. Its working days were
filled by the bulk entry as present (22 each, none on a rest day), and it was closed through the API: 17 summaries at version
1, 17 receipts in payroll, the period «partially closed». **It stays closed: a closed period is not reopened, by design.**

**This plan's five checks (`AcLva57VpsChecks`), all passed:**
1. *The migrations:* both recorded; the new tables, `DailyAttendanceRecords`, the receipts and `AbsenceDeductions` under forced
   row security; no leave-made record, as the inventory counted; the attendance records outside August, the runs and the lines
   are the picture's, checksum for checksum; the old queue has no message and no consumer, the new one is consumed.
2. *One close:* as above; a create by hand on a locked day refused, nothing written; the same close again wrote nothing; no other
   unit's record, no older record, no run and no line changed.
3. *Two corrections:* `hradmin` asks for «غائب» on 5 August; his own approval answers 400 and changes nothing; `e2e-hr-admin`
   approves → version 2 and a row of +1 in payroll; the reverse → version 3 and a row of −1. Payroll's free days for the summary:
   0 before, 0 after.
4. *The gate, read only:* `attendance-status` of the run of June 2026: `Warn`, 425 in scope, 425 missing; the receipts of August
   2026: 17. No run, line or audit record changed.
5. *The screens:* ten pages in Chromium at 1366×768 and 390×844, one enforced CSP policy each, no violation; Arabic,
   right-to-left; names and no identifier; the edit form posts neither the employee nor the date and offers no «في إجازة»;
   the run's page has its attendance panel. (One sign-in was repeated after `ERR_NETWORK_CHANGED` on this notebook.)

**Everyone else:** the PLAN-36..56 server checks (`AcOps36VpsBootTests` with its partial classes and `AcInt37VpsServerTests`, 36j
excepted), 05:13:29Z to 07:31:48Z from this notebook: **71 of 73 in the run, 72 of 73 after one rerun.**
`AC_WF_46_vps_the_sweeps_run…` passes only in the minutes after a sweep cycle and passed alone, started 9 s after one (the cause
PLAN-55 recorded). `AC_POL_47_vps_the_migration_the_trigger…` is **not passed, for the local cause PLAN-53 recorded**: it reads a
file of the formatted Windows machine. `AC_UX_67_vps_every_key_page…` passed with this plan's attendance pages among its key
pages. `scripts/vps/smoke.ps1 -RequireIntegrationWiring`: **50 passed, 0 failed, 1 warning** (the client-side port check), after
the deploy and again after every suite. This plan's close check, run a second time, found the month closed and wrote nothing.

**The rollback note** (written into `OPS_VPS_SINGLE_HOST.md`; PLAN-56's code read with `git show`): the older release ignores
the new tables and columns (the new columns have defaults or take null; the new enums are integers in tables it never reads, so
no row breaks its reader); **it has no lock** (its screens write on closed days, its edit rebinds the employee, it takes «في
إجازة» by hand); its old close publishes the old event again and its old consumer returns, writing `AttendancePeriodClosed:`
rows the new handover does not know, so a later real close of that month would deduct twice; its `AbsenceRule` skips a refund
row and applies the positive one, so it over-deducts, and its loader carries nothing; the gate's warning is an ordinary audit
record to it, closable, and then enough for a posting. **So: no close, no correction and no calculation while the older release
runs;** after real closes, the way back is a fix forward. `Down` of the two migrations is never used.

**After:** tag `4eccb1f269a0` (the branch's head when the server was built — an ancestor of the merge commit; what came after it on the branch is documents and reports only); 21 containers healthy; 70 GB free; registry 3.6 GB (two releases); Seq 47.4 GB
with a 30-day policy.

## 13. Changes for API clients and operators

- **New API** (HR.Core, through the gateway under `api/hr-core/`): the close and the read of a period, the request and the two
  decisions of a correction, and their antiforgery endpoints; (Payroll) `attendance-status` of a run and the receipts of a
  period. Shapes and answers: `docs/HCM_002_ATTENDANCE.md`.
- **The old close ended:** `POST DailyAttendanceRecords/ClosePeriod` is gone (its GET redirects to «فترات الحضور»), and
  `IAttendancePeriodClosed` has no publisher and no consumer.
- **Policies of the attendance actions:** `HrCore.AttendanceWrite` (create, edit, delete, bulk entry, ask for a correction),
  `HrCore.AttendanceClose` (close), `HrCore.AttendanceCorrectionApprove` (decide; never a branch-scoped user).
- **Settings:** `Attendance:Periods:Mode`, `Attendance:Close:UnrecordedWorkingDays`, `Attendance:Corrections:LeaveEventsAfterClose`,
  `Attendance:Calendar:WeeklyRestDays`, `Attendance:Calendar:Holidays` (HR.Core); `Payroll:Attendance:Gate` (Payroll).
  Holidays are the operator's, by environment; none ships.
- **What a user sees:** «فترات الحضور» and «تصحيحات الحضور» in the menu; the attendance edit shows the employee and the date
  read-only and offers no «في إجازة»; a locked day offers «طلب تصحيح» instead of «تعديل»; the bulk entry says how many days it
  wrote and skipped; a run's page has an attendance panel; «مراجعة الرواتب» shows the gate's warning with no «إغلاق».
- **Runbook:** `docs/runbooks/attendance-period-close.md`. **Read-only inventory:** `scripts/lva057-validate.ps1 -Inventory`.

## 14. Items other plans own, and defects for later plans

**Owned elsewhere (the plan's «out of scope»):** the binding of `hr-contracts-web` in production (PLAN-37, G-LVA-18); the branch
scope of rosters (PLAN-52); the atomic approval and cancellation of a leave (LVA-A — why the acceptance injects the leave
event); balances, the legal catalogue, governance, overlap and ESS (LVA-B/D/E/F); a «mission» status and a central dated
calendar (LVA-G); shifts, timing, lateness, devices and overtime (LVA-H, PAY-H); the payroll period's cut-off, the historical
wage of a difference, and returning a cancelled leave's deduction after it was applied (PAY-H); the full cycle to the
disbursement (PLAN-59).

**Defects and notes for later plans:**
1. **Payroll gives a line to every active pay profile, whatever its dates** (an employee whose profile begins after the run's
   period is paid for it), and knows no hire date. The gate lists such an employee as missing, rightly; under `Block` he holds
   the run back. PLAN-59 should decide the run's scope before it raises the gate (PAY-H owns the period's cut-off).
2. **Two regular runs of one agency calculating at once** both mark a free deduction or adjustment (the marking is
   unconditional; older than this plan). This plan closed it for attendance rows only (`xmin` on `AbsenceDeduction`). PLAN-58.
3. **The prod-like seed dates pay profiles without regard to hire dates** (`DemoPayrollSeeder`), which is why AC-LVA-57e cannot
   show an empty list on that stack. Changing the seed changes every seeded payroll figure; PLAN-59 or PLAN-65.
4. **Payroll notes every leave decision as an adjustment of zero** with no run (older than this plan); the next regular run
   takes it. Harmless in amounts, noise in a run's lines. PAY-H.
5. `scripts/pol049-validate.ps1` is red on `main` (28 findings) before and after this plan; AC-12 is `Blocked` on the lab stack
   as in the committed evidence (PLAN-59's).
6. The correction's decision is a single approval. A second level, or a threshold above which payroll must also agree, is
   governance (LVA-E).

## 15. Timing

All times UTC, 2026-11-25 and 2026-11-26. The stamps are in `reports/plan57-progress.md`.

| | |
|-|-|
| Started | 11-25 20:19:57Z |
| Context summarised (no work lost), twice | resumed at once each time |
| Stop 1 | 11-26 04:01Z to 04:15:04Z — **14 min.** `BLOCKED:` on P-16's disk line (26 GB free, the line 28; the Seq retention the owner had allowed freed nothing). The owner asked why the local registry was not wiped in favour of GHCR, was answered, and wrote «امسح وأكمل» |
| This report written | 11-26 07:44Z |
| Wall-clock to this report | **about 11 h 30 min**, of which 14 min stopped |

There was no other stop: no machine restart, no turn ended on a wait.

Phases (finished): P0 20:49:10Z · P1 21:06:33Z · P2 21:25:58Z · P3 21:44:57Z · P4 22:01:57Z · P5 22:22:14Z · P6 23:05:16Z ·
P7 03:14:58Z · P8 nothing to merge · P9 07:44Z.

Inside the working time, what was waiting rather than work (the acceptance class's check against the source, the independent
review, the review's fixes, the server check class, the documents and this report were done during these waits):

| Wait | Time |
|---|---|
| Solution builds (`-warnaserror`, several) | 4 min 25 s to 5 min 41 s each |
| The full Testcontainers step, twice so far (a third on the final head) | 14 min 51 s, 14 min 56 s |
| Acceptance images: 16 services one at a time; HR.Core and Payroll again after the review | 1,320 s; 280 s |
| The migrator image (`docker compose build`, no buildx here) | 1,225 s |
| Prod-like stack starts by hand, four times | 14 min 38 s, 14 min 43 s, 14 min 47 s, 14 min 27 s |
| Acceptance job, prod-like (journey, main, isolated) | 39 min 38 s (stack 885 s, main 645 s) |
| Acceptance job, lab | 21 min 14 s (stack 897 s) |
| Acceptance job, leastpriv, twice | 8 min 57 s, 9 min 6 s |
| Local CI steps in a fresh clone, first pass (a second on the final head) | 43 min 21 s |
| The server: backup; build (upload 29 s, build 1,798 s, push 32 s); deploy; this plan's five checks; the older plans' checks | 1 min; 31 min; 3 min 31 s; 19 min; 138 min |

The waiting for the pull request's checks, the Actions minutes and the merge are in the merge note of the progress file.

`pwsh -NoProfile -File scripts/plan-timing.ps1 -From 25 -To 67` (at 04:33Z; the row of this plan grows until the merge):

```
PLAN-49      11 2026-11-10 11:35 2026-11-10 11:37 2026-11-11 04:35   True 16:58
PLAN-50      14 2026-11-11 13:00 2026-11-11 13:02 2026-11-11 23:10   True 10:08
PLAN-51      15 2026-11-12 02:26 2026-11-12 02:30 2026-11-12 20:04   True 17:34
PLAN-52      11 2026-11-12 23:25 2026-11-12 23:26 2026-11-13 12:40   True 13:13
PLAN-53      17 2026-11-21 03:15 2026-11-22 15:34 2026-11-23 07:39  False 16:05
PLAN-54      17 2026-11-23 10:26 2026-11-23 10:27 2026-11-24 06:20  False 19:53
PLAN-55      15 2026-11-24 08:14 2026-11-24 08:14 2026-11-25 05:51  False 21:36
PLAN-56      19 2026-11-25 07:53 2026-11-25 07:58 2026-11-25 22:50  False 14:52
PLAN-57      25 2026-11-25 23:19 2026-11-25 23:21 2026-11-26 07:18  False 7:56
```

(The script's columns are in the machine's local time, UTC+3: commits, first commit, start, end, merged, wall-clock. The full
table of PLAN-25..67 is in `artifacts/plan57/plan-timing.txt`.)

## 16. Readiness note for PLAN-58, PLAN-59 and wave 2

- **The contract PLAN-59 calls:** close a month for the whole agency (`POST …/attendance/periods/{yyyy-MM}/close` with no unit,
  as a central user; 409 lists who lacks a working day), read `GET api/payroll/attendance/receipts?periodStart=` until it holds
  one receipt per summary, calculate, and read `attendance-status`. The gate may be raised to `Block` per environment
  (`Payroll__Attendance__Gate=Block`) once the run's scope and HR.Core's agree (item 1 above): on the prod-like seed it would
  refuse every month.
- **What PLAN-58's disbursement meets:** absence rows with negative days (a refund: a deduction component with a positive
  amount, so a line's total of deductions can fall below its statutory deductions' sum — the net formula of PLAN-56 holds);
  carried rows applied by a later month's run under the name «فرق غياب عن فترة yyyy-MM»; and a posting that still needs a closed
  audit record that is **not** the gate's warning (G-PAY-05 stays PLAN-58's).
- **What LVA-A changes:** the leave event will come from an atomic approval instead of the acceptance's injection. The
  consumers' contract does not change: unlocked days through the writer, locked days as system corrections, one save. The
  acceptance scenarios 57f and 57g then drive the leave's own screens, and their clean-up of payroll's zero adjustments goes
  when PAY-H stops writing them.
- **The historical wage (PAY-H):** a carried difference is priced by the run that applies it, at that run's daily rate. A wage
  change between the attendance period and the applying run prices the difference at the new wage. The row keeps the original
  period (`OriginalPeriodStart/End`), which is what PAY-H needs to price it at the period's own rate.
- **Rollback:** see section 12 — no close and no correction while PLAN-56's release runs.
