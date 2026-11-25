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
| P1 model, calendar, summary, migration | in progress | |
| P2 one writer | code written, not built | |
| P3 close and contract | | |
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

P1: `dotnet build HRManagement.slnx -warnaserror`; add migration `LVA057_AttendancePeriods` (HR.Core) with the fill under lifted
`FORCE`; `Lva057AttendanceSummaryCalculatorTests` and `Lva057MigrationPostgreSqlTests`; commit P1 with only its files (the P2 code
already in the tree — the writer, the controller, the views, the consumers, the reconciler — is committed in P2).
