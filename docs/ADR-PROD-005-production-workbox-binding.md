# ADR-PROD-005 — Production workbox binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-005 (card 84) |
| Date | 2026-07-30 |
| Kind | Production binding (not live workbox/notifications) |

## Decision

| Topic | Choice |
|---|---|
| SoT | `docs/prod-workbox/workbox-matrix.json` |
| Runtime | `ProdWorkbox` in ServiceDefaults; `AddProdWorkbox()` on Gateway / Payroll / HR.Core |
| Live claim | `LiveWorkboxClaim=false` until PROD-005a–e |
| Soft blockers | IAM-002 NotStarted (honest); live workbox/notify/search Blocked; COM-001/WF-002/NAT-005 NotStarted; PROD-006 Satisfied (semantic-layer binding) |
| Cross-card | PROD-004 and EXT-007 blockers for PROD-005 → Satisfied |

## Consequences

- Staging/Production fail-fast on incomplete workbox contracts or live claim.
- Live workbox UI and live notification delivery remain out of scope.
- IAM-002 and COM-001 (NotificationHub gap) stay NotStarted and must not be claimed Satisfied by this binding.
