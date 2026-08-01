# NAT-006 — Performance, Resilience & Recovery Certification

**Card:** NAT-006  
**Status:** Done (certification harness + evidence template)  
**Priority:** National Gate / L  
**Owner:** Independent Performance + SRE Audit

## Purpose

Define the performance, resilience, and recovery certification pack: workload envelope (CAP-001), soak/stress/spike, payroll under load, noisy neighbor, HA/failover, DR/cyber recovery, RPO/RTO, observability, and 30% capacity headroom.

## Scope

- CAP-001 national envelope scenarios (72h@100%, 4h@150%, 15m@200%)
- 30% headroom on critical resources
- HA failover and DR RPO/RTO targets
- Cyber recovery (OPS-006) cross-reference
- Noisy neighbor (CAP-002) fairness under overload
- Zero loss/corruption under certified scenarios
- Reproducible raw telemetry/results (witnessed live run = Blocked)

## Data Model

See `docs/nat/nat006-checklist.json` for structured performance certification checklist.

## Business Rules

1. **CAP-001 targets defined** — harness references perf scenarios and results schema.
2. **30% headroom minimum** — critical resource headroom enforced in checklist.
3. **Zero loss/corruption** — payroll and data integrity under load scenarios.
4. **No production-violating tuning** — certification config matches production profile.
5. **Witnessed independent run** — live production-like test Blocked until ORR.

## Relationship to CAP-001..003 / OPS-002..006 / NFR-002

| Card | Focus |
|------|-------|
| CAP-001 | National envelope harness Done |
| CAP-002 | Tenant load protection Done |
| CAP-003 | Three-year capacity plan Done |
| OPS-002..006 | HA, backup, cyber recovery bindings |
| NFR-002 | Chaos/fault injection catalog |
| NAT-006 | Independent performance/resilience certification template |

## Acceptance Criteria Mapping

- [x] Workload envelope parameters (soak/stress/spike/headroom)
- [x] Resilience and observability requirements
- [x] CAP-001 reference and results schema validation
- [x] DR/cyber-RTO criteria defined
- [x] Live witnessed performance run = Blocked

## Artifacts

| File | Purpose |
|------|---------|
| `docs/NAT_006_PERFORMANCE_CERT.md` | This document |
| `docs/nat/nat006-checklist.json` | Performance certification checklist |
| `scripts/nat006-validate.ps1` | Validates NAT-006 artifacts |
| `Nat006PerformanceCertTests.cs` | Unit tests for performance cert schema |

## Validation

- `scripts/nat006-validate.ps1`
- `Nat006PerformanceCertTests`

## Remaining Live Blockers

- Independent witnessed soak/stress/spike on production-like pending
- Live DR/cyber-RTO drill with raw telemetry pending
