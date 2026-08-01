# ADR-SOV-004 — Production zero-trust binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | SOV-004 (card 90) |
| Date | 2026-07-30 |
| Kind | Production binding (not live PAM/JIT/workstation/SOC) |

## Decision

| Topic | Choice |
|---|---|
| SoT | `docs/sov-zt/zero-trust-matrix.json` |
| Runtime | `SovZt` in ServiceDefaults; `AddSovZt()` after `AddSovKeys()` on Gateway / Payroll / HR.Core |
| Live claim | `LivePamClaim=false` until SOV-004a |
| Soft blockers | IAM-003/004/SOV-003 Satisfied; live-pam/workstation/break-glass/recert Blocked; SOV-005 Satisfied (threat-modeling binding); NAT-001 NotStarted |
| Cross-card | SOV-003 blocker SOV-004 → Satisfied; do not raise `LiveKeyCustodyClaim` |

## Consequences

- Staging/Production fail-fast on incomplete zero-trust contracts or live PAM claim.
- Live PAM/JIT, admin workstation enforcement, SOC break-glass, and operational recertification remain out of scope.
- SOV-005 platform binding Satisfied (live TM review later); NAT-001 stays NotStarted; `LiveKeyCustodyClaim` unchanged.
