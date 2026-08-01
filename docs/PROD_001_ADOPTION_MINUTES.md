# PROD-001 — Program adoption minutes (policy-platform production binding)

| Field | Value |
|---|---|
| Card | PROD-001 (card 80) |
| Date | 2026-07-30 |
| Decision | Adopt lab policy-platform as startup/CI binding; LivePolicyEngineClaim=false |

## Claim gates

| Claim | Requires |
|---|---|
| `LivePolicyEngineClaim=true` | PROD-001a + live-policy-engine Satisfied |
| Live workflow runtime | PROD-001b + live-workflow-runtime |
| Live form designer | PROD-001c + live-form-designer |
| Live agency override product | PROD-001d |
| WF-001/WF-002 / ApprovalStep | PROD-001e |

## Cross-card

STR-004 nofork matrix blocker PROD-001 flipped to Satisfied with this binding; ONB-001 remains Blocked.
