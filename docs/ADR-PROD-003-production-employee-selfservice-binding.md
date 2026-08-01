# ADR-PROD-003 — Production employee-selfservice binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-003 (card 82) |
| Date | 2026-07-30 |
| Kind | Production binding (not live ESS/MSS portals) |

## Decision

| Topic | Choice |
|---|---|
| SoT | `docs/prod-ess/employee-selfservice-matrix.json` |
| Runtime | `ProdEss` in ServiceDefaults; `AddProdEss()` on Gateway / Payroll / HR.Core |
| Live claim | `LiveEmployeeSelfServiceClaim=false` until PROD-003a–e |
| Soft blockers | IAM-002 NotStarted (honest); live portals / usage Blocked; ESS-001/MSS-001/PROD-004 NotStarted |
| Cross-card | PROD-002 and STR-007 blockers for PROD-003 → Satisfied |

## Consequences

- Staging/Production fail-fast on incomplete ESS contracts or live claim.
- Live employee/manager portals and live usage KPI remain out of scope.
- IAM-002 stays NotStarted and must not be claimed Satisfied by this binding.
