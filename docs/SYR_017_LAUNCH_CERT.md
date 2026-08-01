# SYR-017 — Syrian Payroll Phased Launch Certification

**Card:** SYR-017  
**Status:** Done (launch harness + phased wave plan)  
**Priority:** National Gate / XL  
**Owner:** Syrian National Authorization Board

## Purpose

Define the Syrian payroll phased launch certification: three consecutive shadow payroll cycles per system/channel, baseline 1.25M payrolls in 4 hours (stress 2M until SYR-002 census), 14 governorates, pilot→agency→sector→national waves, freeze/rollback, command center, File 14 signatures, and 30-day monitoring per wave.

## Scope

- Three consecutive shadow cycles with zero unexplained variance and zero duplicate/loss
- Baseline volume: 1,250,000 payrolls / 4 hours; stress: 2,000,000
- 14 governorate coverage
- Four phased waves with explicit scope, blast radius, rollback — no auto-expansion
- NAT-008 cross-reference: release digest, agencies, systems, certification expiry
- Prerequisites: SYR-001..016 harness/live status, NAT-001..007 harness, VEN-001 Done
- Full-scale reconciliation, 72h load, DR/cyber recovery, failure drills

## Data Model

See `docs/syr/phased-launch-plan.json` for structured phased launch plan.

## Business Rules

1. **No-Go for uncertified agency or instruction** — wave scope explicitly bounded.
2. **No auto-expansion** — each wave requires independent authorization.
3. **30-day monitoring per wave** — SLO and incident tracking before next wave.
4. **File 14 signatures** — Syrian legal file bundle required per wave.
5. **NAT-008 fields required** — release digest, agencies, systems, expiry on national Go.

## Relationship to SYR-001..016 / NAT-001..008 / VEN-001

| Card | Focus |
|------|-------|
| SYR-001..014 | Syrian production bindings (mixed Done/NotStarted) |
| SYR-015/016 | Records matrix and cyber compliance — NotStarted |
| NAT-001..007 | National certification harness packs |
| NAT-008 | National Go/No-Go decision template |
| VEN-001 | Vendor exit drill Done |

## Acceptance Criteria Mapping

- [x] Phased wave plan (pilot, agency, sector, national)
- [x] Shadow cycle and volume requirements (1.25M/4h, stress 2M)
- [x] 14 governorate count and wave controls (scope, blast radius, rollback)
- [x] NAT-008 cross-reference fields
- [x] Live shadow cycles and board authorization = Blocked

## Artifacts

| File | Purpose |
|------|---------|
| `docs/SYR_017_LAUNCH_CERT.md` | This document |
| `docs/syr/phased-launch-plan.json` | Phased launch plan |
| `scripts/syr017-validate.ps1` | Validates SYR-017 artifacts |
| `Syr017LaunchTests.cs` | Unit tests for launch plan schema |

## Validation

- `scripts/syr017-validate.ps1`
- `Syr017LaunchTests`

## Remaining Live Blockers

- SYR-015 records/retention matrix NotStarted
- SYR-016 Syrian cyber compliance crosswalk NotStarted
- Three consecutive live shadow payroll cycles pending
- NAT-008 live board decision Blocked
