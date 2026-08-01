# ADR-PROD-004 — Production remote-branch binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-004 (card 83) |
| Date | 2026-07-30 |
| Kind | Production binding (not live PWA/offline fleets) |

## Decision

| Topic | Choice |
|---|---|
| SoT | `docs/prod-branch/remote-branch-matrix.json` |
| Runtime | `ProdBranch` in ServiceDefaults; `AddProdBranch()` on Gateway / Payroll / HR.Core |
| Live claim | `LiveRemoteBranchClaim=false` until PROD-004a–e |
| Soft blockers | live fleets Blocked; L10N-001/MOB-001/PROD-005/SYR-013 NotStarted |
| Cross-card | PROD-003 ESS matrix blocker PROD-004 → Satisfied |

## Consequences

- Staging/Production fail-fast on incomplete remote-branch contracts or live claim.
- Live PWA fleets and field offline remain out of scope.
- L10N-001 / MOB-001 / SYR-013 stay NotStarted and must not be claimed Satisfied by this binding.
