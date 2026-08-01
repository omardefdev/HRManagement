# ADR-SOV-001 — Production control-baseline binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | SOV-001 (card 87) |
| Date | 2026-07-30 |
| Kind | Production binding (not live independent ATO) |

## Decision

| Topic | Choice |
|---|---|
| SoT | `docs/sov-baseline/control-baseline-matrix.json` |
| Runtime | `SovBaseline` in ServiceDefaults; `AddSovBaseline()` on Gateway / Payroll / HR.Core |
| Live claim | `LiveAtoClaim=false` until SOV-001a |
| Soft blockers | STR-002/SUP-001/GOV-001 Satisfied; live-independent-ato / continuous-assessment-workers / live-poam-system Blocked; SYR-001/NAT-001 NotStarted; SOV-002 Satisfied (data-sovereignty binding) |
| Cross-card | STR-002 `control-catalog.json` blocker SOV-001 → Satisfied; `CTRL-*` Blocked-Legal unchanged |

## Consequences

- Staging/Production fail-fast on incomplete control-baseline contracts or live ATO claim.
- Live independent ATO remains out of scope.
- SYR-001 / NAT-001 stay NotStarted; SOV-002 platform binding Satisfied (live egress later).
- STR-002 Blocked-Legal controls are not flipped to Implemented by this binding.
