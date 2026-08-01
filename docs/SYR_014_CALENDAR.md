# SYR-014 — Syrian Calendar, Continuous Operations, and Shifts

## Purpose

Define effective-dated holiday calendars per Decree 188/2025 with fixed and variable holidays, local/emergency holiday decisions, continuous 24/7 operations, shift policies, compensatory time, and payroll recalculation rules under `Asia/Damascus` timezone.

## Scope

- Decree 188/2025 fixed public holidays (not hard-coded in application code)
- Variable holidays (Eid, etc.) from late announcements with retroactive application
- Local and emergency holidays by official decision at governorate/agency/site level
- 24/7 agencies (hospitals, emergency, security) never fully closed
- Shift crossing midnight; compensatory and overtime from rule pack
- Effective-dated calendar assignment per agency/site/legal regime
- Payroll recalculation before approval when holidays change retroactively
- NTP time source with DST observed

## Artifacts

| File | Purpose |
|------|---------|
| `docs/SYR_014_CALENDAR.md` | This document |
| `docs/syr/holiday-calendar-policy.json` | Holiday and calendar policy structure |
| `scripts/syr014-validate.ps1` | Validates SYR-014 artifacts and structure |
| `Syr014CalendarTests.cs` | Unit tests for calendar policy schema and validation |

## Acceptance Criteria Mapping

- [x] No hard-coded holiday list in application code
- [x] Late-announced holidays apply with controlled retroactive effect
- [x] 24/7 agencies not fully closed
- [x] Overtime/compensatory time from rule pack
- [x] Recalculate payroll impact before approval
- [x] Every calendar day has signed source

## Validation

- `scripts/syr014-validate.ps1`
- `Syr014CalendarTests`
