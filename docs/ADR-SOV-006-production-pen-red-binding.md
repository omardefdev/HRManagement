# ADR-SOV-006 — Production pen-red binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | SOV-006 (card 92) |
| Date | 2026-07-30 |
| Kind | Production binding (not live pen/red/purple) |

## Decision

| Topic | Choice |
|---|---|
| SoT | `docs/sov-pentest/pen-red-matrix.json` |
| Runtime | `SovPentest` in ServiceDefaults; `AddSovPentest()` after `AddSovThreat()` on Gateway / Payroll / HR.Core |
| Live claim | `LivePenClaim=false` until SOV-006a |
| Soft blockers | SOV-001/004/005/OBS-003/REL-001 Satisfied; live-pen/red/purple/remediation Blocked; NAT-001/NAT-002 NotStarted |
| Cross-card | SOV-005 blocker SOV-006 → Satisfied; do not raise `LiveThreatModelClaim` |

## Consequences

- Staging/Production fail-fast on incomplete pen-red contracts or live pen claim.
- Live independent assessment, red/purple exercises, and remediation retest remain out of scope.
- NAT-001 / NAT-002 / FRD-001 stay open; `LiveThreatModelClaim` unchanged. SOV-007 is a separate vendor-exit binding card.
