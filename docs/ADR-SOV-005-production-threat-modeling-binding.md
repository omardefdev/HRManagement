# ADR-SOV-005 — Production threat-modeling binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | SOV-005 (card 91) |
| Date | 2026-07-30 |
| Kind | Production binding (not live independent TM review) |

## Decision

| Topic | Choice |
|---|---|
| SoT | `docs/sov-threat/threat-model-matrix.json` |
| Runtime | `SovThreat` in ServiceDefaults; `AddSovThreat()` after `AddSovZt()` on Gateway / Payroll / HR.Core |
| Live claim | `LiveThreatModelClaim=false` until SOV-005a |
| Soft blockers | STR/TEN/INT/SOV-004 Satisfied; live-tm-review/attack-tree/misuse/residual Blocked; SOV-006 Satisfied (pen-red binding); NAT-001 NotStarted |
| Cross-card | SOV-004 blocker SOV-005 → Satisfied; do not raise `LivePamClaim` |

## Consequences

- Staging/Production fail-fast on incomplete threat-model contracts or live TM claim.
- Live independent review, attack-tree walkthrough, misuse sync, and residual signoff remain out of scope.
- SOV-006 platform binding Satisfied (live pen later); NAT-001 / FRD-001 stay open; `LivePamClaim` unchanged.
