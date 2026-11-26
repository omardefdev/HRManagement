# HCM-002 — Attendance: the daily record, the period, the correction and the handover to payroll

**As built by PLAN-57 (LVA-C), 2026-11.** This document describes what runs. What the first version of this card described
(clock-in and clock-out, shifts, devices, overtime) is not built: see «Not built» at the end.

## Purpose

One status per employee and day; a month that really closes; a change after the close that is a recorded correction; and a
handover to payroll that payroll applies once, whatever the order and the number of deliveries.

## The model (HR.Core)

| Entity | What it is | Rules |
|---|---|---|
| `DailyAttendanceRecord` | One employee, one day, one status: «حاضر», «غائب», «في إجازة», «عن بُعد» | Unique per employee and day. `Source` says who wrote it: `Manual`, `Bulk`, `Leave`, `Correction`. `SourceReference` names the leave or the correction; `ReplacedStatus` is what a leave replaced, so that its cancellation can put it back |
| `AttendancePeriod` | A calendar month of one agency (`Attendance:Periods:Mode = CalendarMonth`; `Custom` periods through the API only, never overlapping) | Unique per agency and start. `Open` → `PartiallyClosed` → `Closed`. Never reopened |
| `AttendancePeriodClosure` | One close: who, when, which unit (none = the whole agency), how many employees | Written with the summaries of that close |
| `AttendanceEmployeeSummary` | One employee's figures for one period: working days, present, absent, on leave, remote, unrecorded | Unique per period and employee; `Version` 1 at the close, +1 per approved correction; `xmin` |
| `AttendanceCorrection` | A change of one locked day: the new status (or the record's removal), the reason, who asked and who decided | One waiting request per employee and day; one system correction per leave event and day; `xmin` |

All five are tenant tables under forced row security (migration `LVA057_AttendancePeriods`).

## Working days

A working day is a day that is not a weekly rest day (`Attendance:Calendar:WeeklyRestDays`, default `Friday,Saturday`) and not a
holiday (`Attendance:Calendar:Holidays`, a list of `yyyy-MM-dd`). Holidays are configuration, by environment: no list is in
code, and Production and Staging ship an empty one. «Today» and an employee's end of service are dates in the configured zone
(Damascus by default). A value that is not a date stops the service at start.

`AttendanceSummaryCalculator` counts an employee's days between `max(period start, hire date)` and
`min(period end, end of service)`. A record on a non-working day is not counted. `Attendance:Close:UnrecordedWorkingDays`
decides what a working day with no record means at the close: `RequireComplete` (default: the close is refused), `CountAsAbsent`
or `Ignore`.

## One writer

Every write of a day goes through `AttendanceDayWriter`; an architecture test
(`AttendanceSingleWriterArchitectureTests`) and `scripts/lva057-validate.ps1` refuse any other.

| Act | Who | Rules |
|---|---|---|
| Create, edit, delete by hand | `HrCore.AttendanceWrite` (hr-admin, hr-core; a branch manager for his unit's tree) | The employee is in the caller's scope. Never «في إجازة». Never a locked day. An edit changes the status and the note only: the employee and the date are not bound |
| Bulk entry | the same | Working days only, up to today, for the unit's tree and who left inside the month. A day that has a record or is locked is skipped |
| A leave approved or cancelled | the leave consumers | Unlocked days: the leave writes «في إجازة» (`Source = Leave`) and removes only what it wrote. Locked days: a system correction (below) |
| An approved correction | `AttendanceCorrectionService` | The one write of a locked day |

`LeaveScheduleReconciler` reads leave-made days only, and returns to work only an employee whom a leave (or itself) moved to
«في إجازة» — a history row that notes a status over the same status is not a move.

## The close

`AttendancePeriodService.CloseAsync` (screen «فترات الحضور» → «إغلاق فترة», and `POST api/attendance/periods/{yyyy-MM}/close`):

1. The period has ended; a branch closes its own tree (403 outside it, 404 for another agency's unit); the whole agency (no unit)
   is a central user's.
2. In scope: every employee of the unit's tree hired by the period's end and not waiting for activation; one whose service ended
   is in scope when it ended on or after the period's first day (the last history row that **moved** him into an ended status).
3. An employee an earlier close gave a summary is skipped; a close with nobody left writes nothing.
4. With `RequireComplete`, one employee with a working day unrecorded refuses the whole close (409 with the list); nothing is
   written, not even the period.
5. **One save:** the period, the summaries at version 1, the closure row and one `IAttendanceSummaryPublished` per summary in the
   outbox. A failure leaves none of them.
6. The period is `Closed` once no eligible employee of the agency lacks a summary; until then `PartiallyClosed`.

Measured on PostgreSQL: 2,000 employees × 31 days closed in 2.69 s.

## The lock

A day is locked when a summary of its employee covers it. `AttendanceLockService` answers that for the writer and the screens.

A writer reads the lock and saves later; a close reads the month's days and saves later. So that neither passes the other,
both meet on a PostgreSQL advisory lock per agency and month (`AttendanceMonthLock`): the close takes it exclusively before it
reads; every save that writes an attendance day (`HRCoreDbContext.SaveChangesAsync`) takes it shared and reads the lock again
before it writes. A save that began before the close finishes first and is counted; one that began after waits, and is refused
with «اليوم ضمن فترة حضور مغلقة». The days an approved correction writes are exempt.

## The correction

A change of a locked day (`AttendanceCorrectionService`; screen «تصحيحات الحضور», and `api/attendance/corrections`):

- **The request** (`HrCore.AttendanceWrite`, the employee in the caller's scope): a locked day, another status than the day's,
  never «في إجازة», a reason of at least 15 characters. One request waits per employee and day. A day a leave made is not
  corrected by a person: it changes with the leave.
- **The decision** (`HrCore.AttendanceCorrectionApprove`: hr-admin or hr-core, **not branch-scoped**), by someone who is **not
  the requester** — checked in the service, whatever the caller's roles. A rejection needs a note.
- **An approval, in one save:** the day, the summary recalculated at its next version, the correction, and the event. Two
  approvals on one summary at once give two versions (`xmin`, with a retry in a new unit of work); an approval and a rejection of
  one request at once do not both win (`xmin` on the request: the second answers 409).
- **A leave decision on a locked day** makes a system correction, with no requester and no decider:
  `Attendance:Corrections:LeaveEventsAfterClose = AutoApply` (default) applies it at once; `Pending` leaves it for a central
  decision, in place of a person's waiting request for that day. The same event delivered twice makes one correction.

## The contract with payroll

`IAttendanceSummaryPublished` (schema 1): `SummaryId`, `Version`, `AttendancePeriodId`, `PeriodStart`, `PeriodEnd`,
`EmployeeId`, the six counts, `Reason` (`Close`, `Correction`, `LeaveEvent`), `CorrectionId`, `TenantId`, `OccurredAt`.
The counts are **absolute**: a version says what the summary is, not what changed.

| Call | Policy | Answer |
|---|---|---|
| `POST api/attendance/periods/{yyyy-MM}/close` `{ "orgUnitId": null \| id }` | `HrCore.AttendanceClose` | 200 `{ periodId, status, closedEmployees, skippedAlreadyClosed }`; 400 a month it cannot close; 403 outside the scope; 404 another agency's unit; 409 `{ error, incomplete: [{ employeeId, unrecordedWorkingDays }], total }` |
| `GET api/attendance/periods/{yyyy-MM}` | `HrCore.ReadOnly`, in scope | the period's state, closed and remaining, and each summary's latest version |
| `POST api/attendance/corrections` `{ employeeId, attendanceDate, newStatus, reason }` | `HrCore.AttendanceWrite` | 200 the request; 400 a broken rule; 409 a request already waits |
| `POST api/attendance/corrections/{id}/approve` \| `/reject` `{ note }` | `HrCore.AttendanceCorrectionApprove` | 200; 400 the requester's own decision or a rejection without a note; 403 a branch; 409 already decided |
| `GET api/payroll/runs/{id}/attendance-status` | payroll module | `{ gateMode, employeesInScope, missingAttendance, carryForwardApplied, carryForwardPending }` |
| `GET api/payroll/attendance/receipts?periodStart=yyyy-MM-dd` | payroll module | `{ count, receipts: [{ employeeId, summaryId, latestVersion, latestAbsentDays, recordedAbsentDays }] }` |

Every write validates an antiforgery token (`GET …/antiforgery` issues it). Payroll's side — the receipt, the difference, the
carry, the refund and the gate — is in `docs/PAYROLL_RULES.md`. The operator's steps are in
`docs/runbooks/attendance-period-close.md`.

The old close (`IAttendancePeriodClosed`) is gone: its contract is marked obsolete and has no publisher and no consumer.

## Proof

| What | Where |
|---|---|
| The calculator, the writer, the leave paths, the close, the corrections' rules, the review's fixes | `Lva057AttendanceSummaryCalculatorTests`, `Lva057AttendanceDayWriterTests`, `Lva057LeaveAttendanceTests`, `Lva057AttendanceCloseTests`, `Lva057AttendanceCorrectionRulesTests`, `Lva057ReviewFixTests` |
| One save, the outbox, concurrent closes, the month's lock, two approvals at once, a decision race, `Pending` mode, 2,000 employees | `Lva057AttendanceCloseIntegrationTests` (PostgreSQL) |
| The migration's fill under forced row security | `Lva057MigrationPostgreSqlTests` |
| The API's answers, antiforgery, roles and scope | `Lva057AttendanceApiHttpTests` |
| End to end on the prod-like stack | `AcLva57AttendancePeriodTests` (AC-LVA-57a..h) |
| The files a person edits by hand | `scripts/lva057-validate.ps1` (`-SelfTest`) |

## Not built (other plans)

The shared-kernel entities `AttendanceRecord` (clock-in, clock-out, worked and overtime minutes, device source) and
`ShiftDefinition` (shift times, breaks, overtime thresholds) of this card's first version are models only: no table, no screen
and no import uses them (`Hcm002AttendanceTests` and `scripts/hcm002-validate.ps1` check that they exist). Shifts, timing,
lateness, devices and overtime are LVA-H / PAY-H (wave 2); a «mission» status and a central dated calendar are LVA-G; the
atomic approval of a leave is LVA-A.
