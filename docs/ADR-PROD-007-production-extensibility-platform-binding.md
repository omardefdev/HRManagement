# ADR-PROD-007 — Production extensibility-platform binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-007 (card 86) |
| Date | 2026-07-30 |
| Kind | Production binding (not live extension runtime / SDK) |

## Decision

| Topic | Choice |
|---|---|
| SoT | `docs/prod-extensibility/extensibility-matrix.json` |
| Runtime | `ProdExtensibility` in ServiceDefaults; `AddProdExtensibility()` on Gateway / Payroll / HR.Core |
| Live claim | `LiveExtensibilityClaim=false` until PROD-007a–e |
| Soft blockers | INT-002/SUP-001/STR-004 Satisfied; live-extension-runtime / live-partner-sdk / live-webhook-delivery Blocked; EXT-006/IOP-001/REL-004 NotStarted |
| Cross-card | EXT-005 and PROD-006 blockers for PROD-007 → Satisfied |

## Consequences

- Staging/Production fail-fast on incomplete extensibility contracts or live claim.
- Live plugin host, partner SDK, and webhook delivery remain out of scope.
- EXT-006 / IOP-001 / REL-004 stay NotStarted and must not be claimed Done by this binding.
