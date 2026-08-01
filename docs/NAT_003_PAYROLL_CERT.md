# NAT-003 — Payroll & Legal Service Certification

**Card:** NAT-003  
**Status:** Done (certification harness + evidence template)  
**Priority:** National Gate / L  
**Owner:** Finance + Legal + External Audit

## Purpose

Define the payroll and legal service certification pack: golden cases, boundary/retroactive scenarios, parallel runs, reconciliation, approvals, payments, reversals, separation benefits, and law/config/version evidence.

## Scope

- Golden payroll cases with legal rule provenance
- ≥3 consecutive cycles at target volume (live = Blocked)
- Zero unexplained financial variance
- Maker/checker separation proof
- Bank and treasury reconciliation
- Historical replay match
- Separation benefits and reversal audit trails

## Data Model

See `docs/nat/nat003-checklist.json` for structured payroll certification checklist.

## Business Rules

1. **Three consecutive cycles required** — harness defines criteria; live cycles Blocked.
2. **Zero unexplained variance** — reconciliation tolerance 0%.
3. **Every rule has legal provenance** — config version and law reference evidence.
4. **Maker/checker enforced** — no single-actor payroll approval path.
5. **Historical replay match** — recalculation produces identical results.

## Relationship to PAY-001..003 / EXT-003 / CAP-001

| Card | Focus |
|------|-------|
| PAY-001..003 | Payroll engine and rules Done |
| EXT-003 | Treasury payment integration |
| CAP-001 | National workload envelope |
| NAT-003 | Independent payroll certification template |

## Acceptance Criteria Mapping

- [x] Golden case catalog with boundary/retroactive/reversal cases
- [x] Three-cycle requirement and zero-variance criteria
- [x] Bank/treasury reconciliation requirements
- [x] Maker/checker and legal evidence slots
- [x] Live three-cycle run = Blocked until wave authorized

## Artifacts

| File | Purpose |
|------|---------|
| `docs/NAT_003_PAYROLL_CERT.md` | This document |
| `docs/nat/nat003-checklist.json` | Payroll certification checklist |
| `scripts/nat003-validate.ps1` | Validates NAT-003 artifacts |
| `Nat003PayrollCertTests.cs` | Unit tests for payroll cert schema |

## Validation

- `scripts/nat003-validate.ps1`
- `Nat003PayrollCertTests`

## Remaining Live Blockers

- Three consecutive live payroll cycles at target volume pending
- Independent bank/treasury reconciliation pending
