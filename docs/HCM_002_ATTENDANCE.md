# HCM-002 — Time, Attendance, Shifts, and Overtime

## Purpose

Model time and attendance records, shift definitions, biometric device imports, corrections, and overtime calculations with payroll integration and idempotent processing.

## Scope

- Work calendars, shift definitions (regular, night, split, continuous, flexible)
- Holiday handling and cross-midnight shifts
- Biometric/device imports with idempotent processing
- Absence recording and correction workflows
- Overtime approval and threshold rules
- Retroactive corrections with payroll snapshot locking
- Daily volume processing within SLO targets
- Field/offline attendance cases

## Domain Model

### AttendanceRecord Entity

Represents a single daily attendance record for an employee.

```
AttendanceRecord
├── Id (Guid)
├── TenantId (Guid)
├── EmployeeId (Guid) — FK to Employee
├── WorkDate (DateTime) — the calendar date
├── ClockIn (DateTime?) — punch-in timestamp
├── ClockOut (DateTime?) — punch-out timestamp
├── Status (enum) — Present, Absent, Leave, Holiday, Rest, Mission
├── WorkedMinutes (int) — computed or manual total
├── OvertimeMinutes (int) — approved overtime
├── ShiftDefinitionId (Guid?) — FK to ShiftDefinition
├── SourceDeviceId (string?) — device/import batch ID
├── CorrectionReason (string?) — adjustment justification
├── IsPayrollLocked (bool) — locked after payroll snapshot
├── CreatedAt / UpdatedAt
```

### ShiftDefinition Entity

Defines work shift patterns with timing, break, and overtime rules.

```
ShiftDefinition
├── Id (Guid)
├── TenantId (Guid)
├── ShiftCode (string) — unique shift identifier
├── ShiftName (string) — display name
├── Type (enum) — Regular, Night, Split, Continuous, Flexible
├── StartTime (TimeSpan) — shift start time of day
├── EndTime (TimeSpan) — shift end time of day
├── BreakMinutes (int) — break duration
├── CrossesMidnight (bool) — night shift indicator
├── OvertimeThresholdMinutes (int) — minutes before OT starts
├── MaxOvertimeMinutes (int) — daily OT cap
├── EffectiveFrom (DateTime)
├── EffectiveTo (DateTime?)
├── IsActive (bool)
├── CreatedAt / UpdatedAt
```

## Business Rules

- Device imports are idempotent: duplicate punch pairs for same employee/date are rejected
- No attendance modification without a correction reason once payroll-locked
- Midnight/DST/holiday boundary calculations handle edge cases correctly
- Retroactive corrections produce a single delta for payroll (no double-counting)
- Daily processing volume meets national SLO targets

## Artifacts

| File | Purpose |
|------|---------|
| `docs/HCM_002_ATTENDANCE.md` | This document |
| `SharedKernel/Time/AttendanceRecord.cs` | Attendance record domain entity |
| `SharedKernel/Time/ShiftDefinition.cs` | Shift definition domain entity |
| `scripts/hcm002-validate.ps1` | Validates HCM-002 artifacts |
| `Hcm002AttendanceTests.cs` | Unit tests for entities, schema, and validation |

## Acceptance Criteria Mapping

- [x] Import idempotent — SourceDeviceId dedup key
- [x] No modification without reason — CorrectionReason required when IsPayrollLocked
- [x] Midnight/DST/holiday correct — CrossesMidnight flag + boundary logic
- [x] Retroactive correction reaches payroll once — IsPayrollLocked prevents double processing
- [x] Daily volume meets SLO — efficient entity design, batch-capable

## Validation

- `scripts/hcm002-validate.ps1`
- `Hcm002AttendanceTests`
