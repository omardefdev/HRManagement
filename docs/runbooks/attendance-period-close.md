# Runbook — Closing an attendance period and handing it to payroll

**Card:** PLAN-57 (LVA-C)  
**Who:** the HR officer of a unit (close), a central HR officer (decisions on corrections), the payroll processor (before a calculation)  
**Last reviewed:** 2026-11-26

## What a close is

An attendance period is a calendar month of one agency. Closing it for a unit:

- writes **one summary per employee** of the unit and of every unit below it (working days, present, absent, on leave, remote,
  unrecorded), at **version 1** — employees whose service ended inside the month are included, up to their last day;
- **locks** those employees' days of the month for every writer: the screens, the bulk entry, the API and a leave decision;
- publishes each summary to payroll (`IAttendanceSummaryPublished`), in the same database transaction as the close.

A closed period is **never reopened**. A change after the close is a **correction** (below).

A close and a write of an attendance day never pass each other: both take a lock on the agency's month in the database. A write
sent while a close runs waits for it, and is then refused with «اليوم ضمن فترة حضور مغلقة؛ قدّم طلب تصحيح» — nothing was written;
ask for a correction instead.

## Before the close

1. The month has ended (a period is not closed before its last day: «لا تُغلق فترة الحضور قبل انتهائها»).
2. Every employee has a record on every **working day** (the default rule, `Attendance:Close:UnrecordedWorkingDays = RequireComplete`).
   A working day is a day that is not a weekly rest day (`Attendance:Calendar:WeeklyRestDays`, Friday and Saturday) and not a holiday
   (`Attendance:Calendar:Holidays`).
3. Fill what is missing on «الحضور اليومي» → «إدخال جماعي»: it writes working days only, no future day, never over an existing
   record, and never «في إجازة» (that status comes from an approved leave only).

## Closing

| Step | Where | Result |
|------|-------|--------|
| 1 | «فترات الحضور» → «إغلاق فترة»: the month, and the unit (a central officer may leave the unit empty: the whole agency) | «أُغلقت فترة الحضور…» with the number of employees closed |
| 2 | If the close answers with a list of employees and days | Those employees have working days with no record: record them, close again. Nothing was written |
| 3 | «فترات الحضور»: the month's row | «مغلقة جزئياً» until every employee of the agency is closed, then «مغلقة» |

The API does the same: `POST api/attendance/periods/{yyyy-MM}/close` with `{ "orgUnitId": "<unit>" }` (409 with
`incomplete[]` when a working day is missing; 403 outside the caller's scope; 404 for a unit of another agency), and
`GET api/attendance/periods/{yyyy-MM}` reads the period and its summaries. Closing the same unit twice changes nothing.

## Holidays

Holidays are configuration, by environment: `Attendance__Calendar__Holidays__0=2027-04-17`, `…__1=…` on the HR.Core service
(dates as `yyyy-MM-dd`). The Production and Staging files ship with an **empty** list and the code holds none
(`scripts/lva057-validate.ps1` refuses otherwise). A value that is not a date stops the service at start, by design.
Set the holidays of a month **before** its close: a summary counts the working days as configured when it is written, and a later
change of the list does not rewrite a closed summary.

## A change after the close: a correction

1. On «الحضور اليومي», a locked day shows «طلب تصحيح». The requester (whoever may write attendance in the unit) gives the new
   status and a reason of at least 15 characters. One request per employee and day waits at a time.
2. A **central** officer who is **not the requester** decides it on «تصحيحات الحضور» (`HrCore.AttendanceCorrectionApprove`; a
   branch-scoped user cannot decide). A rejection needs a note.
3. An approval changes the day, writes the summary's **next version**, and publishes it — in one transaction.
4. A leave approved or cancelled for a locked day does the same by itself: a system correction with no requester
   (`Attendance:Corrections:LeaveEventsAfterClose = AutoApply`).

## What payroll does with it

- Each version is recorded once (a redelivery writes nothing; an older version that arrives late is ignored). The **difference** in
  absent days against what payroll already recorded becomes one absence row: positive deducts, negative refunds
  («استرداد خصم غياب — فترة yyyy-MM»).
- If the month's regular run is **not calculated yet**, the row belongs to that month's run.
- If the month's run is **frozen** (calculated and validated, under review, approved or posted), the row is **carried**: the
  agency's next calculated run applies it as «فرق غياب عن فترة yyyy-MM». The frozen run never changes.
- A cancelled or rejected run frees what it applied; the next calculation takes it again.

## Before a calculation: who is not closed

The run's page shows «الحضور»: the gate's mode, the employees in the run's scope, and those whose attendance of the period is not
closed (`GET api/payroll/runs/{id}/attendance-status` gives the same list; `GET api/payroll/attendance/receipts?periodStart=`
lists what payroll received).

| `Payroll:Attendance:Gate` | A calculation while someone is not closed |
|---|---|
| `Warn` (default) | runs, and leaves **one open audit record** on the run («تحذير بوابة الحضور»: the count and the employees). It is not a review: «مراجعة الرواتب» shows it with no «إغلاق» button, closing it is refused, and it never lets a run be posted. It goes when the run is calculated again after the attendance was closed |
| `Block` | is refused with the count; nothing is written |
| `Off` | runs with no check |

A correction run and a final settlement are not checked. The scope is who the run gives a line to: every employee with an active
pay profile. An employee payroll pays whom HR.Core does not hold in service in that month (hired later, or left earlier, by
HR.Core's dates) is listed too, and no close will ever clear him: under `Warn` that is the warning doing its work; under `Block`
it holds the run back until his pay profile and his HR.Core record agree. Check the list before raising the gate to `Block`.

## When something looks wrong

| Sign | Check | Action |
|------|-------|--------|
| A summary exists in HR.Core and payroll's receipts do not list it | RabbitMQ queue `payroll-web-attendance-summary-published` and its `_error` queue; HR.Core's outbox (`docs/runbooks/queue-backup.md`) | Restore the consumer; the outbox delivers again, and a redelivery is safe |
| «أُغلقت الفترة لبعض الموظفين بالتوازي؛ أعد المحاولة» | Two closes of the same employees ran at once | Close again: the second attempt skips who is already closed |
| An approval answers «تغيّر ملخص الحضور أثناء الاعتماد…» | Several decisions on one employee's summary at once | Approve again |
| A day must change and shows no «تعديل» | The day is locked | Ask for a correction; never change the row in the database |
| A leave-made day is wrong | The record is the leave's | Cancel or change the leave; the day follows |

**Never** delete or update a summary, a closure, a correction, a receipt or an applied absence row by SQL: the versions are the
record of what payroll was told, and an applied row is part of a run's result.

## Read-only inventory

```bash
pwsh -NoProfile -File scripts/lva057-validate.ps1 -Inventory
```

On the demo server: `-PgContainer hrm-postgresql -PgUser hrm_admin -SshHost hrm-vps`. It prints counts and checksums only.
