# NAT-007 — Pilot Wave & Operational Readiness Certification

**Card:** NAT-007  
**Status:** Done (certification harness + evidence template)  
**Priority:** National Gate / L  
**Owner:** Pilot Board + Service Management

## Purpose

Define the pilot wave and operational readiness certification pack: agency UAT, adoption, support, SLO, incidents, payroll/data quality, integrations, training, rollback, lessons learned, ORR, staffing, and vendor readiness.

## Scope

- REL-002 and REL-003 pilot gate requirements
- ≥3 consecutive successful payroll cycles
- ≥30 days SLO window without Severity-1
- Support SLA thresholds
- Critical lessons closed before expansion
- Pilot scorecard domains (12 areas)
- Operational drill and agency sign-offs (live = Blocked)

## Data Model

See `docs/nat/nat007-checklist.json` for structured pilot certification checklist.

## Business Rules

1. **REL-002/003 Satisfied** — pilot gate pack and support model bindings complete.
2. **Three payroll cycles** — harness defines requirement; live cycles Blocked.
3. **30-day SLO window** — production-like stability before wave expansion.
4. **Zero Severity-1** — no open Severity-1 data or service defects.
5. **Critical lessons closed** — pilot board blocks expansion until closure.

## Relationship to REL-002/003 / SRE-001..003 / SUP-002..004 / MIG-007

| Card | Focus |
|------|-------|
| REL-002 | Pilot Go/No-Go gate Done |
| REL-003 | Pilot support model Done |
| SRE-001..003 | On-call, ORR, incident command Done |
| SUP-002..004 | Service desk, competency, contracts Done |
| MIG-007 | Onboarding binding |
| NAT-007 | Independent pilot/ops readiness certification template |

## Acceptance Criteria Mapping

- [x] Pilot scorecard domains defined (12 areas)
- [x] REL-002/003 and three-cycle/SLO requirements
- [x] Zero Severity-1 harness criteria
- [x] REL pilot scope matrix cross-reference
- [x] Live pilot scorecard and operational drill = Blocked

## Artifacts

| File | Purpose |
|------|---------|
| `docs/NAT_007_PILOT_CERT.md` | This document |
| `docs/nat/nat007-checklist.json` | Pilot certification checklist |
| `scripts/nat007-validate.ps1` | Validates NAT-007 artifacts |
| `Nat007PilotCertTests.cs` | Unit tests for pilot cert schema |

## Validation

- `scripts/nat007-validate.ps1`
- `Nat007PilotCertTests`

## Remaining Live Blockers

- Agency UAT sign-offs and live pilot scorecard pending
- Operational drill with command center pending
