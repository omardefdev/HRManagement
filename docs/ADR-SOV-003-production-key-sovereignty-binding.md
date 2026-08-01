# ADR-SOV-003 — Production key-sovereignty binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | SOV-003 (card 89) |
| Date | 2026-07-30 |
| Kind | Production binding (not live HSM/ceremony/time) |

## Decision

| Topic | Choice |
|---|---|
| SoT | `docs/sov-keys/key-sovereignty-matrix.json` |
| Runtime | `SovKeys` in ServiceDefaults; `AddSovKeys()` on Gateway / Payroll / HR.Core |
| Live claim | `LiveKeyCustodyClaim=false` until SOV-003a |
| Soft blockers | OPS/STR/SOV-002/EXT-004 Satisfied; SEC-004/live-hsm/ceremony/time Blocked; SOV-004 Satisfied (zero-trust binding); SIG-001/NAT-001 NotStarted |
| Cross-card | SOV-002 + EXT-004 blockers SOV-003 → Satisfied; `hsm-prod` remains Blocked |

## Consequences

- Staging/Production fail-fast on incomplete key-sovereignty contracts or live custody claim.
- Live HSM/ceremony/trusted time remain out of scope.
- SEC-004 stays Blocked honestly; SOV-004 platform binding Satisfied (live PAM later); SIG-001 / NAT-001 stay NotStarted.
- EXT-004 `hsm-prod` and live PKI claims unchanged.
