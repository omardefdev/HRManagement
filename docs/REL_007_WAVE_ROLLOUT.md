# REL-007 — National Wave Rollout and Hypercare

## Purpose

Define national expansion by waves (pilot, sector, distributed) with entry, exit, and stop criteria; command center operations; daily reconciliation; hypercare; rollback; and capacity gates before the next wave.

## Scope

- Wave types: small pilot, large sector, distributed national
- Entry criteria: prior wave exit, capacity, migration rehearsal, training, support, rollback plan
- Exit criteria: 3 payroll cycles, zero Sev-1 data defects, 30-day SLO, adoption, CSAT, reconciliation
- Stop criteria: financial variance, Sev-1 defect, duplicate payment, identity outage
- Command center and daily reconciliation
- 30-day hypercare with lessons learned
- Rollback rehearsal and independent council approval for next wave

## Wave Model

See `docs/rel/wave-entry-exit-criteria.json` for the structured criteria.

### Wave Types

| Type | Max Agencies | Max Employees |
|------|--------------|---------------|
| pilot-small | 3 | 5,000 |
| sector-large | 15 | 150,000 |
| national-distributed | 50 | 500,000 |

### Entry Criteria (mandatory)

- Prior wave exit met or council waiver
- Capacity headroom ≥ 50% (SYR-002)
- Migration rehearsal passed twice
- Training ≥ 90% for privileged roles
- Support on-call roster staffed
- Rollback plan signed and rehearsed

### Exit Criteria (mandatory)

- ≥ 3 consecutive successful payroll cycles
- Zero Severity-1 data defects
- SLO met 30 consecutive days
- Adoption ≥ 80%
- Support CSAT ≥ 4.0/5.0
- Daily reconciliation variance = 0 unexplained

### Stop Criteria

| Trigger | Action |
|---------|--------|
| Unexplained financial variance > 0.01% | Halt wave |
| Severity-1 data defect | Halt wave |
| Duplicate payment | Halt wave + rollback |
| Identity outage > 24h without procedure | Halt expansion |

## Hypercare

- Duration: 30 days post wave launch
- Daily reconciliation required
- Command center 24×7 for first 14 days
- Lessons learned feed back to ONB-002 template

## Acceptance Criteria Mapping

- [x] ≥ 3 payroll cycles per wave — XC-01
- [x] Zero Sev-1 data defect — XC-02 / SC-02
- [x] SLO ≥ 30 days — XC-03
- [x] Adoption/support thresholds — XC-04, XC-05
- [x] Rollback rehearsed — rollback policy
- [x] Independent council opens next wave — `independentCouncilApprovalForNextWave`

## Artifacts

| File | Purpose |
|------|---------|
| `docs/REL_007_WAVE_ROLLOUT.md` | This document |
| `docs/rel/wave-entry-exit-criteria.json` | Entry/exit/stop criteria |
| `scripts/rel007-validate.ps1` | Validates REL-007 artifacts |
| `Rel007WaveTests.cs` | Unit tests |

## Validation

- `scripts/rel007-validate.ps1`
- `Rel007WaveTests`
