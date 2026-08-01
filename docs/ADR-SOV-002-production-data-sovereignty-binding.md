# ADR-SOV-002 — Production data-sovereignty binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | SOV-002 (card 88) |
| Date | 2026-07-30 |
| Kind | Production binding (not live egress/JIT/deletion) |

## Decision

| Topic | Choice |
|---|---|
| SoT | `docs/sov-data/data-sovereignty-matrix.json` |
| Runtime | `SovData` in ServiceDefaults; `AddSovData()` on Gateway / Payroll / HR.Core |
| Live claim | `LiveSovereigntyClaim=false` until SOV-002a |
| Soft blockers | GOV/OPS/STR/SOV-001 Satisfied; live-egress/support/deletion Blocked; SYR-008/NAT-001 NotStarted; SOV-003 Satisfied (key-sovereignty binding) |
| Cross-card | SOV-001 `control-baseline-matrix.json` blocker SOV-002 → Satisfied |

## Consequences

- Staging/Production fail-fast on incomplete data-sovereignty contracts or live sovereignty claim.
- Live egress/JIT/deletion remain out of scope.
- SYR-008 / NAT-001 stay NotStarted; SOV-003 platform binding Satisfied (live HSM later).
- `LiveAtoClaim` and STR-002 Blocked-Legal controls are unchanged.
